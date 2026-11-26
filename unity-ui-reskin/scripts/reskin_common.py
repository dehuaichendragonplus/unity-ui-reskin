# UIReskin Python 公共库：读图、局部配准（运行时节点截图 → 效果图）、Vision OCR、文字取色。
# 依赖由 setup.sh 安装到 ~/.cache/ui-reskin/venv；脚本一律用该 venv 的 python -I 运行。
import json, os, subprocess, re
import numpy as np
import cv2

CACHE = os.path.expanduser("~/.cache/ui-reskin")
HERE = os.path.dirname(os.path.abspath(__file__))


def imread(p, alpha=False):
    im = cv2.imdecode(np.fromfile(p, np.uint8), cv2.IMREAD_UNCHANGED)
    if im is None:
        raise SystemExit(f"读不到图片: {p}")
    if im.ndim == 2:
        im = cv2.cvtColor(im, cv2.COLOR_GRAY2BGR)
    if not alpha and im.shape[2] == 4:
        a = im[:, :, 3:4].astype(np.float32) / 255
        im = (im[:, :, :3].astype(np.float32) * a).astype(np.uint8)  # 透明处按黑底合成
    return im


def imwrite(p, im):
    os.makedirs(os.path.dirname(os.path.abspath(p)), exist_ok=True)
    cv2.imencode(".png", im)[1].tofile(p)


# ---------------- 局部配准 ----------------
def _match(hay, tpl, mask=None):
    if tpl.shape[0] > hay.shape[0] or tpl.shape[1] > hay.shape[1] or tpl.shape[0] < 4 or tpl.shape[1] < 4:
        return -1, (0, 0)
    if float(tpl.std()) < 2.0:  # 纯色块无法配准
        return -1, (0, 0)
    r = cv2.matchTemplate(hay, tpl, cv2.TM_CCOEFF_NORMED, mask=mask)
    r = np.nan_to_num(r, nan=-1, posinf=-1, neginf=-1)
    _, v, _, loc = cv2.minMaxLoc(r)
    return v, loc


def register(rt_gray, mk_gray, rect, window=None, scales=None, holes=None):
    """把运行时截图中 rect 区域当模板，在效果图对应位置附近搜索（多尺度）。
    返回 (score, [x,y,w,h] 效果图矩形, scale)。rect 为 x,y,w,h（像素、左上原点）。"""
    H, W = rt_gray.shape
    x, y, w, h = [int(round(v)) for v in rect]
    x0, y0, x1, y1 = max(0, x), max(0, y), min(W, x + w), min(H, y + h)
    if x1 - x0 < 6 or y1 - y0 < 6:
        return -1, None, 1
    tpl0 = rt_gray[y0:y1, x0:x1]
    tw, th = x1 - x0, y1 - y0
    mask0 = None
    if holes:
        mask0 = np.full((th, tw), 255, np.uint8)
        for hx, hy, hw, hh in holes:
            mask0[int(max(0, hy - y0 - 2)):int(max(0, hy - y0 + hh + 2)), int(max(0, hx - x0 - 2)):int(max(0, hx - x0 + hw + 2))] = 0
        if mask0.mean() < 255 * 0.3:
            mask0 = None  # 洞太大就不挖了
    if window is None:
        window = int(min(160, max(40, 0.25 * max(tw, th))))
    if scales is None:
        scales = np.arange(0.86, 1.141, 0.02) if max(tw, th) > 60 else np.arange(0.8, 1.201, 0.04)
    best = (-1, None, 1)
    for s in scales:
        sw, sh = max(4, int(round(tw * s))), max(4, int(round(th * s)))
        tpl = cv2.resize(tpl0, (sw, sh), interpolation=cv2.INTER_AREA if s < 1 else cv2.INTER_LINEAR)
        msk = cv2.resize(mask0, (sw, sh), interpolation=cv2.INTER_NEAREST) if mask0 is not None else None
        cx, cy = x0 + tw / 2, y0 + th / 2
        X0, Y0 = int(max(0, cx - sw / 2 - window)), int(max(0, cy - sh / 2 - window))
        X1, Y1 = int(min(mk_gray.shape[1], cx + sw / 2 + window)), int(min(mk_gray.shape[0], cy + sh / 2 + window))
        v, loc = _match(mk_gray[Y0:Y1, X0:X1], tpl, msk)
        if v > best[0]:
            best = (v, [X0 + loc[0], Y0 + loc[1], sw, sh], s)
    v, r, s = best
    if r is None:
        return -1, None, 1
    # 回到「节点矩形」坐标（rect 可能被屏幕裁剪过）
    r = [r[0] - (x0 - x) * s, r[1] - (y0 - y) * s, w * s, h * s]
    return v, r, s


