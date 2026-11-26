"""运行时截图 vs 效果图：并排 + 50% 叠图 + 刻度线，用于 1:1 量偏差。

前提：运行时截图与效果图分辨率一致（Game 视图设成效果图尺寸，见 snippets/reskin/SetGameViewSize.cs 或 UIReskin.ReskinCore.SetGameViewSize）。
用法:
  python3 -I overlay.py runtime.png effect.png out.png [y0 y1] [--scale 0.66]
输出 out.png（全图）与 out_crop.png（按 y0..y1 裁切，可缩放后再看）。
三栏依次为：运行时 | 效果图 | 50% 叠加。红线每 100px、黄线每 50px，左侧标纵坐标。
叠加栏里出现「重影」的元素就是没对齐的，重影间距即偏差像素。
"""
import sys

from PIL import Image, ImageDraw


def main():
    raw = sys.argv[1:]
    scale = 1.0
    if "--scale" in raw:
        i = raw.index("--scale")
        scale = float(raw[i + 1])
        raw = raw[:i] + raw[i + 2:]
    args = raw
    rt, eff, out = args[0], args[1], args[2]
    a = Image.open(rt).convert("RGBA")
    e = Image.open(eff).convert("RGBA").resize(a.size)
    w, h = a.size
    c = Image.new("RGBA", (w * 3 + 20, h), (255, 0, 255, 255))
    c.paste(a, (0, 0))
    c.paste(e, (w + 10, 0))
    c.paste(Image.blend(a, e, 0.5), (2 * w + 20, 0))
    d = ImageDraw.Draw(c)
    for y in range(0, h, 50):
        d.line([(0, y), (c.size[0], y)], fill=(255, 0, 0, 255) if y % 100 == 0 else (255, 255, 0, 160))
        if y % 100 == 0:
            d.text((2, y + 1), str(y), fill=(255, 0, 0, 255))
    c.save(out)
    y0 = int(args[3]) if len(args) > 3 else 0
    y1 = int(args[4]) if len(args) > 4 else h
    crop = c.crop((0, y0, c.size[0], y1))
    if scale != 1.0:
        crop = crop.resize((int(crop.size[0] * scale), int(crop.size[1] * scale)))
    crop.save(out.replace(".png", "_crop.png"))


if __name__ == "__main__":
    main()
