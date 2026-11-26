#!/usr/bin/env python3
# Figma 节点 JSON → spec.json（与 psd_to_spec.py 同格式，diff.py --spec 直接读）。
# 输入：Figma REST `GET /v1/files/<key>/nodes?ids=<frameId>` 的响应，或 `GET /v1/files/<key>` 里的某个 Frame 节点。
#       （也可让 Figma 官方 MCP 的 get_design_context / get_metadata 导出后保存成 JSON。）
# 预览图：`GET /v1/images/<key>?ids=<frameId>&format=png&scale=1` 下载后用 --preview 指定。
# 用法：python -I figma_to_spec.py frame.json --out <目录> [--frame <节点id>] [--preview frame.png] [--width 768]
#   --width：把 Frame 缩放到该宽度（Figma 常用 375/390 宽设计稿，项目参考宽 768）
import argparse, json, os, sys

ap = argparse.ArgumentParser()
ap.add_argument("json")
ap.add_argument("--out", required=True)
ap.add_argument("--frame")
ap.add_argument("--preview")
ap.add_argument("--width", type=float, default=0)
a = ap.parse_args()
os.makedirs(a.out, exist_ok=True)
doc = json.load(open(a.json))


def find_frame(d):
    if "nodes" in d:  # /nodes 响应
        nodes = d["nodes"]
        key = a.frame or next(iter(nodes))
        return nodes[key]["document"]
    if "document" in d:
        stack = [d["document"]]
        while stack:
            n = stack.pop()
            if (a.frame and n.get("id") == a.frame) or (not a.frame and n.get("type") == "FRAME"):
                return n
            stack.extend(n.get("children", []))
    return d


frame = find_frame(doc)
fb = frame["absoluteBoundingBox"]
k = a.width / fb["width"] if a.width else 1.0
c255 = lambda c, op=1: [round(c["r"] * 255), round(c["g"] * 255), round(c["b"] * 255)] if c else None


def solid(paints):
    for p in paints or []:
        if p.get("visible", True) and p.get("type") == "SOLID":
            return c255(p["color"])
    return None


def gradient(paints):
    for p in paints or []:
        if p.get("visible", True) and p.get("type", "").startswith("GRADIENT"):
            return [{"pos": round(s["position"], 3), "color": c255(s["color"])} for s in p.get("gradientStops", [])]
    return None


els = []


def walk(n, path, visible):
    vis = visible and n.get("visible", True)
    p = f"{path}/{n['name']}" if path else n["name"]
    bb = n.get("absoluteRenderBounds") or n.get("absoluteBoundingBox")
    if n is not frame and bb and n.get("type") not in ("GROUP", "FRAME", "COMPONENT", "INSTANCE", "SECTION"):
        e = {"name": n["name"].strip(), "path": p, "layerKind": n["type"], "kind": "text" if n["type"] == "TEXT" else "image",
             "rect": [round((bb["x"] - fb["x"]) * k, 2), round((bb["y"] - fb["y"]) * k, 2), round(bb["width"] * k, 2), round(bb["height"] * k, 2)],
             "visible": vis, "opacity": n.get("opacity", 1)}
        fx = {}
        if n.get("strokes") and n.get("strokeWeight") and solid(n["strokes"]):
            fx["Stroke"] = [{"color": solid(n["strokes"]), "size": n["strokeWeight"] * k, "position": n.get("strokeAlign", "CENTER")}]
        for ef in n.get("effects", []):
            if not ef.get("visible", True):
                continue
            if ef["type"] in ("DROP_SHADOW", "INNER_SHADOW"):
                key = "DropShadow" if ef["type"] == "DROP_SHADOW" else "InnerShadow"
                spread = ef.get("spread", 0) * k
                fx.setdefault(key, []).append({"color": c255(ef["color"]), "offset": [ef["offset"]["x"] * k, ef["offset"]["y"] * k],
                                               "size": ef.get("radius", 0) * k + spread, "choke": 100 if ef.get("radius", 0) == 0 and spread > 0 else 0,
                                               "opacity": round(ef["color"].get("a", 1) * 100, 1)})
        g = gradient(n.get("fills"))
        if g:
            fx["GradientOverlay"] = [{"angle": 90, "gradient": g, "reversed": False}]
        if fx:
            e["effects"] = fx
            if "Stroke" in fx:
                e["stroke"], e["strokeW"] = fx["Stroke"][0]["color"], fx["Stroke"][0]["size"]
        if n["type"] == "TEXT":
            st = n.get("style", {})
            e.update(text=n.get("characters", ""), fontPx=round(st.get("fontSize", 0) * k, 2), font=st.get("fontPostScriptName") or st.get("fontFamily"),
                     fill=solid(n.get("fills")), tracking=st.get("letterSpacing", 0), leadingPx=round(st.get("lineHeightPx", 0) * k, 2) if st.get("lineHeightPx") else None,
                     align={"LEFT": "left", "RIGHT": "right", "CENTER": "center"}.get(st.get("textAlignHorizontal"), "justify"))
        els.append(e)
    for c in n.get("children", []):
        walk(c, p, vis)


walk(frame, "", True)
spec = {"source": os.path.abspath(a.json), "width": round(fb["width"] * k), "height": round(fb["height"] * k),
        "preview": os.path.abspath(a.preview) if a.preview else None, "elements": els}
json.dump(spec, open(os.path.join(a.out, "spec.json"), "w"), ensure_ascii=False, indent=1)
print(f"{frame['name']} {spec['width']}x{spec['height']} 元素 {len(els)}（文字 {sum(e['kind'] == 'text' for e in els)}） → {a.out}/spec.json")
