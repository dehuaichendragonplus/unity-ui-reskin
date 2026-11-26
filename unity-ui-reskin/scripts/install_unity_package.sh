#!/bin/bash
# 把 UIReskin 编辑器工具装进当前 Unity 工程，作为嵌入式包 Packages/com.agent.uireskin（不改 manifest.json、不碰 Assets/）。
# - 自动找 Unity 工程（或第 1 个参数指定）；已装同版本就跳过，旧版本覆盖更新。
# - 默认把该目录写进 git 的 info/exclude（本机忽略，不会被误提交）；团队想共享时传 --track 并自行提交。
# - 卸载：删除 Packages/com.agent.uireskin 目录，并去掉 info/exclude 里那一行。
# 装完 Unity 会自动编译（asmdef 有版本宏保护：工程没有 TMP 时整个程序集跳过，不会让工程报错）。
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
SRC="$HERE/../unity-package/com.agent.uireskin"
TRACK=0; UNITY=""
for a in "$@"; do case "$a" in --track) TRACK=1;; *) UNITY="$a";; esac; done
[ -n "$UNITY" ] || UNITY="$(python3 -I "$HERE/uir_project.py" unity)"
[ -d "$UNITY/Assets" ] && [ -d "$UNITY/ProjectSettings" ] || { echo "不是 Unity 工程：$UNITY" >&2; exit 1; }
DST="$UNITY/Packages/com.agent.uireskin"
# 工程里已经自带一份 UIReskin（例：BubbleWord3 的 Assets/Editor/UIReskin）时不装，避免同名类型冲突，直接用工程自带的
if grep -rlq --include='*.cs' -E 'class ReskinCore\b' "$UNITY/Assets" 2>/dev/null; then
  echo "工程 Assets 下已有 UIReskin（class ReskinCore），不安装嵌入式包，直接使用工程自带版本：" ; grep -rl --include='*.cs' -E 'class ReskinCore\b' "$UNITY/Assets" | head -3
  exit 0
fi
ver() { python3 -c 'import json,sys;print(json.load(open(sys.argv[1]))["version"])' "$1" 2>/dev/null || echo none; }
NEW=$(ver "$SRC/package.json"); OLD=$(ver "$DST/package.json")
if [ "$OLD" = "$NEW" ] && diff -rq -x '*.meta' "$SRC" "$DST" >/dev/null 2>&1; then
  echo "已是最新：$DST（$NEW）"
else
  mkdir -p "$DST"
  # 只同步源文件，保留 Unity 生成的 .meta（guid 稳定）
  rsync -a --delete --exclude '*.meta' "$SRC/" "$DST/"
  echo "已安装 UIReskin $OLD → $NEW：$DST"
fi
if [ $TRACK -eq 0 ] && git -C "$UNITY" rev-parse --git-dir >/dev/null 2>&1; then
  TOP=$(git -C "$UNITY" rev-parse --show-toplevel)
  EXC=$(cd "$TOP" && git rev-parse --git-path info/exclude)
  case "$EXC" in /*) ;; *) EXC="$TOP/$EXC";; esac
  REL="/$(python3 -c 'import os,sys;print(os.path.relpath(sys.argv[1],sys.argv[2]))' "$DST" "$TOP")/"
  mkdir -p "$(dirname "$EXC")"; touch "$EXC"
  grep -qxF "$REL" "$EXC" || { echo "$REL" >> "$EXC"; echo "已写入本机忽略：$EXC ← $REL"; }
fi
echo "下一步：Unity 里等编译完成（MCP: wait_for_compilation / get_compilation_errors），再执行 UIReskin.ReskinConfig.Dump() 看自动探测结果。"
