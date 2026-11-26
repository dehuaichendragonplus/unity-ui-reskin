#!/usr/bin/env python3
# 效果图 ↔ 运行时 差异分析：分组汇总 → 自动修正清单 → 跟随锚点的辅助线。
#
# 输入：
#   效果图 PNG（与运行时截图同分辨率最好）；或 --spec（psd_to_spec / figma_to_spec 产出，数值精确，不用 CV）
#   运行时目录：ReskinMatrix.Snap 产出的 runtime.json + runtime.png
# 输出（--out 目录）：
#   diff.md        人看的报告（按组：组整体偏移 / 组内残差 / 文字字号与颜色 / 低置信与未配对）
#   diff.json      全部明细
#   fixes.json     ReskinApply.Apply 可直接读（先 dryRun）
#   guides.json    ReskinOverlay.LoadGuides 可直接读（目标框跟随锚点）
#   annotated.png  运行时截图上画 品红=目标框、绿=当前框、标偏差；cmp.png 三栏对比
#
# 用法：
#   python -I diff.py 效果图.png <runtime目录> --out <输出目录> [--groups Root/TopGroup,Root/MiddleGroup] [--group-depth 2]
#   python -I diff.py --spec spec.json <runtime目录> --out <输出目录>
import argparse, json, os, re, sys, statistics
import numpy as np, cv2
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from reskin_common import imread, imwrite, register, refine_edges, ocr, norm_text, text_colors, color_dist

ap = argparse.ArgumentParser()
ap.add_argument("mockup", nargs="?")
ap.add_argument("rtdir")
ap.add_argument("--spec")
ap.add_argument("--out", required=True)
ap.add_argument("--groups", default="")
ap.add_argument("--group-depth", type=int, default=2)
ap.add_argument("--thr", type=float, default=2.0, help="像素阈值，小于此值视为已对齐")
ap.add_argument("--min-score", type=float, default=0.7, help="配准置信度下限，低于此值不出修正")
ap.add_argument("--langs", default="en-US,zh-Hans")
ap.add_argument("--no-text", action="store_true")
ap.add_argument("--only", default="", help="只输出路径匹配该正则的修正（如 'ImgBg$|TopGroup/TextGroup/Text$'）")
ap.add_argument("--flat", action="store_true", help="不按组合并：每个元素按自身完整偏差修正（只修个别元素时用）")
ap.add_argument("--include-list", action="store_true", help="列表(ScrollView/Content)内的元素也出修正；默认只报告（滚动位置/数据不同）")
a = ap.parse_args()
if not a.mockup and not a.spec:
    sys.exit("需要效果图或 --spec")
os.makedirs(a.out, exist_ok=True)

rt = json.load(open(os.path.join(a.rtdir, "runtime.json")))
rt_png = os.path.join(a.rtdir, "runtime.png")
rt_img = imread(rt_png)
SW, SH = rt["screenW"], rt["screenH"]
if rt_img.shape[1] != SW:
    print(f"⚠ runtime.png 宽 {rt_img.shape[1]} ≠ runtime.json 屏宽 {SW}，按截图尺寸缩放坐标")
warn = []

# ---------- 效果图 / spec ----------
spec = None
if a.spec:
    spec = json.load(open(a.spec))
    k = SW / spec["width"]
    mk_img = None
    if spec.get("preview") and os.path.exists(spec["preview"]):
        mk_img = cv2.resize(imread(spec["preview"]), (SW, int(round(spec["height"] * k))))
else:
    mk_img = imread(a.mockup)
    if mk_img.shape[1] != SW:
        k = SW / mk_img.shape[1]
        mk_img = cv2.resize(mk_img, (SW, int(round(mk_img.shape[0] * k))))
        warn.append(f"效果图宽 {int(SW / k)} 与屏宽 {SW} 不同，已等比缩放到屏宽")
    if abs(mk_img.shape[0] - SH) > 2:
        warn.append(f"效果图高 {mk_img.shape[0]} 与屏高 {SH} 不同（长宽比不同），纵向偏差可能含适配差，建议 Game 视图设成效果图尺寸")
rt_gray = cv2.cvtColor(rt_img, cv2.COLOR_BGR2GRAY)
mk_gray = cv2.cvtColor(mk_img, cv2.COLOR_BGR2GRAY) if mk_img is not None else None

