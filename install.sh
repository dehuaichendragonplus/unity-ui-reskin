#!/bin/bash
# 安装 unity-ui-reskin skill 到 Claude Code 用户级目录（~/.claude/skills/unity-ui-reskin），之后任何 Unity 项目里都能直接用。
# 用法：bash install.sh            # 安装 / 升级
#       bash install.sh --python   # 同时建 Python 环境（~/.cache/ui-reskin/venv，OpenCV / psd-tools / OCR），首次用到时代理也会自动建
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
DST="$HOME/.claude/skills/unity-ui-reskin"
mkdir -p "$HOME/.claude/skills"
if [ -d "$DST" ]; then echo "升级：$DST"; else echo "安装：$DST"; fi
rsync -a --delete --exclude '__pycache__' --exclude '*.pyc' --exclude '.DS_Store' "$HERE/unity-ui-reskin/" "$DST/"
chmod +x "$DST"/scripts/*.sh "$DST"/scripts/*.py
[ "${1:-}" = "--python" ] && bash "$DST/scripts/setup.sh"
echo "完成。新开 Claude Code 会话后，说「按效果图替换 XX 界面」或「美术换皮了，审查一下 <tag>」即可触发。"
