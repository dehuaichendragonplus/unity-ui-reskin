"""UIReskin 工具链的零配置探测：Unity 工程、仓库根、资源仓库（含子模块）、本地化组件脚本、规范字体、效果图画面。

命令行（python3 -I uir_project.py <子命令>）：
  info [--unity DIR]            打印探测结果（代理首次在项目里使用时先跑这个）
  unity                         只打印 Unity 工程目录
  asset-repo <REV> [<REV2>]     打印包含该提交 / tag 的 git 仓库（主仓或子模块）
所有探测都可用参数覆盖，不读任何配置文件。
"""
import os
import re
import subprocess
import sys


def sh(args, cwd=None):
    r = subprocess.run(args, cwd=cwd, capture_output=True, text=True)
    return r.stdout.strip() if r.returncode == 0 else None


def git(repo, *args):
    return sh(["git", "-C", repo, "-c", "core.quotepath=off", *args])


def is_unity(d):
    return os.path.isdir(os.path.join(d, "Assets")) and os.path.isdir(os.path.join(d, "ProjectSettings"))


def find_unity(start=None, override=None):
    """从 start（默认当前目录）往上找 Unity 工程；找不到再在仓库根下 2 层内找（排除子模块里的工程，取最浅的）"""
    if override:
        d = os.path.abspath(override)
        if not is_unity(d):
            raise SystemExit(f"{d} 不是 Unity 工程（缺 Assets/ 或 ProjectSettings/）")
        return d
    d = os.path.abspath(start or os.getcwd())
    cur = d
    while True:
        if is_unity(cur):
            return cur
        nxt = os.path.dirname(cur)
        if nxt == cur:
            break
        cur = nxt
    root = git(d, "rev-parse", "--show-toplevel") or d
    subs = set(submodules(root))
    cands = []
    for dirpath, dirnames, _ in os.walk(root):
        depth = os.path.relpath(dirpath, root).count(os.sep) + (0 if dirpath == root else 1)
        dirnames[:] = [x for x in dirnames if not x.startswith(".") and x not in ("Library", "Temp", "node_modules", "Logs")]
        if depth >= 2:
            dirnames[:] = []
        if is_unity(dirpath):
            rel = os.path.relpath(dirpath, root)
            in_sub = any(rel == s or rel.startswith(s + os.sep) for s in subs)
            cands.append((in_sub, depth, dirpath))
            dirnames[:] = []
    if not cands:
        raise SystemExit("没找到 Unity 工程：在 Unity 工程目录里运行，或传 --unity <目录>")
    cands.sort()
    return cands[0][2]


def repo_root(path):
    return git(path, "rev-parse", "--show-toplevel") or os.path.abspath(path)


def submodules(root):
    out = git(root, "config", "--file", ".gitmodules", "--get-regexp", r"submodule\..*\.path") or ""
    return [line.split(" ", 1)[1] for line in out.splitlines() if " " in line]


def candidate_repos(unity):
    root = repo_root(unity)
    repos = [root] + [os.path.join(root, s) for s in submodules(root)]
    # Assets 下指向别的仓库的软链（资源在独立仓库、主工程软链进来）
    assets = os.path.join(unity, "Assets")
    for n in os.listdir(assets):
        p = os.path.join(assets, n)
        if os.path.islink(p):
            r = repo_root(os.path.realpath(p))
            if r not in repos:
                repos.append(r)
    return [r for r in repos if os.path.isdir(r)]


def find_asset_repo(unity, revs, override=None):
    """包含 revs 的仓库；多个仓库都能解析时，取 revs 区间改动 prefab 最多的那个"""
    if override:
        return os.path.abspath(override)
    hits = []
    for r in candidate_repos(unity):
        if all(rev == "WORKTREE" or git(r, "rev-parse", "--verify", "-q", rev + "^{commit}") for rev in revs):
            rng = [x for x in revs if x != "WORKTREE"]
            n = len((git(r, "diff", "--name-only", *rng, "--", "*.prefab") or "").splitlines())
            hits.append((n, r))
    if not hits:
        raise SystemExit(f"在 {candidate_repos(unity)} 里都找不到提交 {revs}；先 git fetch（子模块用 git -C <子模块> fetch --tags），或传 --repo")
    hits.sort(reverse=True)
    return hits[0][1]


ASSETS_RE = re.compile(r"(?:^|/)Assets/(.*)$")


def assets_rel(path_in_repo):
    """仓库内路径 → Assets 下的相对路径（取第一个 Assets/ 之后）；不在 Assets 下返回 None"""
    m = ASSETS_RE.search(path_in_repo.replace("\\", "/"))
    return m.group(1) if m else None


def walk_files(roots, suffix):
    """遍历多个根（跟随软链、按真实路径去重）"""
    seen = set()
    for base in roots:
        if not os.path.isdir(base):
            continue
        for dirpath, dirnames, files in os.walk(base, followlinks=True):
            real = os.path.realpath(dirpath)
            if real in seen:
                dirnames[:] = []
                continue
            seen.add(real)
            dirnames[:] = [x for x in dirnames if not x.startswith(".")]
            for f in files:
                if f.endswith(suffix):
                    yield os.path.join(dirpath, f)


def guid_roots(unity, asset_repo=None):
    roots = [os.path.join(unity, x) for x in ("Assets", "Packages", os.path.join("Library", "PackageCache"))]
    if asset_repo:
        roots.append(asset_repo)
    return roots


def meta_guid(path):
    try:
        with open(path, errors="ignore") as f:
            for line in f:
                if line.startswith("guid:"):
                    return line.split()[1]
    except OSError:
        pass
    return None