nodes = {n["path"]: n for n in rt["nodes"]}
depth = lambda p: 0 if p == "" else p.count("/") + 1
name_of = lambda p: p.split("/")[-1] if p else rt["view"]


def parent_chain(p):
    parts = p.split("/")
    return ["/".join(parts[:i]) for i in range(len(parts) - 1, 0, -1)]


# ---------- 候选节点 ----------
def is_candidate(n):
    if not n["visible"]:
        return False
    w, h = n["rect"][2], n["rect"][3]
    if w < 6 or h < 6:
        return False
    return (n.get("sprite") and n.get("alpha", 1) > 0.05) or (n.get("text") or "").strip()


cands = [n for n in rt["nodes"] if is_candidate(n)]
groups = [g for g in a.groups.split(",") if g] or sorted({"/".join(c["path"].split("/")[:d]) for c in cands for d in range(1, a.group_depth + 1)
                                                            if depth(c["path"]) > d})
groups = [] if a.flat else [g for g in groups if g in nodes]


def group_of(p):
    best = None
    for g in groups:
        if (p == g or p.startswith(g + "/")) and (best is None or len(g) > len(best)):
            best = g
    return best or "(无组)"


def outline_of(e):
    """效果图文字的「描边」：Stroke，或 阻塞≥50% 的投影（实心外扩 + 偏移，本项目美术的常见做法）。"""
    fx = e.get("effects", {})
    if fx.get("Stroke"):
        s0 = fx["Stroke"][0]
        return {"color": s0.get("color"), "px": s0.get("size"), "offset": [0, 0], "kind": "Stroke"}
    for d in fx.get("DropShadow", []):
        if d.get("choke", 0) >= 50:
            return {"color": d.get("color"), "px": round(d.get("size", 0) * d.get("choke", 100) / 100, 2), "offset": d.get("offset"), "kind": "DropShadow(choke)"}
    return None


# ---------- 测量：效果图中的目标矩形 ----------
res = {}
cache = {}
def text_holes(p):
    """图片节点配准时挖掉子孙文字（文案/语言/数字不同会拉低置信度）"""
    if not nodes[p].get("sprite"):
        return None
    return [(c.get("textRect") or c["rect"]) for c in cands if c["path"].startswith(p + "/") and (c.get("text") or "").strip()] or None


if mk_img is not None:
    for n in cands:
        key = (tuple(round(v) for v in n["rect"]), n["path"] if text_holes(n["path"]) else "")
        if key not in cache:
            sc, mk, s = register(rt_gray, mk_gray, n["rect"], holes=text_holes(n["path"]))
            edge = None
            if mk is not None and sc >= a.min_score and not (n.get("text") or "").strip():
                edge = refine_edges(rt_gray, mk_gray, n["rect"], mk)
            cache[key] = (sc, edge or mk, s, "edges" if edge else "uniform")
        sc, mk, s, m = cache[key]
        if mk is not None:
            res[n["path"]] = {"score": round(float(sc), 3), "mk": mk, "scale": s, "method": m}
    # 组容器也整体配准一次（组整体偏移的直接证据）
    for g in groups:
        n = nodes[g]
        if g not in res and n["visible"] and n["rect"][2] > 20 and n["rect"][3] > 20:
            sc, mk, s = register(rt_gray, mk_gray, n["rect"], window=120)
            if mk is not None:
                res[g] = {"score": round(float(sc), 3), "mk": mk, "scale": s, "method": "group"}
