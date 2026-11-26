"""UIReskin 工具链的项目识别与「项目事实」读写。

通用包里不内置任何项目 / 框架知识：用到时现场调研，核实后写进项目仓库的 .uireskin/facts.json（工具读）与 facts.md（人看：结论 + 证据 + 待核实），
下次直接读事实。本模块只做两件事：找工程 / 仓库这类纯结构探测，以及给调研列候选（不替项目下结论）。

命令行（python3 -I uir_project.py <子命令>）：
  info [--unity DIR]            工程 / 仓库 / 子模块 / 事实文件状态 + 调研候选（本地化组件、常用字体、参考分辨率）
  facts init                    在仓库根建 .uireskin/facts.json + facts.md 模板（已存在不覆盖）
  facts show                    打印当前事实，并列出空着的字段（要用到时先调研补齐）
  unity                         只打印 Unity 工程目录
  asset-repo <REV> [<REV2>]     打印包含该提交 / tag 的 git 仓库（主仓或子模块）
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


# ---------- 项目事实 ----------
FACTS_DIR = ".uireskin"
FACTS_TEMPLATE = {
    "schema": 1,
    "updated": "",
    "_说明": "UIReskin 项目事实：只写核实过的结论；证据、来源与待核实项写在同目录 facts.md。空字符串 / 空数组 = 未调研，工具会提示先调研。",
    "unity": {"project": "", "assetRepo": ""},
    "ui": {"refWidth": 0, "refHeight": 0, "match": "", "prefabDir": "", "spriteDir": "", "effectFrames": {}, "defaultFrame": ""},
    "text": {"defaultFont": "", "locTextType": "", "locMaterialField": "", "locMaterialPrefix": "", "locTermField": "",
             "fontRoot": "", "fontAssetPattern": "", "materialPattern": "", "defaultMaterialStyle": "", "fallbackSourceStyle": "",
             "defaultCloneSource": "", "reportLang": "", "outlineCalibration": 0, "healthFieldMatchesMaterial": False, "afterMaterialsHook": ""},
    "localization": {"component": "", "idField": "", "keyField": "", "requiredFonts": [], "titleFont": "", "materialMapFontPrefix": "",
                     "materialMap": [], "forbidUnder": [], "fixPolicy": ""},
    "framework": {"viewBase": "", "bindingAttribute": "", "bindingPathMember": "", "bindingOptionalMember": "", "bindingComponentMember": "",
                  "descriptorAttribute": "", "descriptorKeyMember": "",
                  "open": "", "close": "", "register": "", "referenceViews": []},
}
FACTS_MD_TEMPLATE = """# UIReskin 项目事实

> 由代理调研后写入，供 unity-ui-reskin skill 直接使用。**只记核实过的结论**，每条写证据（文件路径 / 类名 / 实测结果）；没把握的放「待核实」。
> 机器可读部分在同目录 `facts.json`，两边改动保持一致。推翻旧结论时在「变更记录」写一行原因。

## 画面与坐标
- UI 参考分辨率：
- 效果图规格与手机画面裁切：
- 新 prefab / 切图放哪：

## 文字与字体
- 本地化文字组件与样式字段：
- 规范字体 / 字体问题由谁改：
- 描边校准系数：

## UI 框架（写业务代码用）
- 界面基类与生命周期：
- 节点绑定方式：
- 注册方式：
- 打开 / 关闭界面：
- 参照界面：

## 资源管理与图集
- 资源加载方式（Resources / Addressables / YooAsset / 自研）与约定：
- 图集约定：

## 待核实

