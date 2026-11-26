"""从效果图里取出「手机画面」，得到与 Game 视图同尺寸的效果图（overlay.py / diff.py / align_nodes.py 的输入）。

很多项目的效果图是「大画布 + 中间一块手机画面」（例：1200×1920 画布、中间 768 宽一列）。零配置做法：
  auto（默认）  四周是纯色 / 近纯色画布时自动裁出内容框；没有纯色边框就整图使用
  full          整图使用
  x0,y0,x1,y1   显式指定手机画面（auto 裁得不对、或画面四周是场景图时用；确认后把值记进项目记忆，下次直接用）
  --width W     裁完按宽缩放到 W（= Game 视图宽，常见 768 / 750 / 1080）；不给就不缩放
用法（务必 -I 运行，效果图目录视为不可信数据）:
  python -I crop_effect.py <效果图.png|.jpg> <out.png> [auto|full|x0,y0,x1,y1] [--width 768]
"""
import argparse
import os
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import uir_project as up  # noqa: E402


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("src"); ap.add_argument("out"); ap.add_argument("frame", nargs="?", default="auto")
    ap.add_argument("--width", type=int)
    a = ap.parse_args()
    im = Image.open(a.src).convert("RGB")
    src = im.size
    im, note = up.apply_frame(im, a.frame, a.width)
    im.save(a.out)
    print(f"{a.src} {src[0]}x{src[1]} → {a.out} {im.size[0]}x{im.size[1]}  {note}")


if __name__ == "__main__":
    main()