if spec:
    els = [e for e in spec["elements"] if e.get("visible", True)]
    by_name = {}
    for e in els:
        by_name.setdefault(e["name"].lower(), []).append(e)
    used = set()
    for n in cands:
        cand = []
        if n.get("text"):
            cand = [e for e in els if e["kind"] == "text" and norm_text(e.get("text")) == norm_text(n["text"])]
        if not cand:
            cand = by_name.get(name_of(n["path"]).lower(), []) + (by_name.get(n["sprite"].lower(), []) if n.get("sprite") else [])
        cand = [e for e in cand if id(e) not in used]
        if not cand:
            continue
        base = n.get("textRect") or n["rect"]
        cx, cy = base[0] + base[2] / 2, base[1] + base[3] / 2
        dist = lambda e: ((e["rect"][0] * k + e["rect"][2] * k / 2 - cx) ** 2 + (e["rect"][1] * k + e["rect"][3] * k / 2 - cy) ** 2) ** 0.5
        e = min(cand, key=dist)
        if dist(e) > max(160, 0.5 * max(n["rect"][2], n["rect"][3])):
            continue  # 同名图层离得太远：多半是另一个实例/另一个状态，不配
        is_text = e["kind"] == "text" and (n.get("text") or "").strip()
        if not is_text:
            prev = res.get(n["path"])
            if prev and prev["score"] >= a.min_score:
                prev["spec"] = e  # CV 已可靠：位置/尺寸用 CV（PSD 图层框是裁边后的像素框，与节点矩形定义不同）
                continue
        used.add(id(e))
        er = [v * k for v in e["rect"]]
        entry = {"score": 1.0, "mk": er, "spec": e, "method": "spec"}
        if is_text and mk_gray is not None:
            # 文字：把效果图里该文字的像素块，反向到运行时截图里找 → 运行时实际笔画框（与 PSD 文字框同定义）
            m = 3
            sc, rtr, sc_s = register(mk_gray, rt_gray, [er[0] - m, er[1] - m, er[2] + 2 * m, er[3] + 2 * m], window=60, scales=np.arange(0.76, 1.30, 0.02))
            if rtr is not None and sc >= 0.55:
                entry.update(rtRect=[rtr[0] + m * sc_s, rtr[1] + m * sc_s, rtr[2] - 2 * m * sc_s, rtr[3] - 2 * m * sc_s], score=round(float(sc), 3), inkScale=round(float(sc_s), 3), method="spec+ink")
            else:
                entry.update(score=0.5, method="spec(未在运行时找到同样的文字块)")
        else:
            entry["noSize"] = True
        res[n["path"]] = entry

for p, r in res.items():
    n = nodes[p]
    x, y, w, h = r.get("rtRect") or n["rect"]
    mx, my, mw, mh = r["mk"]
    r.update(dx=(x + w / 2) - (mx + mw / 2), dy=(y + h / 2) - (my + mh / 2), dw=0.0 if r.get("noSize") else w - mw, dh=0.0 if r.get("noSize") else h - mh, group=group_of(p))

