#!/usr/bin/env python3
"""界面换皮审查的 git 侧统计（零配置：自动找 Unity 工程与包含 BASE 的资源仓库 / 子模块）。只读 prefab YAML，不需要 Unity。

子命令：
  fonts   <BASE> <TO>   改了字体的 TMP 节点：按"原字体 -> 新字体"汇总，并按 prefab 列出；Resources 下的 prefab 单独标出
  locfont <BASE> <TO>   挂了本地化组件却不是规范字体的节点，标出"本轮改坏 / 本轮新挂 / 原本就不是"。
                        组件与规范字体取项目事实 localization.component / requiredFonts（参数可覆盖）；未调研时跳过并提示
                        --appendix <文件>  另外输出 Markdown 附录表（prefab | 节点路径(多语言 id) | 状态）
  refs    <BASE> <TO>   改过的 prefab 里：引用了工程中不存在的 guid（断引用，多为美术遗留的参考图 / 字体）、
                        旧版 UnityEngine.UI.Text 节点（代码按 TMP 取会取空）。只报 <TO> 里新出现的，原有的单独计数
                        --forbid-loc-under <路径片段>  路径含该片段的 prefab 里出现本地化组件也报（默认取项目事实 localization.forbidUnder，可重复）

通用参数：--unity <Unity 工程>  --repo <资源仓库>  --loc-script <组件类名>  --allowed-font <字体资产名>（可重复）  --id-field <多语言 id 字段>
不带参数时一律取项目事实（.uireskin/facts.json），不内置任何项目的组件名 / 字体名。
<TO> 写 WORKTREE 表示读资源仓库工作区当前文件。
局限：只认 prefab 文件里自己定义的 GameObject；嵌套实例在外层的覆盖值看不到，最终以 Unity 里的复核为准。

用法：python3 -I prefab_audit.py locfont <BASE> WORKTREE --appendix /tmp/a.md
"""
import argparse
import collections
import os
import re
import subprocess
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import uir_project as up  # noqa: E402

RES = None          # 资源仓库（main 里探测后赋值）
UNITY = None
LOC_GUID = None     # 本地化组件脚本 guid（可能为 None）

DOC_RE = re.compile(r'^--- !u!(\d+) &(-?\d+)[^\n]*\n', re.M)
GO_RE = re.compile(r'm_GameObject: \{fileID: (-?\d+)')
FONT_RE = re.compile(r'm_fontAsset: \{fileID: -?\d+, guid: ([0-9a-f]+)')
LOCID_RE = None   # main 里按项目事实 localization.idField 生成


def git(*args):
    return subprocess.run(['git', '-C', RES, '-c', 'core.quotepath=off', *args], capture_output=True, text=True).stdout


def changed_prefabs(base, to):
    rng = [base] if to == 'WORKTREE' else [base, to]
    return [p for p in git('diff', '--name-only', *rng, '--', '*.prefab').splitlines() if p]


def read(rev, path):
    if rev == 'WORKTREE':
        full = os.path.join(RES, path)
        return open(full, errors='ignore').read() if os.path.exists(full) else ''
    return git('show', f'{rev}:{path}')


def font_names():
    names = {}
    for full in up.walk_files([os.path.join(UNITY, "Assets"), RES], ".asset.meta"):
        f = os.path.basename(full)
        if "SDF" in f or "Font" in f:
            g = up.meta_guid(full)
            if g:
                names[g] = up.assets_rel(full[:-5].replace(os.sep, "/")) or os.path.basename(full[:-5])
    return names


