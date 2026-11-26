#!/usr/bin/env python3
# PSD / PSB → spec.json（diff.py --spec 直接读）+ 预览图 + 可选导出图层 PNG。
# 从源文件精确读出：每个图层的矩形、层级路径、是否可见、状态分组；
# 文字图层：内容、字号（已乘变换矩阵，单位=画布像素）、字体、填充色、字间距、行距、对齐；
# 图层样式：描边（大小/颜色/位置）、投影（距离/角度/大小/颜色/不透明度）、颜色叠加、渐变叠加（色标）、内阴影、外发光。只收「已启用」的样式。
# 用法：python -I psd_to_spec.py 效果图.psb --out <目录> [--all-states] [--export-layers]
#   --all-states   连隐藏的状态组（如「大奖切换01」）一起导出，元素带 state 字段
#   --export-layers 把可见的像素/智能对象图层导出成 PNG（缺切图时可直接用）
import argparse, json, math, os, re, sys
from psd_tools import PSDImage

ap = argparse.ArgumentParser()
ap.add_argument("psd")
ap.add_argument("--out", required=True)
ap.add_argument("--all-states", action="store_true")
ap.add_argument("--export-layers", action="store_true")
a = ap.parse_args()
os.makedirs(a.out, exist_ok=True)
psd = PSDImage.open(a.psd)
W, H = psd.size


def rgb(c):
    """psd-tools 的颜色是 Descriptor（类 dict，但不是 dict 子类）：RGB 0-255 或灰度 0-100。"""
    if c is None:
        return None
    try:
        keys = list(c.keys())
    except AttributeError:
        return None
    g = lambda k: float(c[k])
    if b"Rd  " in keys:
        return [round(g(b"Rd  ")), round(g(b"Grn ")), round(g(b"Bl  "))]
    if b"Gry " in keys:
        v = round(g(b"Gry ") * 2.55); return [v, v, v]
    return None


def eff_list(l):
    out = {}
    if not l.has_effects():
        return out
    for e in l.effects:
        if not getattr(e, "enabled", True):
            continue
        n = e.__class__.__name__
        d = {"opacity": round(float(getattr(e, "opacity", 100)), 1)}
        if hasattr(e, "color"):
            d["color"] = rgb(e.color)
        if n == "Stroke":
            d.update(size=float(e.size), position=str(e.position).split(".")[-1].split(":")[0], fill=str(e.fill_type).split(".")[-1].split(":")[0])
            if e.gradient is not None:
                d["gradient"] = grad(e.gradient)
        elif n in ("DropShadow", "InnerShadow"):
            ang = float(e.angle)
            dist = float(e.distance)
            d.update(distance=dist, angle=ang, size=float(e.size), choke=float(getattr(e, "choke", 0)),
                     offset=[round(-dist * math.cos(math.radians(ang)), 2), round(dist * math.sin(math.radians(ang)), 2)])  # 屏幕坐标 y 向下
        elif n == "OuterGlow":
            d.update(size=float(e.size))
        elif n == "GradientOverlay":
            d.update(angle=float(e.angle), gradient=grad(e.gradient), reversed=bool(e.reversed))
        out.setdefault(n, []).append(d)
    return out


def grad(gd):
    try:
        stops = gd.get(b"Clrs") or []
        res = []
        for s in stops:
            res.append({"pos": round(float(s.get(b"Lctn", 0)) / 4096, 3), "color": rgb(s.get(b"Clr "))})
        return res
    except Exception:
        return None


def text_info(l):
    ed = l.engine_dict
    runs = ed["StyleRun"]["RunArray"]
    ss = runs[0]["StyleSheet"]["StyleSheetData"]
    xx, xy, yx, yy, tx, ty = l.transform
    scale = math.sqrt(abs(xx * yy - xy * yx)) or 1
    fonts = [str(f["Name"]) for f in l.resource_dict.get("FontSet", [])]
    fill = ss.get("FillColor", {}).get("Values")
    para = ed.get("ParagraphRun", {}).get("RunArray", [{}])[0].get("ParagraphSheet", {}).get("Properties", {})
    just = {0: "left", 1: "right", 2: "center"}.get(para.get("Justification", 0), "justify")
    info = {
        "text": l.text.replace("\r", "\n").strip("\n"),
        "fontPx": round(float(ss.get("FontSize", 0)) * scale, 2),
        "font": str(fonts[ss.get("Font", 0)]) if fonts and ss.get("Font", 0) < len(fonts) else None,
        "fill": [round(v * 255) for v in fill[1:4]] if fill else None,
        "tracking": ss.get("Tracking", 0),
        "leadingPx": round(float(ss.get("Leading", 0)) * scale, 2) if not ss.get("AutoLeading", True) else None,
        "align": just,
        "fauxBold": ss.get("FauxBold", False),
        "runs": len(runs),
    }
    if len(runs) > 1:
        info["runSizes"] = sorted({round(float(r["StyleSheet"]["StyleSheetData"].get("FontSize", 0)) * scale, 1) for r in runs})
    return info