# ---------- 文字：字号与颜色 ----------
text_rows = []
if not a.no_text:
    if spec:
        for p, r in res.items():
            n, e = nodes[p], r.get("spec")
            if not e or e["kind"] != "text" or not n.get("text"):
                continue
            px_per_font = n["ppu"] * n["scale"][1]
            cur_px = n["fontSize"] * px_per_font
            tgt_px = e.get("fontPx", 0) * k
            ol = outline_of(e)
            row = {"path": p, "text": n["text"][:30], "cur": n["fontSize"], "target": round(tgt_px / px_per_font, 1) if tgt_px else None,
                   "fontPx_mk": e.get("fontPx"), "fontPx_rt": round(cur_px / k, 1),
                   "fill_mk": e.get("fill"), "fill_rt": [int(v * 255) for v in n["color"][:3]] if n.get("color") else None,
                   "stroke_mk": ol and ol["color"], "strokeW_mk": ol and ol["px"], "strokeOff_mk": ol and ol["offset"],
                   "stroke_rt": [int(v * 255) for v in n["underlayColor"][:3]] if n.get("underlayColor") and n["underlayColor"][3] > 0.05 else None,
                   "underlay_rt": [round(n.get("underlayDilate", 0), 2), round(n.get("underlayOffsetY", 0), 2)], "material_rt": n.get("material"),
                   "gradient_mk": [g["color"] for g in e.get("effects", {}).get("GradientOverlay", [{}])[0].get("gradient") or []] or None,
                   "font_mk": e.get("font")}
            if norm_text(e.get("text")) != norm_text(n["text"]):
                row["note"] = f"文案不同：效果图「{e.get('text')}」"
            rr = r.get("rtRect")
            if rr and ol and ol.get("px"):
                m_ = 6
                cr = text_colors(rt_img, [rr[0] - m_, rr[1] - m_, rr[2] + 2 * m_, rr[3] + 2 * m_])
                if mk_img is not None:
                    er_ = r["mk"]; cm_ = text_colors(mk_img, [er_[0] - m_, er_[1] - m_, er_[2] + 2 * m_, er_[3] + 2 * m_])
                    row["strokeW_mk_measured"] = cm_ and cm_["strokeW"]
                row["strokeW_rt"] = cr and cr["strokeW"]
                if cr and row.get("strokeW_mk_measured"):
                    row["strokeRatio"] = round(cr["strokeW"] / row["strokeW_mk_measured"], 2)
            if rr:
                row["inkSize"] = [round(rr[2] / max(1, r["mk"][2]), 3), round(rr[3] / max(1, r["mk"][3]), 3)]
                if row["inkSize"][0] - row["inkSize"][1] > 0.04:
                    row["note"] = (row.get("note", "") + f" 字形偏宽（宽比 {row['inkSize'][0]} / 高比 {row['inkSize'][1]}）→ 查字距").strip()
            text_rows.append(row)
    else:
        mk_path = os.path.join(a.out, "_mockup_scaled.png")
        imwrite(mk_path, mk_img)
        mk_lines = ocr(mk_path, a.langs)
        rt_lines = ocr(rt_png, a.langs)
        for n in cands:
            t = (n.get("text") or "").strip()
            if not t:
                continue
            x, y, w, h = n["rect"]
            mine = [l for l in rt_lines if x - 4 <= l["rect"][0] + l["rect"][2] / 2 <= x + w + 4 and y - 4 <= l["rect"][1] + l["rect"][3] / 2 <= y + h + 4]
            mine = [l for l in mine if norm_text(l["text"]) and norm_text(l["text"]) in norm_text(t)]
            r = res.get(n["path"], {})
            ddx, ddy = r.get("dx", 0), r.get("dy", 0)
            ratios, cm, cr, found = [], None, None, 0
            for l in mine:
                lt = norm_text(l["text"])
                cxr, cyr = l["rect"][0] + l["rect"][2] / 2 - ddx, l["rect"][1] + l["rect"][3] / 2 - ddy
                same = [m for m in mk_lines if norm_text(m["text"]) == lt]
                if not same:
                    continue
                m = min(same, key=lambda m: (m["rect"][0] + m["rect"][2] / 2 - cxr) ** 2 + (m["rect"][1] + m["rect"][3] / 2 - cyr) ** 2)
                if abs(m["rect"][1] + m["rect"][3] / 2 - cyr) > 60:
                    continue
                found += 1
                ratios.append(m["rect"][3] / max(1, l["rect"][3]))
                if cm is None:
                    cm, cr = text_colors(mk_img, m["rect"]), text_colors(rt_img, l["rect"])
            row = {"path": n["path"], "text": t[:30], "cur": n.get("fontSize"), "autoSize": n.get("autoSize"), "lines_rt": len(mine), "lines_matched": found}
            if not mine:
                row["note"] = "运行时 OCR 未识别（字太小/被遮挡/语言不在 --langs）"
            elif not found:
                row["note"] = "效果图里找不到同样的文字（语言不同或文案 key 不同 → 用英文跑运行时 / 列待策划确认）"
            if found and (n["path"] not in res or res[n["path"]]["score"] < a.min_score):
                # 文字内容相同 → 用 OCR 行框中心差作为文字节点的位置偏差（比截图块配准可靠）
                pairs_ = []
                for l in mine:
                    lt = norm_text(l["text"]); same = [m for m in mk_lines if norm_text(m["text"]) == lt]
                    if same:
                        m = min(same, key=lambda m: abs(m["rect"][1] - l["rect"][1]) + abs(m["rect"][0] - l["rect"][0]))
                        pairs_.append((l["rect"][0] + l["rect"][2] / 2 - m["rect"][0] - m["rect"][2] / 2, l["rect"][1] + l["rect"][3] / 2 - m["rect"][1] - m["rect"][3] / 2))
                if pairs_:
                    odx = statistics.median([p_[0] for p_ in pairs_]); ody = statistics.median([p_[1] for p_ in pairs_])
                    res[n["path"]] = {"score": 0.9, "mk": [x - odx, y - ody, w, h], "scale": 1, "method": "ocr",
                                      "dx": odx, "dy": ody, "dw": 0.0, "dh": 0.0, "group": group_of(n["path"])}
            if ratios:
                ratio = statistics.median(ratios)
                row["ratio"] = round(ratio, 3)
                row["target"] = round(n["fontSize"] * ratio, 1)
            if cm and cr:
                row.update(fill_mk=cm["fill"], fill_rt=cr["fill"], stroke_mk=cm["stroke"], stroke_rt=cr["stroke"],
                           strokeW_mk=cm["strokeW"], strokeW_rt=cr["strokeW"], shadow_mk=cm["shadowOff"], shadow_rt=cr["shadowOff"])
            text_rows.append(row)