class Prefab:
    """按 GameObject 汇总：名字、层级路径、TMP 字体 guid、本地化组件的 id。"""

    def __init__(self, text, loc_guid):
        parts = DOC_RE.split(text)
        docs = {parts[i + 1]: (parts[i], parts[i + 2]) for i in range(1, len(parts), 3)}
        self.name, tf_go, father, go_tf = {}, {}, {}, {}
        self.font, self.loc = {}, {}
        for fid, (cls, body) in docs.items():
            if cls == '1':
                m = re.search(r'm_Name: (.*)', body)
                self.name[fid] = m.group(1).strip() if m else '?'
            elif cls in ('4', '224'):
                g, f = GO_RE.search(body), re.search(r'm_Father: \{fileID: (-?\d+)', body)
                if g:
                    tf_go[fid] = g.group(1)
                    go_tf[g.group(1)] = fid
                if f:
                    father[fid] = f.group(1)
            elif cls == '114':
                g = GO_RE.search(body)
                if not g:
                    continue
                fm = FONT_RE.search(body)
                if fm:
                    self.font[g.group(1)] = fm.group(1)
                if loc_guid in body:
                    lm = LOCID_RE.search(body) if LOCID_RE else None
                    self.loc[g.group(1)] = int(lm.group(1)) if lm else 0
        self._tf_go, self._father, self._go_tf = tf_go, father, go_tf
        self.docs = {fid: body for fid, (cls, body) in docs.items()}

    def path(self, gid):
        out, tf, guard = [], self._go_tf.get(gid), 0
        while tf and tf in self._tf_go and guard < 64:
            out.append(self.name.get(self._tf_go[tf], '?'))
            tf, guard = self._father.get(tf), guard + 1
        out.reverse()
        return '/'.join(out[1:]) if len(out) > 1 else (out[0] if out else '?')


def short(p):
    return up.assets_rel(p) or p


def cmd_fonts(a):
    names, loc = font_names(), LOC_GUID or '-'
    total, per = collections.Counter(), {}
    for p in changed_prefabs(a.base, a.to):
        old, new = Prefab(read(a.base, p), loc), Prefab(read(a.to, p), loc)
        c = collections.Counter()
        for gid, g in new.font.items():
            if gid in old.font and old.font[gid] != g:
                c[(names.get(old.font[gid], old.font[gid]), names.get(g, g))] += 1
        if c:
            per[p] = c
            total.update(c)
    print('## 字体改动汇总（原字体 -> 新字体）')
    for (x, y), n in total.most_common():
        print(f'{n:5d}  {x}  ->  {y}')
    print('\n## 按 prefab')
    for p, c in per.items():
        flag = '  ⚠ Resources（热更前界面）' if '/Resources/' in p else ''
        print(f'{sum(c.values()):4d}  {short(p)}{flag}')


def cmd_locfont(a):
    if not a.loc_script or not a.allowed_font:
        print('项目事实未记载 localization.component / requiredFonts：先调研（uir_project.py info 列候选，读组件源码确认它要求的字体）再写入 .uireskin/facts.json；本项跳过')
        return 0
    if not LOC_GUID:
        print(f'工程里找不到 {a.loc_script}.cs（项目事实里的组件名不对？），跳过 locfont')
        return 0
    loc = LOC_GUID
    ok = set(up.find_asset_guids(UNITY, a.allowed_font, asset_repo=RES))
    if not ok:
        print(f'工程里找不到规范字体 {a.allowed_font}，跳过 locfont（用 --allowed-font 指定本项目的规范字体）')
        return 0
    names = font_names()
    rows = []
    for p in changed_prefabs(a.base, a.to):
        old, new = Prefab(read(a.base, p), loc), Prefab(read(a.to, p), loc)
        for gid, lid in new.loc.items():
            f = new.font.get(gid)
            if f is None or f in ok:
                continue
            if gid in old.font and old.font[gid] in ok:
                st = '改坏'
            elif gid in old.loc and gid in old.font:
                st = '原有'
            else:
                st = '新挂'
            rows.append((short(p), new.path(gid), lid, names.get(f, f), st))
    cnt = collections.Counter(r[4] for r in rows)
    print(f'挂 {a.loc_script} 却不是 {"/".join(a.allowed_font)} 的节点：{len(rows)}  ' + '  '.join(f'{k} {v}' for k, v in cnt.items()))
    byp = collections.defaultdict(list)
    for r in rows:
        byp[r[0]].append(r)
    for p, rs in byp.items():
        print(f'  {p}  ' + ' '.join(f'{k}{v}' for k, v in collections.Counter(r[4] for r in rs).items()))
    if a.appendix:
        lines = ['| prefab | 节点（多语言 id） | 状态 |', '|---|---|---|']
        for p, rs in byp.items():
            g = collections.defaultdict(list)
            for r in rs:
                g[r[4]].append(f'`{r[1]}`({r[2]})')
            for st, items in g.items():
                lines.append(f'| {p} | {"、".join(sorted(items))} | {st} {len(items)} |')
        with open(a.appendix, 'w', encoding='utf-8') as f:
            f.write('\n'.join(lines) + '\n')
        print(f'附录已写入 {a.appendix}')
    return 1 if cnt.get('改坏') or cnt.get('新挂') else 0


