#!/usr/bin/env python3
"""spec.json（psd_to_spec / figma_to_spec 的输出）→ layout.json：新建界面的骨架节点树，交给 Unity 端 UIReskin.ReskinBuild.Build 生成 prefab。

做什么：
  - 取手机画面：--frame auto|full|x0,y0,x1,y1|<项目事实 ui.effectFrames 里的名字>（同 crop_effect.py；默认取项目事实 ui.defaultFrame，没有就 auto）
  - 换算单位：--ref-width = 项目 UI 参考宽（默认取项目事实 ui.refWidth；没有就画面宽，1 px = 1 单位）
  - 字体：layout.font 取项目事实 text.defaultFont（没有就交给 Unity 端按项目里最常用的 TMP 字体选）
  - 层级：PSD 组 → 父节点，图层 → 子节点；保持 PSD 叠放顺序（下层先建，后建的画在上面）；名字里的 / 换成 _，同级重名加 _2 _3（保证路径唯一，ReskinApply 能按路径找到）
  - 锚点（只给第一层节点定，里面的节点相对父节点居中，整组跟着父节点走）：
      几乎铺满画面 → 四边拉伸；宽 ≥ 90% → 横向拉伸；中心在上 25% → 顶部；下 25% → 底部；否则居中；
      水平方向贴左 / 右边 15% 内 → 左 / 右锚
  - 图片：sprite = 图层名（Unity 端优先找工程里同名 Sprite）；png = 导出的图层图（--export-layers 导出的，缺切图时导入用）
  - 文字：内容、字号（已换算）、填充色、字距（tracking/10）、对齐、渐变；描边 / 投影写进同目录的 styles.json（ReskinTextStyle.Apply 直接用）
  - 功能推断（role，Unity 端按 role 建 UGUI 控件；同时写 bindings.json 供代理写业务代码）：
      close     名字含 close / 关闭 / 叉 / ×，或单独的 x → Button（关闭）
      button    名字含 btn / button / 按钮 → Button（组 = 整组可点）
      progress  名字含 progress / 进度 / slider / bar / 血条 / 经验 → 组：Slider（不可拖）；单图：Image Filled
      toggle    名字含 toggle / 开关 / switch / check / 勾选 → Toggle
      list      名字含 list / scroll / 列表 / 滚动，或组里 ≥3 个结构和尺寸相同、沿一条轴排开的子组 → ScrollRect + Viewport + Content + LayoutGroup（第一个子项是模板）
      input     名字含 input / 输入 → 只标注（建 TMP_InputField 结构差异大，代理按项目已有输入框复制）
      文字：纯数字 / 时间 / 价格 → dynamicText（代码写入，不挂静态多语言）；其余 → staticText（需要多语言 key）
  - 隐藏状态组（psd_to_spec --all-states）→ 节点默认不激活
用法：
  python -I spec_to_layout.py <spec.json> --prefab Assets/<模块>/Prefabs/UI/UIXxx.prefab [--frame auto] [--ref-width 768] [--out <目录>]
  输出 layout.json + styles.json + layout.md（节点树、锚点、缺图清单），默认写在 spec.json 同目录
"""
import argparse
import collections
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import uir_project as up  # noqa: E402

BTN_RE = re.compile(r"(btn|button|按钮)", re.I)
CLOSE_RE = re.compile(r"(close|关闭|叉|×|^x$|^x[_ ]|[_ ]x$)", re.I)
PROGRESS_RE = re.compile(r"(progress|进度|slider|bar\b|_bar|血条|经验条?)", re.I)
NOT_PROGRESS_RE = re.compile(r"(num|number|数字|icon|图标|txt|text|title|标题|badge|徽章)", re.I)
TOGGLE_RE = re.compile(r"(toggle|开关|switch|check|勾选)", re.I)
LIST_RE = re.compile(r"(list|scroll|列表|滚动)", re.I)
INPUT_RE = re.compile(r"(input|输入)", re.I)
DYN_RE = re.compile(r"^[\s\d:：.,/xX×%+\-$¥€£]+$|^\d+(\.\d+)?\s*[kKmMwW万亿]$|^Lv\.?\s*\d+$|^\d+\s*/\s*\d+$", re.I)