# ---------- 修正：先组、后元素，自上而下累计 ----------
C = {}          # path -> 已计划的屏幕位移（含祖先），像素
fixes, group_rows = [], []


def inherited(p):
    for q in parent_chain(p):
        if q in C:
            return C[q]
    return (0.0, 0.0)


def add_move(p, cx, cy, reason):
    n = nodes[p]
    ppu = n["ppu"] or 1
    fixes.append({"path": p, "op": "move", "dx": round(float(cx) / ppu, 2), "dy": round(-float(cy) / ppu, 2), "value": 0, "reason": reason})
    ix, iy = inherited(p)
    C[p] = (ix + cx, iy + cy)


LIST_RE = re.compile(r"(^|/)(Viewport/)?Content/")
in_list = lambda p: bool(LIST_RE.search(p)) and "Scroll" in p
ok = lambda r: r["score"] >= a.min_score
for g in sorted(groups, key=depth):
    mem = [(p, r) for p, r in res.items() if (p.startswith(g + "/")) and p not in groups and ok(r) and (a.include_list or not in_list(p))]
    if len(mem) < 2:
        group_rows.append({"group": g, "n": len(mem), "note": "可用元素不足"})
        continue
    ix, iy = inherited(g)
    dxs = [r["dx"] + ix for _, r in mem]; dys = [r["dy"] + iy for _, r in mem]
    mdx, mdy = statistics.median(dxs), statistics.median(dys)
    madx = statistics.median([abs(v - mdx) for v in dxs]); mady = statistics.median([abs(v - mdy) for v in dys])
    mdx, mdy, madx, mady = float(mdx), float(mdy), float(madx), float(mady)
    row = {"group": g, "n": len(mem), "median": [round(mdx, 1), round(mdy, 1)], "mad": [round(madx, 1), round(mady, 1)]}
    consistent_x = abs(mdx) > a.thr and madx <= max(2.0, 0.5 * abs(mdx))
    consistent_y = abs(mdy) > a.thr and mady <= max(2.0, 0.5 * abs(mdy))
    if consistent_x or consistent_y:
        cx = -mdx if consistent_x else 0.0; cy = -mdy if consistent_y else 0.0
        if nodes[g].get("hasLayoutParent"):
            row["note"] = "组整体偏移，但组节点在 LayoutGroup 下 → 改父布局"
        else:
            add_move(g, cx, cy, f"组整体偏移 median=({mdx:.1f},{mdy:.1f}) 组内离散 MAD=({madx:.1f},{mady:.1f})")
            row["fix"] = [round(float(cx), 1), round(float(cy), 1)]
    else:
        row["note"] = "组内偏差不一致 → 逐个修正" if max(abs(mdx), abs(mdy)) > a.thr else "组整体已对齐"
    group_rows.append(row)

