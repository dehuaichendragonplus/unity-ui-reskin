"""按颜色测量元素包围盒，用来精确量「效果图里某个元素多大、在哪」。

用法:
  python3 -I measure.py image.png x0 y0 x1 y1 preset
preset:
  green   绿色按钮（g>170 r<120 b<90）
  blue    蓝色面板（b>200 r<80 120<g<220）
  yellow  黄色票面/按钮（r>220 g>170 b<90）
  alpha   不透明像素（a>20，适合透明底的离屏渲染图）
输出该区域内命中像素的包围盒 (x0,y0,x1,y1) 以及宽高、中心点。
两张图各量一次，相减就是要改的尺寸/位置。
"""
import sys

from PIL import Image

PRESETS = {
    "green": lambda r, g, b, a: g > 170 and r < 120 and b < 90,
    "blue": lambda r, g, b, a: a > 200 and b > 200 and r < 80 and 120 < g < 220,
    "yellow": lambda r, g, b, a: r > 220 and g > 170 and b < 90,
    "alpha": lambda r, g, b, a: a > 20,
}


def main():
    path, x0, y0, x1, y1, preset = sys.argv[1], *map(int, sys.argv[2:6]), sys.argv[6]
    im = Image.open(path).convert("RGBA")
    px = im.load()
    hit = PRESETS[preset]
    xs, ys = [], []
    for y in range(max(0, y0), min(im.size[1], y1)):
        for x in range(max(0, x0), min(im.size[0], x1)):
            if hit(*px[x, y]):
                xs.append(x)
                ys.append(y)
    if not xs:
        print("no hit")
        return
    bb = (min(xs), min(ys), max(xs), max(ys))
    print(f"bbox={bb} size={bb[2] - bb[0]}x{bb[3] - bb[1]} center=({(bb[0] + bb[2]) // 2},{(bb[1] + bb[3]) // 2})")


if __name__ == "__main__":
    main()
