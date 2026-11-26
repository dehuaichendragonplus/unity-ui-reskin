#!/bin/bash
# 一次性环境：Python venv（OpenCV / psd-tools）+ 编译 Vision OCR。产物在 ~/.cache/ui-reskin，不进仓库。
set -e
C="$HOME/.cache/ui-reskin"; mkdir -p "$C"
HERE="$(cd "$(dirname "$0")" && pwd)"
[ -x "$C/venv/bin/python" ] || python3 -m venv "$C/venv"
"$C/venv/bin/pip" -q install --disable-pip-version-check opencv-python-headless numpy pillow psd-tools
swiftc -O "$HERE/ocr.swift" -o "$C/ocr"
echo "ok: $C/venv/bin/python  $C/ocr"