el_rows = []
for p in sorted(res, key=depth):
    r = res[p]
    if p in groups:
        continue
    n = nodes[p]
    ix, iy = inherited(p)
    rx, ry = r["dx"] + ix, r["dy"] + iy
    row = {"path": p, "group": r["group"], "score": r["score"], "method": r["method"], "d": [round(float(r["dx"]), 1), round(float(r["dy"]), 1)],
           "residual": [round(float(rx), 1), round(float(ry), 1)], "dsize": [round(float(r["dw"]), 1), round(float(r["dh"]), 1)]}
    if not ok(r):
        row["note"] = "低置信（动态内容/文案不同/被遮挡），不出修正"
        el_rows.append(row); continue
    if in_list(p) and not a.include_list:
        if max(abs(rx), abs(ry), abs(r["dw"]), abs(r["dh"])) > a.thr:
            row["note"] = "列表项（滚动位置/数据与效果图不同），只报告；需要时 --include-list"
        el_rows.append(row); continue
    is_text = bool((n.get("text") or "").strip()) and not n.get("sprite")
    cx = -rx if abs(rx) > a.thr else 0.0
    cy = -ry if abs(ry) > a.thr else 0.0
    # 尺寸（仅图片；文字框尺寸不代表可见大小，用字号修）
    rs_x = rs_y = 0.0
    # 尺寸阈值：CV 等比配准的尺度步长 0.02 → 大图有 ±1% 量化误差，不足以支撑 2px 级修正
    size_thr_w = max(a.thr, 0.025 * n["rect"][2]) if r.get("method") == "uniform" else a.thr + 1  # 四边精修 ±1px
    size_thr_h = max(a.thr, 0.025 * n["rect"][3]) if r.get("method") == "uniform" else a.thr + 1
    if not is_text and (abs(r["dw"]) > size_thr_w or abs(r["dh"]) > size_thr_h):
        sx, sy = n["scale"]
        ppu = n["ppu"] or 1
        stretch_x = n["anchorMin"][0] != n["anchorMax"][0]; stretch_y = n["anchorMin"][1] != n["anchorMax"][1]
        rs_x = -r["dw"] / (ppu * sx) if abs(r["dw"]) > size_thr_w else 0.0
        rs_y = -r["dh"] / (ppu * sy) if abs(r["dh"]) > size_thr_h else 0.0
        if rs_x or rs_y:
            fixes.append({"path": p, "op": "resize", "dx": round(rs_x, 2), "dy": round(rs_y, 2), "value": 0,
                          "reason": f"尺寸差 {r['dw']:.0f}x{r['dh']:.0f}px" + (" (拉伸锚点)" if stretch_x or stretch_y else "")})
            row["fix_size"] = [round(float(rs_x), 1), round(float(rs_y), 1)]
            # 改尺寸会让中心沿 (0.5 - pivot) 方向移动，扣掉
            px_, py_ = n["pivot"]
            cx -= (0.5 - px_) * rs_x * sx * ppu
            cy += (0.5 - py_) * rs_y * sy * ppu
    if abs(cx) > 0.5 or abs(cy) > 0.5:
        if n.get("hasLayoutParent"):
            row["note"] = "在 LayoutGroup 下，位置由布局决定 → 改间距/父节点"
        else:
            add_move(p, cx, cy, f"残差 ({rx:.1f},{ry:.1f}) score={r['score']}")
            row["fix_move"] = [round(float(cx), 1), round(float(cy), 1)]
    el_rows.append(row)

if a.only:
    _only = re.compile(a.only)
    fixes = [f for f in fixes if _only.search(f["path"])]

styles = []
if spec:
    for p, r in res.items():
        e = r.get("spec")
        if not e or e["kind"] != "text" or not nodes[p].get("text"):
            continue
        st = {"path": p, "fontPx": round(e.get("fontPx", 0) * k, 2), "fill": e.get("fill")}
        go = e.get("effects", {}).get("GradientOverlay")
        if go and go[0].get("gradient") and len(go[0]["gradient"]) >= 2 and abs(go[0].get("angle", 90) - 90) < 30:
            g0, g1 = go[0]["gradient"][0]["color"], go[0]["gradient"][-1]["color"]
            top, bottom = (g0, g1) if go[0].get("reversed") else (g1, g0)  # PS 角度 90：第一个色标在下
            st["gradientTop"], st["gradientBottom"] = top, bottom
        ol = outline_of(e)
        if ol:
            st["outline"] = {"color": ol["color"], "px": ol["px"], "offset": ol["offset"]}
        if e.get("tracking") is not None:
            st["charSpacing"] = round(float(e["tracking"]) / 10, 2)  # PS 1/1000em → TMP 1/100em
        styles.append(st)
    json.dump({"prefab": rt.get("prefab"), "prefix": "RS", "styles": styles}, open(os.path.join(a.out, "styles.json"), "w"), ensure_ascii=False, indent=1)

# 跟随：低置信/未配准的兄弟节点（倒计时文字、动态图标等）若大部分落在某个被移动元素的矩形内，跟着一起移动，避免底图和上面的内容错开
moved = {f["path"]: f for f in fixes if f["op"] == "move"}
def _inside(a_, b_):
    ix = max(0, min(a_[0] + a_[2], b_[0] + b_[2]) - max(a_[0], b_[0])); iy = max(0, min(a_[1] + a_[3], b_[1] + b_[3]) - max(a_[1], b_[1]))
    return ix * iy / max(1, a_[2] * a_[3])
for c in cands:
    p = c["path"]
    if p in moved or (p in res and ok(res[p])) or (in_list(p) and not a.include_list):
        continue
    par = p.rsplit("/", 1)[0] if "/" in p else ""
    for mp, mf in moved.items():
        mpar = mp.rsplit("/", 1)[0] if "/" in mp else ""
        if mpar == par and _inside(c["rect"], nodes[mp]["rect"]) > 0.6:
            fixes.append({"path": p, "op": "move", "dx": mf["dx"], "dy": mf["dy"], "value": 0, "reason": f"跟随 {mp.split('/')[-1]}（低置信，叠在其上）"})
            break