def role_of(name, kind, text=None):
    n = name or ""
    if CLOSE_RE.search(n):
        return "close"
    if INPUT_RE.search(n):
        return "input"
    if TOGGLE_RE.search(n):
        return "toggle"
    if PROGRESS_RE.search(n) and not NOT_PROGRESS_RE.search(n):
        return "progress"
    if kind == "group" and LIST_RE.search(n):
        return "list"
    if BTN_RE.search(n):
        return "button"
    if kind == "text":
        return "dynamicText" if DYN_RE.match((text or "").strip()) else "staticText"
    return None


SUGGEST = {
    "close": "关闭本界面（走项目 UI 管理器的关闭接口）",
    "button": "点击回调：按按钮文字 / 图标推断（领取、购买、确认、前往……），推断不出写 TODO 并列待确认",
    "progress": "代码设置 value（0~1），数据来源待确认",
    "toggle": "onValueChanged 回调，状态要持久化时写存档",
    "list": "按数据生成子项（第一个子项是模板），子项内控件同样绑定",
    "input": "复制项目已有输入框结构，onEndEdit 回调",
    "dynamicText": "代码写入（数字 / 时间 / 价格），不挂静态多语言",
    "staticText": "挂项目的静态多语言组件并找 / 补多语言 key（不新增 key 时先复用同义词条）",
}


BUTTON_FUNCS = [
    (re.compile(r"(free|watch|video|ad\b|ads\b|广告|免费)", re.I), "看广告领取 / 免费领取（接项目广告接口，成功回调里发奖）"),
    (re.compile(r"(activate|unlock|激活|解锁|开通)", re.I), "激活 / 解锁（多为付费：价格读计费配置，走项目支付接口）"),
    (re.compile(r"(claim|cliam|collect|get\b|receive|领取|收下)", re.I), "领取奖励（发奖 + 奖励飞行 + 刷新界面状态）"),
    (re.compile(r"(buy|purchase|购买|\$|¥|€|£|^\s*[\d.,]+\s*$)", re.I), "购买（价格读商店 / 计费配置，走项目支付或扣币接口）"),
    (re.compile(r"(restart|retry|again|重新|重开|再来)", re.I), "重新开始（走关卡重开流程）"),
    (re.compile(r"(continue|next|继续|下一)", re.I), "继续 / 下一步"),
    (re.compile(r"(play|start|go\b|开始|前往|去完成)", re.I), "开始 / 前往（跳转对应界面或进关）"),
    (re.compile(r"(ok|confirm|yes|确定|确认|好的)", re.I), "确认（执行后关闭）"),
    (re.compile(r"(cancel|no\b|取消|放弃|quit|exit|退出)", re.I), "取消 / 放弃（关闭或回退）"),
    (re.compile(r"(help|\?|帮助|说明|规则|info)", re.I), "打开说明 / 帮助"),
    (re.compile(r"(setting|设置)", re.I), "打开设置"),
    (re.compile(r"(share|分享)", re.I), "分享"),
]


def button_func(label, name):
    for rx, desc in BUTTON_FUNCS:
        if rx.search(label or "") or rx.search(name or ""):
            return desc
    return ""