LEGACY_TEXT_GUID = '5f7201a12d95ffc409449d95f23cf332'   # com.unity.ugui Text.cs
GUID_RE = re.compile(r'guid: ([0-9a-f]{32})')


def all_guids():
    found = set()
    for p in up.walk_files(up.guid_roots(UNITY, RES), '.meta'):
        g = up.meta_guid(p)
        if g:
            found.add(g)
    return found


def node_issues(text, known, loc_guid, forbid_loc):
    p = Prefab(text, loc_guid)
    out = set()
    for gid, body in p.docs.items():
        go = GO_RE.search(body)
        where = p.path(go.group(1)) if go else '(文件级)'
        for g in GUID_RE.findall(body):
            if g not in known and not g.startswith('0000000000000000'):
                out.add(f'断引用 {g[:8]}…  {where}')
        if LEGACY_TEXT_GUID in body and 'm_Script' in body:
            out.add(f'旧版 UI.Text  {where}')
        if forbid_loc and loc_guid and loc_guid in body:
            out.add(f'不该出现的本地化组件  {where}')
    return out


def cmd_refs(a):
    known = all_guids()
    loc_guid = LOC_GUID
    total_new = 0
    for path in changed_prefabs(a.base, a.to):
        aq = any(x in path for x in (a.forbid_loc_under or []))
        new = node_issues(read(a.to, path), known, loc_guid, aq)
        old = node_issues(read(a.base, path), known, loc_guid, aq)
        fresh = sorted(new - old)
        if not fresh and not (new & old):
            continue
        print(f'{short(path)}  本轮新增 {len(fresh)}  原有 {len(new & old)}')
        for x in fresh:
            print('   ' + x)
        total_new += len(fresh)
    print(f'本轮新增问题合计 {total_new}')
    return 1 if total_new else 0


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest='cmd', required=True)
    for name in ('fonts', 'locfont', 'refs'):
        s = sub.add_parser(name)
        s.add_argument('base')
        s.add_argument('to')
        s.add_argument('--unity')
        s.add_argument('--repo')
        s.add_argument('--loc-script')
        s.add_argument('--allowed-font', action='append')
        s.add_argument('--id-field')
        if name == 'locfont':
            s.add_argument('--appendix')
        if name == 'refs':
            s.add_argument('--forbid-loc-under', action='append')
    a = ap.parse_args()
    global RES, UNITY, LOC_GUID, LOCID_RE
    UNITY = up.find_unity(override=a.unity)
    F = up.load_facts(UNITY)
    a.loc_script = a.loc_script or up.fact(F, 'localization.component')
    a.allowed_font = a.allowed_font or up.fact(F, 'localization.requiredFonts') or []
    id_field = a.id_field or up.fact(F, 'localization.idField')
    LOCID_RE = re.compile(re.escape(id_field) + r': (-?\d+)') if id_field else None
    if getattr(a, 'forbid_loc_under', None) is None and a.cmd == 'refs':
        a.forbid_loc_under = up.fact(F, 'localization.forbidUnder') or []
    RES = up.find_asset_repo(UNITY, [a.base, a.to], override=a.repo or (os.path.join(up.repo_root(UNITY), up.fact(F, 'unity.assetRepo')) if up.fact(F, 'unity.assetRepo') else None))
    LOC_GUID = up.find_script_guid(UNITY, a.loc_script, RES)[0] if a.loc_script else None
    print(f'[项目事实 {F.get("_path", "无")}]', file=sys.stderr)
    print(f'[Unity {UNITY}] [资源仓库 {RES}]', file=sys.stderr)
    sys.exit({'fonts': cmd_fonts, 'locfont': cmd_locfont, 'refs': cmd_refs}[a.cmd](a))


if __name__ == '__main__':
    main()