def find_script_guid(unity, class_name, asset_repo=None):
    """按脚本文件名（<类名>.cs）找 guid；找不到返回 (None, None)"""
    want = class_name + ".cs.meta"
    for p in walk_files(guid_roots(unity, asset_repo)[:2], ".cs.meta"):
        if os.path.basename(p) == want:
            return meta_guid(p), p[:-5]
    return None, None


def find_asset_guids(unity, names, suffix=".asset", asset_repo=None):
    """按资产文件名（不含扩展名）找 guid 集合 → {guid: 路径}"""
    want = {n + suffix + ".meta" for n in names}
    out = {}
    for p in walk_files(guid_roots(unity, asset_repo)[:1] + ([asset_repo] if asset_repo else []), suffix + ".meta"):
        if os.path.basename(p) in want:
            g = meta_guid(p)
            if g:
                out[g] = p[:-5]
    return out


# ---------- 效果图画面 ----------
def parse_frame(spec):
    """'x0,y0,x1,y1' → 元组；'auto' / 'full' 原样返回"""
    if spec in (None, "", "auto", "full"):
        return spec or "auto"
    v = [int(float(x)) for x in spec.split(",")]
    if len(v) != 4:
        raise SystemExit(f"--frame 需要 x0,y0,x1,y1，收到 {spec}")
    return tuple(v)


def auto_frame(rgb, tol=18, min_frac=0.02):
    """效果图四周是纯色 / 近纯色画布时，找出中间内容框（类似 ImageMagick -trim）。返回 (x0,y0,x1,y1, 是否裁了)"""
    import numpy as np
    a = np.asarray(rgb).astype(np.int16)
    h, w = a.shape[:2]
    corners = np.stack([a[0, 0], a[0, w - 1], a[h - 1, 0], a[h - 1, w - 1]])
    bg = np.median(corners, axis=0)
    diff = np.abs(a - bg).max(axis=2) > tol
    cols = np.where(diff.mean(axis=0) > min_frac)[0]
    rows = np.where(diff.mean(axis=1) > min_frac)[0]
    if len(cols) == 0 or len(rows) == 0:
        return 0, 0, w, h, False
    x0, x1, y0, y1 = int(cols[0]), int(cols[-1]) + 1, int(rows[0]), int(rows[-1]) + 1
    if (x1 - x0) >= 0.97 * w and (y1 - y0) >= 0.97 * h:
        return 0, 0, w, h, False
    return x0, y0, x1, y1, True


def apply_frame(im, spec, width=None):
    """PIL 图按画面规格裁剪并按宽缩放；返回 (新图, 说明)"""
    from PIL import Image
    f = parse_frame(spec)
    note = ""
    if f == "auto":
        x0, y0, x1, y1, cut = auto_frame(im.convert("RGB"))
        if cut:
            im = im.crop((x0, y0, x1, y1))
            note = f"auto 裁出内容框 {x0},{y0},{x1},{y1}（请目视确认是手机画面，不对就传 x0,y0,x1,y1）"
        else:
            note = ("⚠ auto：四周没有纯色边框，按整图处理。若这是「整屏 / 大画布设计 + 中间一块手机参考框」（四周是场景、参考框是浅色矩形），"
                    "必须先看图量出手机画面，再传 x0,y0,x1,y1 重跑（例：1200×1920 画布中间 768 宽一列 = 216,<y0>,984,<y1>）")
    elif isinstance(f, tuple):
        im = im.crop(f)
        note = f"按 {f} 裁剪"
    if width and im.width != width:
        im = im.resize((width, round(im.height * width / im.width)), Image.LANCZOS)
        note += f"；按宽缩放到 {im.size[0]}x{im.size[1]}"
    return im, note.lstrip("；")


def main():
    import argparse
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("cmd", choices=["info", "unity", "asset-repo"])
    ap.add_argument("revs", nargs="*")
    ap.add_argument("--unity")
    a = ap.parse_args()
    unity = find_unity(override=a.unity)
    if a.cmd == "unity":
        print(unity)
        return
    if a.cmd == "asset-repo":
        print(find_asset_repo(unity, a.revs or ["HEAD"]))
        return
    root = repo_root(unity)
    ver = ""
    pv = os.path.join(unity, "ProjectSettings", "ProjectVersion.txt")
    if os.path.exists(pv):
        ver = open(pv).readline().split(":", 1)[-1].strip()
    print(f"Unity 工程   {unity}（{ver}）")
    print(f"仓库根       {root}")
    print(f"子模块       {submodules(root) or '无'}")
    print(f"候选资源仓库 {candidate_repos(unity)}")
    pkg = os.path.join(unity, "Packages", "com.agent.uireskin", "package.json")
    print(f"UIReskin 包  {'已安装' if os.path.exists(pkg) else '未安装（scripts/install_unity_package.sh）'}")
    g, p = find_script_guid(unity, "LocalizationText")
    print(f"LocalizationText 脚本 {p or '无'}")
    g2, p2 = find_script_guid(unity, "LocalizeTextMeshProUGUI")
    print(f"LocalizeTextMeshProUGUI 脚本 {p2 or '无'}")
    fonts = find_asset_guids(unity, ["TMP_Font_Normal", "TMP_Font_Normal_Title"])
    print(f"TMP_Font_Normal 规范字体 {list(fonts.values()) or '无'}")
    mf = os.path.join(unity, "Packages", "manifest.json")
    if os.path.exists(mf):
        txt = open(mf).read()
        for k in ("com.unity.textmeshpro", "com.unity.ugui", "com.unity.2d.sprite", "com.funplay", "unity-mcp", "mcp"):
            if k in txt:
                print(f"manifest 含   {k}")


if __name__ == "__main__":
    main()