def clean(name):
    n = re.sub(r"[/\\]+", "_", (name or "").strip()) or "Node"
    return n[:60]


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("spec")
    ap.add_argument("--prefab", required=True, help="要生成的 prefab 路径（相对 Unity 工程根，Assets/ 开头）")
    ap.add_argument("--frame")
    ap.add_argument("--ref-width", type=float)
    ap.add_argument("--out")
    ap.add_argument("--include-hidden", action="store_true", help="连 visible=false 且不属于状态组的图层也建（默认跳过）")
    a = ap.parse_args()
    spec = json.load(open(a.spec, encoding="utf-8"))
    F = up.load_facts()
    a.frame = a.frame or up.fact(F, "ui.defaultFrame") or "auto"
    a.ref_width = a.ref_width or up.fact(F, "ui.refWidth")
    out = a.out or os.path.dirname(os.path.abspath(a.spec))
    os.makedirs(out, exist_ok=True)
    W, H = spec["width"], spec["height"]

    # 1) 手机画面
    f = up.parse_frame(a.frame, F)
    note = ""
    if f == "full":
        frame = (0, 0, W, H)
    elif f == "auto":
        frame, note = (0, 0, W, H), "auto：没有 preview，整张画布"
        pv = spec.get("preview")
        if pv and os.path.exists(pv):
            from PIL import Image
            x0, y0, x1, y1, cut = up.auto_frame(Image.open(pv).convert("RGB"))
            frame = (x0, y0, x1, y1)
            note = f"auto 裁出 {frame}（请目视确认）" if cut else "⚠ auto：四周没有纯色边框，按整张画布处理；若手机画面只是中间一块，传 --frame x0,y0,x1,y1"
    else:
        frame = f
        note = f"按 {frame}"
    fx0, fy0, fx1, fy1 = frame
    FW, FH = fx1 - fx0, fy1 - fy0
    k = (a.ref_width / FW) if a.ref_width else 1.0
    RW, RH = round(FW * k, 2), round(FH * k, 2)

    def conv(r):
        x, y, w, h = r
        return [round((x - fx0) * k, 2), round((y - fy0) * k, 2), round(w * k, 2), round(h * k, 2)]

    # 2) 建树（按 PSD 顺序：下层在前）
    nodes = collections.OrderedDict()   # key = 原始路径元组
    children = collections.defaultdict(list)
    dropped, hidden = [], 0

    def ensure_group(parts, state):
        key = tuple(parts)
        if key in nodes or not parts:
            return
        ensure_group(parts[:-1], state)
        nodes[key] = {"src": "/".join(parts), "rawName": parts[-1], "kind": "group", "rect": None, "active": True}
        children[tuple(parts[:-1])].append(key)

    for e in spec["elements"]:
        if not e.get("visible", True) and not e.get("state") and not a.include_hidden:
            hidden += 1
            continue
        x, y, w, h = e["rect"]
        if x + w <= fx0 or y + h <= fy0 or x >= fx1 or y >= fy1:
            dropped.append(e.get("name"))
            continue
        parts = [p for p in e.get("path", e.get("name", "")).split("/") if p != ""] or [e.get("name", "Node")]
        ensure_group(parts[:-1], e.get("state"))
        key = tuple(parts)
        while key in nodes:   # 同路径重复图层：给原始路径加序号，后面再统一去重名
            key = key[:-1] + (key[-1] + "\u0000",)
        n = {"src": e.get("path", e.get("name")), "rawName": parts[-1], "kind": e.get("kind", "image"), "rect": conv(e["rect"]),
             "active": bool(e.get("visible", True)), "opacity": e.get("opacity", 1)}
        if n["kind"] == "image":
            n["sprite"] = e.get("name", parts[-1]).strip()
            if e.get("png"):
                n["png"] = os.path.abspath(e["png"])
        else:
            n["text"] = e.get("text", "")
            n["fontSize"] = round((e.get("fontPx") or 0) * k, 1)
            n["fill"] = e.get("fill")
            n["charSpacing"] = round(float(e.get("tracking") or 0) / 10.0, 2)
            n["align"] = e.get("align", "center")
            go = (e.get("effects") or {}).get("GradientOverlay")
            if go and go[0].get("gradient"):
                cs = [s["color"] for s in go[0]["gradient"] if s.get("color")]
                if len(cs) >= 2:
                    bottom, top = (cs[0], cs[-1]) if not go[0].get("reversed") else (cs[-1], cs[0])   # 90°：第一个色标在下
                    n["gradientTop"], n["gradientBottom"] = top, bottom
            if e.get("stroke"):
                n["outline"] = {"color": e["stroke"], "px": round(float(e.get("strokeW") or 0) * k, 2), "offset": [0, 0]}
            elif e.get("shadow") and float(e["shadow"].get("choke", 0)) >= 50:
                sh = e["shadow"]
                n["outline"] = {"color": sh.get("color"), "px": round(float(sh.get("size", 0)) * k, 2), "offset": [0, round(float(sh.get("distance", 0)) * k, 2)]}
            elif e.get("shadow"):
                n["shadowNote"] = f"软投影 size={e['shadow'].get('size')} distance={e['shadow'].get('distance')}（Underlay 只能近似，需要时手工调）"
        if e.get("state"):
            n["state"] = e["state"]
        # 状态组：属于隐藏状态的节点不激活
        if e.get("state") and not e.get("visible", True):
            n["active"] = False
        nodes[key] = n
        children[tuple(parts[:-1])].append(key)

    # 3) 组矩形 = 子节点并集（自底向上）
    def rect_of(key):
        n = nodes[key]
        if n["rect"] is not None:
            return n["rect"]
        rs = [rect_of(c) for c in children.get(key, [])]
        rs = [r for r in rs if r]
        if not rs:
            n["rect"] = [0, 0, 0, 0]
            return n["rect"]
        x0 = min(r[0] for r in rs); y0 = min(r[1] for r in rs)
        x1 = max(r[0] + r[2] for r in rs); y1 = max(r[1] + r[3] for r in rs)
        n["rect"] = [round(x0, 2), round(y0, 2), round(x1 - x0, 2), round(y1 - y0, 2)]
        return n["rect"]

    for key in list(nodes):
        rect_of(key)

    # 4) 锚点：第一层按画面位置定，里面的相对父节点居中
    def anchor_top_level(r):
        x, y, w, h = r
        cx, cy = x + w / 2, y + h / 2
        if w >= 0.95 * RW and h >= 0.95 * RH:
            return "stretch"
        hx = "stretchX" if w >= 0.9 * RW else ("left" if x <= 0.15 * RW and cx < RW * 0.35 else ("right" if x + w >= 0.85 * RW and cx > RW * 0.65 else "center"))
        vy = "top" if cy <= 0.25 * RH else ("bottom" if cy >= 0.75 * RH else "middle")
        return f"{vy}-{hx}"

    # 4.5) 重复结构 → list：组里 ≥3 个子组，子节点数相同、尺寸差 ≤10%、中心沿一条轴排开
    def repeated_list(key):
        kids = [c for c in children.get(key, []) if nodes[c]["kind"] == "group"]
        if len(kids) < 3:
            return None
        sig = collections.Counter(len(children.get(c, [])) for c in kids)
        cnt, num = sig.most_common(1)[0][1], sig.most_common(1)[0][0]
        same = [c for c in kids if len(children.get(c, [])) == num]
        if cnt < 3:
            return None
        ws = [nodes[c]["rect"][2] for c in same]; hs = [nodes[c]["rect"][3] for c in same]
        if max(ws) > 1.1 * min(ws) or max(hs) > 1.1 * min(hs):
            return None
        cx = [nodes[c]["rect"][0] + nodes[c]["rect"][2] / 2 for c in same]
        cy = [nodes[c]["rect"][1] + nodes[c]["rect"][3] / 2 for c in same]
        if max(cx) - min(cx) < 0.25 * min(ws):
            return "vertical"
        if max(cy) - min(cy) < 0.25 * min(hs):
            return "horizontal"
        return "grid"

    # 4.6) 功能推断：先按名字，再按结构 / 形状（默认图层名「组 123」「圆角矩形 5」时只能靠后者）
    def center(r):
        return r[0] + r[2] / 2, r[1] + r[3] / 2

    def inside(pt, r, pad=0.0):
        return r[0] - pad <= pt[0] <= r[0] + r[2] + pad and r[1] - pad <= pt[1] <= r[1] + r[3] + pad

    def descendants(key):
        for c in children.get(key, []):
            yield c
            yield from descendants(c)

    TIME_RE = re.compile(r"^\s*\d{1,2}(:\d{2}){1,2}\s*$|^\s*\d+\s*[dhms天时分秒]", re.I)
    NOT_BTN_RE = re.compile(r"(背景|底板|任务|panel|frame|倒计时|timer|countdown|time)", re.I)

    def buttonish(r):
        w, h = r[2], r[3]
        return h > 0 and 1.3 <= w / h <= 6 and 0.05 * RW <= h <= 0.2 * RW and w <= 0.6 * RW

    def raw_name(k):
        return nodes[k]["rawName"].replace("\u0000", "")

    roles = {}   # key -> (role, basis, label, extra)
    parent_of = {c: p for p, cs in children.items() for c in cs}
    for key, n in nodes.items():
        raw = n["rawName"].replace("\u0000", "")
        r = role_of(raw, n["kind"], n.get("text"))
        if r == "progress":
            w, h = n["rect"][2], n["rect"][3]
            bar = h > 0 and w > 0 and (w >= 3 * h or h >= 3 * w)
            if not bar or (n["kind"] == "group" and sum(1 for d in descendants(key) if nodes[d]["kind"] == "image") < 2):
                r = None
        if r:
            roles[key] = (r, "name", "", {})
    # 结构：底图 + 文字的按钮组；底图上压着同级文字的按钮图
    for key, n in nodes.items():
        if key in roles:
            continue
        if n["kind"] == "group":
            imgs = [c for c in children.get(key, []) if nodes[c]["kind"] == "image"]
            texts = [d for d in descendants(key) if nodes[d]["kind"] == "text"]
            gr = n["rect"]
            ga = max(gr[2] * gr[3], 1)
            base = next((c for c in imgs if nodes[c]["rect"][2] * nodes[c]["rect"][3] >= 0.8 * ga and buttonish(nodes[c]["rect"])), None)
            if base and NOT_BTN_RE.search(raw_name(base) + raw_name(key)):
                base = None
            label = " ".join((nodes[t].get("text") or "").strip() for t in texts)
            if base and texts and TIME_RE.match(label):
                base = None
            if base and texts and all(inside(center(nodes[t]["rect"]), nodes[base]["rect"]) for t in texts) and len(texts) <= 3:
                roles[key] = ("button", "shape", label, {})
                for t in texts:
                    roles.setdefault(t, ("dynamicText" if DYN_RE.match((nodes[t].get("text") or "").strip()) else "staticText", "name", "", {"buttonLabel": True}))
        elif n["kind"] == "image" and buttonish(n["rect"]) and not NOT_BTN_RE.search(raw_name(key)):
            sib = [c for c in children.get(parent_of.get(key, ()), []) if c != key and nodes[c]["kind"] == "text" and inside(center(nodes[c]["rect"]), n["rect"])]
            lab = " ".join((nodes[c].get("text") or "").strip() for c in sib)
            if 1 <= len(sib) <= 2 and parent_of.get(key, ()) not in roles and not TIME_RE.match(lab):
                roles[key] = ("button", "shape", " ".join((nodes[c].get("text") or "").strip() for c in sib), {})
    # 结构：大面板右上角的小方块 → 关闭
    for key, n in nodes.items():
        if key in roles or n["kind"] not in ("image", "group"):
            continue
        x, y, w, h = n["rect"]
        if not (h > 0 and 0.75 <= w / h <= 1.33 and 0.035 * RW <= max(w, h) <= 0.14 * RW):
            continue
        cx, cy = center(n["rect"])
        anc = parent_of.get(key)
        while anc:
            ar = nodes[anc]["rect"]
            if ar[2] >= 0.5 * RW and ar[3] >= 0.3 * RW:
                if cx >= ar[0] + 0.78 * ar[2] and cy <= ar[1] + 0.22 * ar[3]:
                    roles[key] = ("close", "shape", "", {})
                break
            anc = parent_of.get(anc)
    # 结构：同一组里共左边、同中线的长条图（底 + 填充）→ 进度条
    for key, n in nodes.items():
        if key in roles or n["kind"] != "group" or n["rect"][3] <= 0 or n["rect"][2] < 3 * n["rect"][3]:
            continue
        bars = [c for c in children.get(key, []) if nodes[c]["kind"] == "image" and nodes[c]["rect"][3] > 0 and nodes[c]["rect"][2] >= 4 * nodes[c]["rect"][3]]
        if len(bars) >= 2:
            b0 = max(bars, key=lambda c: nodes[c]["rect"][2])
            r0 = nodes[b0]["rect"]
            fills = [c for c in bars if c != b0 and abs(nodes[c]["rect"][0] - r0[0]) <= 0.05 * r0[2] and abs(center(nodes[c]["rect"])[1] - center(r0)[1]) <= r0[3] / 2]
            if fills:
                roles[key] = ("progress", "shape", "", {"fill": "/".join(fills[0])})

    # 5) 输出树（去重名，生成唯一路径）
    out_nodes, styles, missing_png, buttons = [], [], [], 0
    bindings = []

    def emit(key, parent_path, depth, in_button=False):
        nonlocal buttons
        n = nodes[key]
        siblings_used = used_names.setdefault(parent_path, collections.Counter())
        base = clean(n["rawName"].replace("\u0000", ""))
        siblings_used[base] += 1
        name = base if siblings_used[base] == 1 else f"{base}_{siblings_used[base]}"
        path = f"{parent_path}/{name}" if parent_path else name
        node = {k2: v for k2, v in n.items() if k2 not in ("rawName",)}
        node["name"], node["path"], node["parent"] = name, path, parent_path
        node["anchor"] = anchor_top_level(n["rect"]) if depth == 0 else "middle-center"
        role, basis, label, extra = roles.get(key, (role_of(n["rawName"].replace("\u0000", ""), n["kind"], n.get("text")) if n["kind"] == "text" else None, "name", "", {}))
        if role:
            node["roleBasis"] = basis
        if label:
            node["label"] = label
        if extra.get("buttonLabel"):
            node["buttonLabel"] = True
        if n["kind"] == "group" and role is None:
            lay = repeated_list(key)
            if lay:
                role, node["listLayout"] = "list", lay
        elif role == "list":
            node["listLayout"] = repeated_list(key) or "vertical"
        if role in ("button", "close") and in_button:
            role = None   # 按钮里面的底图 / 子按钮：外层整组已可点，内层只当显示图
            node.pop("roleBasis", None)
        if role:
            node["role"] = role
        if role in ("button", "close"):
            node["button"] = True
            buttons += 1
        if role and role != "staticText" or (n["kind"] == "text"):
            sug = SUGGEST.get(role, "")
            if role == "button":
                f2 = button_func(label, n["rawName"])
                sug = (f2 + "；" if f2 else "") + sug
            bindings.append({"path": path, "role": role or n["kind"], "basis": basis if role else "", "kind": n["kind"],
                             "text": n.get("text"), "label": label, "suggest": sug})
        if n["kind"] == "image" and not n.get("png"):
            missing_png.append(path)
        out_nodes.append(node)
        if n["kind"] == "text" and (n.get("outline") or n.get("gradientTop") or n.get("fill") or n.get("charSpacing")):
            st = {"path": path, "fontPx": n.get("fontSize", 0), "fill": n.get("fill"), "charSpacing": n.get("charSpacing", 0)}
            if n.get("outline") and n["outline"].get("color") and n["outline"].get("px", 0) > 0:
                st["outline"] = n["outline"]
            if n.get("gradientTop"):
                st["gradientTop"], st["gradientBottom"] = n["gradientTop"], n["gradientBottom"]
            styles.append(st)
        for c in children.get(key, []):
            emit(c, path, depth + 1, in_button or role in ("button", "close"))

    used_names = {}
    for c in children.get((), []):
        emit(c, "", 0)

    layout = {"source": spec.get("source"), "prefab": a.prefab, "font": up.fact(F, "text.defaultFont", ""), "spriteDir": up.fact(F, "ui.spriteDir", ""),
              "refWidth": RW, "refHeight": RH, "frame": list(frame),
              "scale": round(k, 5), "nodes": out_nodes}
    json.dump(layout, open(os.path.join(out, "layout.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    json.dump({"prefab": a.prefab, "nodes": bindings}, open(os.path.join(out, "bindings.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    json.dump({"prefab": a.prefab, "prefix": "RS", "styles": styles}, open(os.path.join(out, "styles.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)

    L = [f"# 骨架布局 {os.path.basename(a.prefab)}", "",
         f"- 来源：{spec.get('source')}（{W}x{H}）", f"- 手机画面：{note}；参考尺寸 {RW}x{RH}（缩放 {k:.4f}）",
         f"- 节点 {len(out_nodes)}（文字 {sum(1 for x in out_nodes if x['kind'] == 'text')}，图片 {sum(1 for x in out_nodes if x['kind'] == 'image')}，"
         f"组 {sum(1 for x in out_nodes if x['kind'] == 'group')}，按钮 {buttons}）；跳过隐藏图层 {hidden}，画面外 {len(dropped)}",
         f"- 文字样式 {len(styles)} 条 → styles.json（生成 prefab 后跑 ReskinTextStyle.Apply）", "", "## 节点树", ""]
    for x in out_nodes:
        depth = x["path"].count("/")
        extra = x["kind"]
        if x["kind"] == "text":
            extra += f" {x.get('fontSize')}px “{(x.get('text') or '')[:16]}”"
        elif x["kind"] == "image":
            extra += f" sprite={x.get('sprite')}{'' if x.get('png') else '（无导出图）'}"
        L.append(f"{'  ' * depth}- `{x['name']}` [{x['anchor']}] {x['rect']} {extra}{(' · ' + x['role'] + ('（形状推断）' if x.get('roleBasis') == 'shape' else '') + ((' “' + x['label'] + '”') if x.get('label') else '')) if x.get('role') else ''}{(' ' + x['listLayout']) if x.get('listLayout') else ''}{' · 默认隐藏' if not x.get('active', True) else ''}")
    notes = [x for x in out_nodes if x.get("shadowNote")]
    if notes:
        L += ["", "## 需手工处理", ""] + [f"- `{x['path']}`：{x['shadowNote']}" for x in notes]
    if missing_png:
        L += ["", f"## 没有导出图层 PNG 的图片（{len(missing_png)}，工程里也找不到同名 Sprite 时会放占位）", ""] + [f"- `{p}`" for p in missing_png[:80]]
    open(os.path.join(out, "layout.md"), "w", encoding="utf-8").write("\n".join(L) + "\n")
    print("\n".join(L[:6]))
    print(f"→ {out}/layout.json  styles.json  bindings.json  layout.md")


if __name__ == "__main__":
    main()
