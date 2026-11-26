#!/usr/bin/env python3
# 多分辨率截图矩阵 → 一张总览图（统一缩放到同一高度，标注分辨率 / 长宽比 / 是否 Pad）。
# 输入：ReskinMatrix.Run 的输出目录（每个分辨率一个子目录，内含 runtime.png，可选 runtime_guides.png）
# 用法：python -I matrix.py <matrix目录> [--height 900] [--guides]  → <matrix目录>/matrix.png
import argparse, json, os, sys
import numpy as np, cv2
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from reskin_common import imread, imwrite

ap = argparse.ArgumentParser()
ap.add_argument("dir")
ap.add_argument("--height", type=int, default=900)
ap.add_argument("--guides", action="store_true", help="优先用带辅助线的截图")
a = ap.parse_args()

def key(s):
    try:
        w, h = s.split("x"); return int(h) / int(w)
    except ValueError:
        return 0

tiles = []
for sub in sorted(os.listdir(a.dir), key=key, reverse=True):
    d = os.path.join(a.dir, sub)
    if not os.path.isdir(d):
        continue
    f = os.path.join(d, "runtime_guides.png" if a.guides and os.path.exists(os.path.join(d, "runtime_guides.png")) else "runtime.png")
    if not os.path.exists(f):
        continue
    im = imread(f)
    h, w = im.shape[:2]
    s = a.height / h
    im = cv2.resize(im, (int(w * s), a.height), interpolation=cv2.INTER_AREA)
    pad = ""
    rj = os.path.join(d, "runtime.json")
    if os.path.exists(rj):
        pad = " Pad" if json.load(open(rj)).get("pad") else ""
    label = f"{sub}  {h / w:.2f}{pad}"
    bar = np.full((34, im.shape[1], 3), 32, np.uint8)
    cv2.putText(bar, label, (8, 23), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 255, 255), 1, cv2.LINE_AA)
    tiles.append(np.vstack([bar, im]))
if not tiles:
    sys.exit("没有找到截图")
gap = np.full((tiles[0].shape[0], 12, 3), 255, np.uint8)
row = tiles[0]
for t in tiles[1:]:
    row = np.hstack([row, gap, t])
out = os.path.join(a.dir, "matrix.png")
imwrite(out, row)
print(f"{len(tiles)} 张 → {out}")