for t in ([] if spec else text_rows):
    tgt = t.get("target")
    # OCR 行高估字号有 ±5~10% 噪声（换行、行框留白）：阈值 6%，且只走一半步长（阻尼），多轮收敛，避免来回振荡
    if tgt and t.get("cur") and abs(tgt - t["cur"]) / max(1, t["cur"]) > 0.06 and t.get("lines_matched", 0) >= 1:
        damped = round(t["cur"] + (tgt - t["cur"]) * 0.5, 1)
        fixes.append({"path": t["path"], "op": "fontSize", "dx": 0, "dy": 0, "value": damped, "reason": f"字高比 {t.get('ratio', '')}（OCR 估算，阻尼 50%）"})

# ---------- 辅助线 ----------
guides = []
for p, r in res.items():
    if r["score"] < a.min_score:
        continue
    n = nodes[p]
    ppu = n["ppu"] or 1
    pos = [n["pos"][0] - r["dx"] / ppu, n["pos"][1] + r["dy"] / ppu]
    size = list(n["size"])
    if not ((n.get("text") or "").strip() and not n.get("sprite")):
        size = [n["size"][0] - r["dw"] / (ppu * n["scale"][0]), n["size"][1] - r["dh"] / (ppu * n["scale"][1])]
        px_, py_ = n["pivot"]
        pos[0] -= (0.5 - px_) * (size[0] - n["size"][0]) * n["scale"][0]
        pos[1] -= (0.5 - py_) * (size[1] - n["size"][1]) * n["scale"][1]
    guides.append({"path": p, "group": r["group"], "label": name_of(p), "target": [round(v, 1) for v in r["mk"]],
                   "pos": [round(v, 2) for v in pos], "size": [round(v, 2) for v in size]})
json.dump({"view": rt["view"], "refW": SW, "refH": SH, "guides": guides}, open(os.path.join(a.out, "guides.json"), "w"), ensure_ascii=False, indent=1)
json.dump({"prefab": rt.get("prefab"), "fixes": fixes}, open(os.path.join(a.out, "fixes.json"), "w"), ensure_ascii=False, indent=1)
json.dump({"warn": warn, "groups": group_rows, "elements": el_rows, "texts": text_rows}, open(os.path.join(a.out, "diff.json"), "w"), ensure_ascii=False, indent=1)

# ---------- 图 ----------
ann = rt_img.copy()
for p, r in res.items():
    if r["score"] < a.min_score:
        continue
    big = max(abs(r["dx"]), abs(r["dy"]), abs(r["dw"]), abs(r["dh"])) > a.thr
    if not big:
        continue
    mx, my, mw, mh = [int(round(v)) for v in r["mk"]]
    x, y, w, h = [int(round(v)) for v in nodes[p]["rect"]]
    cv2.rectangle(ann, (mx, my), (mx + mw, my + mh), (220, 30, 255), 1)
    cv2.rectangle(ann, (x, y), (x + w, y + h), (60, 255, 30), 1)
    cv2.putText(ann, f"{name_of(p)[:18]} {r['dx']:+.0f},{r['dy']:+.0f}", (x, max(10, y - 3)), cv2.FONT_HERSHEY_SIMPLEX, 0.35, (60, 255, 30), 1, cv2.LINE_AA)
imwrite(os.path.join(a.out, "annotated.png"), ann)
if mk_img is not None:
    h = max(mk_img.shape[0], rt_img.shape[0])
    pad = lambda im: cv2.copyMakeBorder(im, 0, h - im.shape[0], 0, 0, cv2.BORDER_CONSTANT)
    blend = cv2.addWeighted(pad(rt_img), 0.5, pad(mk_img), 0.5, 0)
    imwrite(os.path.join(a.out, "cmp.png"), np.hstack([pad(rt_img), pad(mk_img), blend]))