def visible_chain(l):
    while l is not None and l is not psd:
        if not l.visible:
            return False
        l = l.parent
    return True


def path_of(l):
    names = []
    while l is not None and l is not psd:
        names.append(l.name)
        l = l.parent
    return "/".join(reversed(names))


def top_state(l):
    # 隐藏的顶层/二层组视为「状态」（效果图常把多状态叠在一个文件里）
    chain = []
    while l is not None and l is not psd:
        chain.append(l); l = l.parent
    for g in reversed(chain):
        if g.is_group() and not g.visible:
            return g.name
    return None


els, skipped = [], 0
safe = lambda s: re.sub(r"[^\w一-鿿.-]+", "_", s)[:60]
for l in psd.descendants():
    if l.is_group() or l.kind in ("levels", "huesaturation", "curves", "brightnesscontrast", "colorbalance", "solidcolorfill", "gradientfill", "patternfill"):
        continue
    vis = visible_chain(l)
    state = top_state(l)
    if not vis and not (a.all_states and state and l.visible):
        skipped += 1; continue
    x0, y0, x1, y1 = l.bbox
    if x1 - x0 <= 0 or y1 - y0 <= 0:
        continue
    e = {"name": l.name.strip(), "path": path_of(l), "layerKind": l.kind, "kind": "text" if l.kind == "type" else "image",
         "rect": [x0, y0, x1 - x0, y1 - y0], "visible": vis, "opacity": round(l.opacity / 255, 3)}
    if state:
        e["state"] = state
    if l.kind == "type":
        try:
            e.update(text_info(l))
        except Exception as ex:
            e["textError"] = str(ex)
    fx = eff_list(l)
    if fx:
        e["effects"] = fx
        if "Stroke" in fx:
            e["stroke"] = fx["Stroke"][0].get("color"); e["strokeW"] = fx["Stroke"][0].get("size")
        if "DropShadow" in fx:
            e["shadow"] = fx["DropShadow"][0]
        if "ColorOverlay" in fx and e["kind"] == "text":
            e["fill"] = fx["ColorOverlay"][0]["color"]  # 颜色叠加覆盖文字原色
    if a.export_layers and vis and l.kind in ("pixel", "smartobject", "shape"):
        try:
            im = l.composite()
            if im is not None:
                fn = os.path.join(a.out, "layers", f"{safe(l.name)}_{x0}_{y0}.png")
                os.makedirs(os.path.dirname(fn), exist_ok=True)
                im.save(fn); e["png"] = fn
        except Exception:
            pass
    els.append(e)

preview = os.path.join(a.out, "preview.png")
psd.composite().save(preview)
spec = {"source": os.path.abspath(a.psd), "width": W, "height": H, "preview": preview, "elements": els}
json.dump(spec, open(os.path.join(a.out, "spec.json"), "w"), ensure_ascii=False, indent=1, default=str)

# 人看的摘要
texts = [e for e in els if e["kind"] == "text" and e["visible"]]
L = [f"# {os.path.basename(a.psd)}  {W}x{H}", "", f"元素 {len(els)}（可见 {sum(e['visible'] for e in els)}，文字 {sum(e['kind'] == 'text' for e in els)}），跳过隐藏 {skipped}", "",
     "## 可见文字图层", "", "| 文字 | 矩形 | 字号px | 字体 | 填充 | 描边 | 投影偏移/大小/颜色 | 渐变叠加 |", "|---|---|---|---|---|---|---|---|"]
for e in texts:
    sh = e.get("shadow")
    go = e.get("effects", {}).get("GradientOverlay")
    L.append(f"| {e.get('text', '')[:24].replace(chr(10), ' / ')} | {e['rect']} | {e.get('fontPx')} | {e.get('font')} | {e.get('fill')} | "
             f"{(str(e.get('strokeW')) + 'px ' + str(e.get('stroke'))) if e.get('stroke') else ''} | "
             f"{(str(sh['offset']) + ' ' + str(sh['size']) + ' ' + str(sh.get('color'))) if sh else ''} | "
             f"{[s['color'] for s in go[0]['gradient']] if go and go[0].get('gradient') else ''} |")
states = sorted({e["state"] for e in els if e.get("state")})
if states:
    L += ["", "## 隐藏状态组", ""] + [f"- {s}" for s in states]
open(os.path.join(a.out, "spec.md"), "w").write("\n".join(L))
print("\n".join(L[:4]))
print(f"→ {a.out}/spec.json  spec.md  preview.png")
