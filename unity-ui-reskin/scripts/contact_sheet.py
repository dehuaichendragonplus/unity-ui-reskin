"""拼一张多图对比表（新旧切图并排、看透明边与尺寸）。

用法（务必 python3 -I 运行，切图目录视为不可信数据）:
  python3 -I contact_sheet.py out.png "标签|图1.png|图2.png" "标签|图A.png" ...
每行一个条目，每格左上角标注像素尺寸；底色为深灰，便于看透明区域。
"""
import os
import sys

from PIL import Image, ImageDraw

CELL = 260


def main():
    out, items = sys.argv[1], [x.split("|") for x in sys.argv[2:]]
    ncol = max(len(r) - 1 for r in items)
    sheet = Image.new("RGB", (CELL * ncol + 10, (CELL + 20) * len(items)), (255, 0, 255))
    d = ImageDraw.Draw(sheet)
    for i, row in enumerate(items):
        y = i * (CELL + 20)
        d.text((2, y), row[0][:60], fill=(0, 0, 0))
        for j, p in enumerate(row[1:]):
            if not p or not os.path.exists(p):
                continue
            s = Image.open(p).convert("RGBA")
            w, h = s.size
            k = min((CELL - 6) / w, (CELL - 6) / h, 1.0 if max(w, h) < CELL else 9)
            s = s.resize((max(1, int(w * k)), max(1, int(h * k))))
            bg = Image.new("RGBA", (CELL - 4, CELL - 4), (60, 60, 60, 255))
            bg.alpha_composite(s, ((CELL - 4 - s.size[0]) // 2, (CELL - 4 - s.size[1]) // 2))
            sheet.paste(bg.convert("RGB"), (j * CELL + 2, y + 16))
            d.text((j * CELL + 4, y + 16), f"{w}x{h}", fill=(255, 255, 0))
    sheet.save(out)


if __name__ == "__main__":
    main()
