#!/usr/bin/env python3
"""把资源仓库里 <BASE>..<TO> 之间改过的 prefab，按 <SNAPSHOT_REV>（默认 BASE）的内容导出到 Unity 工程 Assets/<DEST>/，供 Unity 里做新旧对比。

零配置：自动找 Unity 工程，以及包含 BASE 的仓库（主仓 / 子模块 / Assets 下软链进来的资源仓库）。
导出路径 = Assets/<DEST>/<prefab 在 Assets 之下的相对路径>。不导出 .meta（Unity 分配新 guid，不和原 prefab 冲突）。
嵌套 prefab / Variant 的引用仍指向当前版本的源 prefab。用完必须删除（目录 + 同名 .meta），不要带着它跑测试或提交。

用法：python3 -I export_snapshot.py <BASE> <TO> <DEST> [SNAPSHOT_REV] [--unity DIR] [--repo DIR]
  旧版参照：export_snapshot.py ad8e6f36 HEAD _TmpUIAuditOld
  美术版参照：export_snapshot.py ad8e6f36 HEAD _TmpUIAuditArt 479b72f3
  TO 写 WORKTREE 表示和资源仓库工作区比（含未提交改动）
"""
import argparse
import os
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import uir_project as up  # noqa: E402


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("base"); ap.add_argument("to"); ap.add_argument("dest"); ap.add_argument("snap", nargs="?")
    ap.add_argument("--unity"); ap.add_argument("--repo")
    a = ap.parse_args()
    if not a.dest.startswith("_Tmp"):
        raise SystemExit("DEST 必须以 _Tmp 开头，避免误写进正式目录")
    snap = a.snap or a.base
    unity = up.find_unity(override=a.unity)
    repo = up.find_asset_repo(unity, [a.base, a.to], override=a.repo)
    out = os.path.join(unity, "Assets", a.dest)
    rng = [a.base] if a.to == "WORKTREE" else [a.base, a.to]
    files = (up.git(repo, "diff", "--name-only", *rng, "--", "*.prefab") or "").splitlines()
    n = missing = skipped = 0
    for p in files:
        rel = up.assets_rel(p)
        if not rel:
            skipped += 1
            continue
        r = subprocess.run(["git", "-C", repo, "show", f"{snap}:{p}"], capture_output=True)
        if r.returncode != 0:
            print(f"  {snap} 中不存在（新增或已删）：{rel}")
            missing += 1
            continue
        dst = os.path.join(out, rel)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        with open(dst, "wb") as f:
            f.write(r.stdout)
        n += 1
    print(f"[Unity {unity}] [资源仓库 {repo}]")
    print(f"导出 {n} 个 prefab 到 Assets/{a.dest}（{missing} 个在 {snap} 中不存在，{skipped} 个不在 Assets 下跳过）")
    print(f'用完删除：rm -rf "{out}" "{out}.meta"')


if __name__ == "__main__":
    main()