def refine_edges(rt_gray, mk_gray, rect, mk_rect, band=0.22, window=24):
    """大元素：左/右/上/下四条边带分别配准，得到非等比的宽高（九宫格拉伸）。失败返回 None。"""
    x, y, w, h = rect
    if w < 120 and h < 120:
        return None
    sx = mk_rect[2] / w
    out = {}
    H, W = rt_gray.shape
    for side in ("l", "r", "t", "b"):
        if side in "lr":
            bw = int(min(80, max(20, w * band)))
            bx = x if side == "l" else x + w - bw
            sub = (bx, y + h * 0.1, bw, h * 0.8)
        else:
            bh = int(min(80, max(20, h * band)))
            by = y if side == "t" else y + h - bh
            sub = (x + w * 0.1, by, w * 0.8, bh)
        sx0, sy0 = int(max(0, sub[0])), int(max(0, sub[1]))
        sx1, sy1 = int(min(W, sub[0] + sub[2])), int(min(H, sub[1] + sub[3]))
        if sx1 - sx0 < 8 or sy1 - sy0 < 8:
            return None
        tpl = rt_gray[sy0:sy1, sx0:sx1]
        tpl = cv2.resize(tpl, (max(4, int((sx1 - sx0) * sx)), max(4, int((sy1 - sy0) * sx))))
        # 预测位置 = 等比结果中的同一边带
        px = mk_rect[0] + (sx0 - x) * sx
        py = mk_rect[1] + (sy0 - y) * sx
        X0, Y0 = int(max(0, px - window)), int(max(0, py - window))
        X1 = int(min(mk_gray.shape[1], px + tpl.shape[1] + window))
        Y1 = int(min(mk_gray.shape[0], py + tpl.shape[0] + window))
        v, loc = _match(mk_gray[Y0:Y1, X0:X1], tpl)
        if v < 0.6:
            return None
        out[side] = (X0 + loc[0] - (sx0 - x) * sx if side == "l" else None,
                     X0 + loc[0] + tpl.shape[1] + (x + w - sx1) * sx if side == "r" else None,
                     Y0 + loc[1] - (sy0 - y) * sx if side == "t" else None,
                     Y0 + loc[1] + tpl.shape[0] + (y + h - sy1) * sx if side == "b" else None)
    L, R, T, B = out["l"][0], out["r"][1], out["t"][2], out["b"][3]
    if R - L < 8 or B - T < 8:
        return None
    return [L, T, R - L, B - T]


# ---------------- OCR ----------------
def ocr_bin():
    b = os.path.join(CACHE, "ocr")
    if not os.path.exists(b):
        subprocess.run(["swiftc", "-O", os.path.join(HERE, "ocr.swift"), "-o", b], check=True)
    return b


def ocr(path, langs="en-US,zh-Hans"):
    cache = path + ".ocr.json"
    if os.path.exists(cache) and os.path.getmtime(cache) > os.path.getmtime(path):
        return json.load(open(cache))
    out = subprocess.run([ocr_bin(), path, langs], capture_output=True, text=True, check=True).stdout
    res = json.loads(out)
    json.dump(res, open(cache, "w"), ensure_ascii=False)
    return res


def norm_text(s):
    return re.sub(r"[\s\W_]+", "", (s or "").lower())


# ---------------- 文字取色 ----------------
def text_colors(img, rect):
    """在文字行框内用 3-means 分出 背景 / 描边 / 填充：
    背景 = 占边框像素最多的簇；剩下两簇里离背景更「深入字形内部」（距离变换更大）的是填充。"""
    x, y, w, h = [int(round(v)) for v in rect]
    x, y = max(0, x), max(0, y)
    roi = img[y:y + h, x:x + w]
    if roi.size == 0 or roi.shape[0] < 6 or roi.shape[1] < 6:
        return None
    Z = roi.reshape(-1, 3).astype(np.float32)
    k = 3
    crit = (cv2.TERM_CRITERIA_EPS + cv2.TERM_CRITERIA_MAX_ITER, 20, 1.0)
    _, lab, cen = cv2.kmeans(Z, k, None, crit, 3, cv2.KMEANS_PP_CENTERS)
    lab = lab.reshape(roi.shape[:2])
    border = np.concatenate([lab[0], lab[-1], lab[:, 0], lab[:, -1]])
    bg = np.bincount(border, minlength=k).argmax()
    others = [i for i in range(k) if i != bg]
    nonbg = (lab != bg).astype(np.uint8)
    dist = cv2.distanceTransform(nonbg, cv2.DIST_L2, 3)
    depth = {i: float(dist[lab == i].mean()) if (lab == i).any() else 0 for i in others}
    fill = max(others, key=lambda i: depth[i])
    stroke = min(others, key=lambda i: depth[i])
    # 描边宽度 ≈ 描边像素数 / 填充轮廓周长
    fm = (lab == fill).astype(np.uint8)
    cnts, _ = cv2.findContours(fm, cv2.RETR_LIST, cv2.CHAIN_APPROX_NONE)
    perim = sum(len(c) for c in cnts) or 1
    stroke_w = float((lab == stroke).sum()) / perim
    # 阴影：描边质心相对填充质心的偏移（像素，y 向下为正）
    ys, xs = np.nonzero(lab == stroke); fy, fx = np.nonzero(lab == fill)
    off = [float(xs.mean() - fx.mean()), float(ys.mean() - fy.mean())] if len(xs) and len(fx) else [0, 0]
    bgr = lambda i: [int(v) for v in cen[i][::-1]]  # → RGB
    return {"fill": bgr(fill), "stroke": bgr(stroke), "bg": bgr(bg), "strokeW": round(stroke_w, 2), "shadowOff": [round(off[0], 1), round(off[1], 1)]}


def color_dist(a, b):
    if a is None or b is None:
        return None
    return float(np.linalg.norm(np.array(a[:3], float) - np.array(b[:3], float)))