# ---------- 报告 ----------
L = [f"# 差异报告 {rt['view']}  {SW}x{SH}{' (Pad)' if rt.get('pad') else ''}", ""]
L += [f"> 来源：{'spec ' + a.spec if spec else '效果图 ' + a.mockup}；运行时 {a.rtdir}；阈值 {a.thr}px，置信度下限 {a.min_score}", ""]
L += [f"- ⚠ {w}" for w in warn] + ([""] if warn else [])
n_ok = sum(1 for r in el_rows if "fix_move" not in r and "fix_size" not in r and "note" not in r)
L += [f"**结论**：配准元素 {len(el_rows)} 个，已对齐 {n_ok} 个，需修正 {len([f for f in fixes])} 项（fixes.json），低置信 {sum(1 for r in el_rows if r.get('note', '').startswith('低置信'))} 个。", ""]
L += ["## 分组", "", "| 组 | 元素数 | 中位偏差(px) | 离散 MAD | 处理 |", "|---|---|---|---|---|"]
for g in group_rows:
    L.append(f"| `{g['group']}` | {g['n']} | {g.get('median', '')} | {g.get('mad', '')} | {('整组移动 ' + str(g['fix'])) if g.get('fix') else g.get('note', '')} |")
L += ["", "## 元素（只列有偏差或低置信的）", "", "| 组 | 节点 | 置信 | 偏差 dx,dy | 扣除组后残差 | 尺寸差 | 修正 / 说明 |", "|---|---|---|---|---|---|---|"]
for r in el_rows:
    if "fix_move" in r or "fix_size" in r or "note" in r:
        act = "; ".join(x for x in [f"移 {r['fix_move']}" if r.get("fix_move") else "", f"尺寸 {r['fix_size']}" if r.get("fix_size") else "", r.get("note", "")] if x)
        L.append(f"| {r['group'].split('/')[-1]} | `{r['path']}` | {r['score']} | {r['d']} | {r['residual']} | {r['dsize']} | {act} |")
if text_rows:
    L += ["", "## 文字", "", "| 节点 | 文字 | 字号 当前→目标 | 填充 效果图/运行时 | 描边 效果图/运行时 | 描边宽 | 说明 |", "|---|---|---|---|---|---|---|"]
    if spec:
        L[-2:] = ["| 节点 | 文字 | 字号 当前→目标 | 填充 效果图/运行时 | 渐变(效果图) | 描边 效果图(色,px,偏移) | 描边 运行时(色,dilate,offY) | 说明 |", "|---|---|---|---|---|---|---|---|"]
    for t in text_rows:
        if spec:
            sr = f" 实测宽比 {t['strokeRatio']}" if t.get("strokeRatio") else ""
            L.append(f"| `{t['path'].split('/')[-1]}` | {t['text']} | {t.get('cur')} → {t.get('target')} | {t.get('fill_mk')} / {t.get('fill_rt')} | {t.get('gradient_mk') or ''} | "
                     f"{t.get('stroke_mk')}, {t.get('strokeW_mk')}, {t.get('strokeOff_mk')} | {t.get('stroke_rt')}, {t.get('underlay_rt')}{sr} | {t.get('note', '')} |")
            continue
        fs = f"{t.get('cur')} → {t.get('target')}" if t.get("target") else f"{t.get('cur')}"
        fill = f"{t.get('fill_mk')} / {t.get('fill_rt')}" if t.get("fill_mk") else ""
        st = f"{t.get('stroke_mk')} / {t.get('stroke_rt')}" if t.get("stroke_mk") else ""
        sw = f"{t.get('strokeW_mk')} / {t.get('strokeW_rt')}" if t.get("strokeW_mk") is not None else ""
        de = color_dist(t.get("stroke_mk"), t.get("stroke_rt"))
        note = t.get("note", "") + (f" 描边色差 {de:.0f}" if de and de > 40 else "")
        L.append(f"| `{t['path'].split('/')[-1]}` | {t['text']} | {fs} | {fill} | {st} | {sw} | {note} |")
L += ["", "## 下一步", ""] + (["0. 文字样式：`ReskinTextStyle.Apply(\"<out>/styles.json\", dryRun: true)`（字号/填充/渐变/描边材质，10 种语言）。"] if spec else []) + [ "1. `ReskinApply.Apply(\"<out>/fixes.json\", dryRun: true)` 看报告 → 确认后 `dryRun: false`（需退出 Play）。",
      "2. 重新 Snap + diff，直到「需修正 0 项」。", "3. Play 中 `ReskinOverlay.LoadGuides(\"<out>/guides.json\")` 换其它分辨率检查（目标框跟随锚点）。"]
open(os.path.join(a.out, "diff.md"), "w").write("\n".join(L))
print("\n".join(L[:12]))
print(f"→ {a.out}/diff.md  fixes={len(fixes)}  guides={len(guides)}" + (f"  styles={len(styles)}" if spec else ""))
