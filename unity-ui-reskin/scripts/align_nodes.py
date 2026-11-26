"""逐节点局部模板匹配：从运行时截图裁出每个节点，在效果图同位置 ±R 像素内做多尺度匹配，报告偏移与缩放。

比 diff.py 轻：不需要 runtime.json，只要一份「节点名|x0,y0,x1,y1」清单（屏幕像素、左上原点，与截图同尺寸），
适合快速核对某几个元素；文字内容不同（效果图英文 / 实机中文）时匹配分会低，低于 0.55 的标出来、不要据此改。

用法（python3 -I；需要 OpenCV，用 ~/.cache/ui-reskin/venv/bin/python）:
  python -I align_nodes.py <效果图> <运行时截图> <节点清单.txt> [搜索半径=160] [--frame auto|full|x0,y0,x1,y1] [--smin 0.85] [--smax 1.2]
  效果图可以是大画布原图（按 --frame 取手机画面，规则同 crop_effect.py）或已裁好的图；取出后自动缩放到运行时截图的宽度。
输出每行：节点  匹配分  缩放（效果图/实机）  偏移(dx,dy)（效果图 − 实机，屏幕像素，y 向下）
偏移是屏幕像素；换成节点本地单位要除以 runtime.json 里该节点的 ppu（父节点单位 → 屏幕像素）。
"""
import argparse
import os
import sys

import cv2
import numpy as np
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import uir_project as up  # noqa: E402


def load(path):
    return cv2.cvtColor(np.array(Image.open(path).convert("RGB")), cv2.COLOR_RGB2BGR)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("effect"); ap.add_argument("runtime"); ap.add_argument("nodes")
    ap.add_argument("radius", nargs="?", type=int, default=160)
    ap.add_argument("--frame", default="auto")
    ap.add_argument("--smin", type=float, default=0.85); ap.add_argument("--smax", type=float, default=1.2)
    a = ap.parse_args()
    run = load(a.runtime)
    eim, note = up.apply_frame(Image.open(a.effect).convert("RGB"), a.frame, run.shape[1])
    print("效果图：" + note)
    eff = cv2.cvtColor(np.array(eim), cv2.COLOR_RGB2BGR)
    H, W = eff.shape[:2]
    for line in open(a.nodes, encoding="utf-8"):
        line = line.strip()
        if not line or line.startswith("#") or "|" not in line:
            continue
        name, box = line.rsplit("|", 1)
        x0, y0, x1, y1 = [int(float(v)) for v in box.split(",")]
        x0, y0, x1, y1 = max(0, x0), max(0, y0), min(W, x1), min(H, y1)
        if x1 - x0 < 24 or y1 - y0 < 24:
            continue
        t = run[y0:y1, x0:x1]
        if t.std() < 8:
            continue
        R = a.radius
        sx0, sy0, sx1, sy1 = max(0, x0 - R), max(0, y0 - R), min(W, x1 + R), min(H, y1 + R)
        win = eff[sy0:sy1, sx0:sx1]
        best = None
        for s in np.arange(a.smin, a.smax + 1e-6, 0.01):
            tt = cv2.resize(t, None, fx=s, fy=s, interpolation=cv2.INTER_AREA)
            if tt.shape[0] >= win.shape[0] or tt.shape[1] >= win.shape[1]:
                continue
            _, mv, _, ml = cv2.minMaxLoc(cv2.matchTemplate(win, tt, cv2.TM_CCOEFF_NORMED))
            if best is None or mv > best[0]:
                best = (mv, s, ml, tt.shape)
        if best is None:
            continue
        mv, s, (x, y), (h, w, _) = best
        ex, ey = sx0 + x + w / 2, sy0 + y + h / 2
        rx, ry = (x0 + x1) / 2, (y0 + y1) / 2
        edge = "  ⚠缩放到搜索边界，放宽 --smin/--smax 重跑" if abs(s - a.smin) < 0.005 or abs(s - a.smax) < 0.005 else ""
        low = "  （低分，忽略）" if mv < 0.55 else ""
        print(f"{name:40s} 分{mv:.2f} 缩放{s:.2f} 偏移({ex - rx:+.0f},{ey - ry:+.0f}) 实机c({rx:.0f},{ry:.0f}) 尺寸{x1 - x0}x{y1 - y0}{low}{edge}")


if __name__ == "__main__":
    main()