## 变更记录
"""


def facts_path(root):
    return os.path.join(root, FACTS_DIR, "facts.json")


def load_facts(unity=None, root=None):
    """读 .uireskin/facts.json；没有返回 {}（调用方按「未调研」处理）"""
    import json
    if root is None:
        try:
            root = repo_root(unity or find_unity())
        except SystemExit:
            return {}
    p = os.environ.get("UIRESKIN_FACTS") or facts_path(root)   # 环境变量可指向别处的事实文件（测试 / 多工程共用）
    if not os.path.exists(p):
        return {}
    with open(p, encoding="utf-8") as f:
        d = json.load(f)
    d["_path"] = p
    return d


def fact(facts, dotted, default=None):
    """facts.get('a.b.c')；空字符串 / 空数组 / 0 视为未填"""
    cur = facts
    for k in dotted.split("."):
        if not isinstance(cur, dict) or k not in cur:
            return default
        cur = cur[k]
    return default if cur in ("", [], {}, 0, None) else cur


def empty_fields(d, prefix=""):
    out = []
    for k, v in d.items():
        if k.startswith("_") or k in ("schema", "updated"):
            continue
        key = f"{prefix}{k}"
        if isinstance(v, dict) and v and k != "effectFrames":
            out += empty_fields(v, key + ".")
        elif v in ("", [], {}, 0, None):
            out.append(key)
    return out


# ---------- 调研候选（只列候选，不下结论） ----------
CLASS_RE = re.compile(r"\bclass\s+(\w*Locali[sz]\w*)\s*:\s*([\w.<>, ]+)")


def research_loc_components(unity):
    """项目源码里名字含 Locali(s|z)e 的组件类 → [(类名, 基类, 路径)]"""
    out = []
    for p in walk_files([os.path.join(unity, "Assets"), os.path.join(unity, "Packages")], ".cs"):
        try:
            with open(p, errors="ignore") as f:
                txt = f.read()
        except OSError:
            continue
        if "/Editor/" in p.replace("\\", "/"):
            continue
        for m in CLASS_RE.finditer(txt):
            if re.search(r"(Attribute|Editor|System|Drawer|Preference|Manager|Config|Settings?|Provider|Service|Table|Data)$", m.group(1)):
                continue
            out.append((m.group(1), m.group(2).strip(), os.path.relpath(p, unity)))
    return out


FONT_REF_RE = re.compile(r"m_fontAsset: \{fileID: -?\d+, guid: ([0-9a-f]{32})")
REFRES_RE = re.compile(r"m_ReferenceResolution: \{x: ([\d.]+), y: ([\d.]+)\}")


def research_prefab_usage(unity, limit=3000):
    """抽样 prefab / 场景：TMP 字体引用次数排行、CanvasScaler 参考分辨率排行"""
    import collections
    fonts, refres, n = collections.Counter(), collections.Counter(), 0
    for p in walk_files([os.path.join(unity, "Assets")], ".prefab"):
        n += 1
        if n > limit:
            break
        try:
            with open(p, errors="ignore") as f:
                txt = f.read()
        except OSError:
            continue
        fonts.update(FONT_REF_RE.findall(txt))
        refres.update(f"{float(a):g}x{float(b):g}" for a, b in REFRES_RE.findall(txt))
    for p in walk_files([os.path.join(unity, "Assets")], ".unity"):
        try:
            with open(p, errors="ignore") as f:
                refres.update(f"{float(a):g}x{float(b):g}" for a, b in REFRES_RE.findall(f.read()))
        except OSError:
            pass
    names = {}
    if fonts:
        want = set(g for g, _ in fonts.most_common(8))
        for mp in walk_files([os.path.join(unity, "Assets"), os.path.join(unity, "Packages")], ".asset.meta"):
            g = meta_guid(mp)
            if g in want:
                names[g] = os.path.basename(mp)[:-len(".asset.meta")]
    return [(names.get(g, g[:8]), c) for g, c in fonts.most_common(8)], refres.most_common(4), min(n, limit)


# ---------- 效果图画面 ----------
def parse_frame(spec, facts=None):
    """'x0,y0,x1,y1' → 元组；'auto' / 'full' 原样返回；其他名字到项目事实 ui.effectFrames 里查"""
    if spec in (None, "", "auto", "full"):
        return spec or "auto"
    frames = fact(facts or {}, "ui.effectFrames", {}) or {}
    if spec in frames:
        return tuple(int(v) for v in frames[spec])
    if not re.match(r"^[\d.,\s-]+$", spec):
        raise SystemExit(f"--frame {spec}：项目事实 ui.effectFrames 里没有这个名字（现有 {list(frames)}）；或传 auto / full / x0,y0,x1,y1")
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


def apply_frame(im, spec, width=None, facts=None):
    """PIL 图按画面规格裁剪并按宽缩放；返回 (新图, 说明)"""
    from PIL import Image
    f = parse_frame(spec, facts)
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
    import json
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("cmd", choices=["info", "unity", "asset-repo", "facts"])
    ap.add_argument("args", nargs="*")
    ap.add_argument("--unity")
    a = ap.parse_args()
    unity = find_unity(override=a.unity)
    if a.cmd == "unity":
        print(unity)
        return
    if a.cmd == "asset-repo":
        print(find_asset_repo(unity, a.args or ["HEAD"]))
        return
    root = repo_root(unity)
    if a.cmd == "facts":
        sub_cmd = (a.args or ["show"])[0]
        p = facts_path(root)
        if sub_cmd == "init":
            os.makedirs(os.path.dirname(p), exist_ok=True)
            if os.path.exists(p):
                print(f"已存在，不覆盖：{p}")
            else:
                t = json.loads(json.dumps(FACTS_TEMPLATE))
                t["unity"]["project"] = os.path.relpath(unity, root)
                with open(p, "w", encoding="utf-8") as f:
                    json.dump(t, f, ensure_ascii=False, indent=2)
                print(f"已建 {p}")
            md = os.path.join(os.path.dirname(p), "facts.md")
            if not os.path.exists(md):
                with open(md, "w", encoding="utf-8") as f:
                    f.write(FACTS_MD_TEMPLATE)
                print(f"已建 {md}")
            return
        d = load_facts(root=root)
        if not d:
            print(f"没有项目事实：{p}（先 facts init，再按需调研补齐）")
            return
        print(json.dumps({k: v for k, v in d.items() if not k.startswith("_")}, ensure_ascii=False, indent=2))
        print("\n未填（用到时先调研）：" + "、".join(empty_fields(d)))
        return
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
    d = load_facts(root=root)
    if d:
        miss = empty_fields(d)
        print(f"项目事实     {d['_path']}（更新 {d.get('updated') or '?'}；未填 {len(miss)} 项，facts show 查看）")
    else:
        print(f"项目事实     无（{facts_path(root)}）：用到哪项调研哪项，核实后写入；facts init 建模板")
    print("\n## 调研候选（不是结论：核实后写进项目事实）")
    locs = research_loc_components(unity)
    print("本地化组件候选：" + ("；".join(f"{n} : {b}（{p}）" for n, b, p in locs[:8]) or "无"))
    fonts, refres, n = research_prefab_usage(unity)
    print(f"TMP 字体引用排行（抽样 {n} 个 prefab）：" + ("，".join(f"{nm} ×{c}" for nm, c in fonts) or "无"))
    print("CanvasScaler 参考分辨率排行：" + ("，".join(f"{r} ×{c}" for r, c in refres) or "无"))
    mf = os.path.join(unity, "Packages", "manifest.json")
    if os.path.exists(mf):
        txt = open(mf).read()
        pk = [k for k in ("com.unity.textmeshpro", "com.unity.ugui", "com.unity.2d.sprite", "com.unity.addressables", "com.tuyoogame.yooasset", "yooasset") if k in txt]
        print("相关包：" + ("、".join(pk) or "无"))


if __name__ == "__main__":
    main()
