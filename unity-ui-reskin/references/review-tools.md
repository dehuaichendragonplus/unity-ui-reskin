# 审查工具说明

配合 `references/review.md` 使用。工具自动找 Unity 工程，以及包含 BASE 提交的仓库（主仓 / 子模块 / Assets 下软链进来的资源仓库）；需要时用 `--unity` / `--repo` 覆盖。
因项目而异的名字（本地化组件、规范字体、界面基类、绑定特性……）一律读项目事实 `.uireskin/facts.json`；事实缺项时工具只给候选并提示先调研，不按别的项目的名字猜。

## git 侧（不需要 Unity，`python3 -I <skill>/scripts/...`）

| 工具 | 用途 |
|---|---|
| `uir_project.py info` | Unity 工程、仓库根、子模块、候选资源仓库、UIReskin 包、项目事实状态，以及调研候选（本地化组件类、TMP 字体引用排行、CanvasScaler 参考分辨率） |
| `uir_project.py facts init / show` | 建项目事实模板 / 看已有事实与空字段 |
| `uir_project.py asset-repo <REV>` | 哪个仓库包含该提交 / tag |
| `export_snapshot.py <BASE> <TO> <DEST> [SNAPSHOT_REV]` | `<BASE>..<TO>` 改过的 prefab 按 `SNAPSHOT_REV`（默认 BASE）内容导到 `Assets/<DEST>/<Assets 下相对路径>`。`DEST` 必须以 `_Tmp` 开头，用完删除 |
| `prefab_audit.py fonts <BASE> <TO>` | 字体改动汇总（原字体 → 新字体），按 prefab 列出，并标出 `Resources` 下的热更前界面 |
| `prefab_audit.py locfont <BASE> <TO> [--appendix f.md]` | 挂本地化组件（项目事实 `localization.component`，`--loc-script` 覆盖）却不是规范字体（`localization.requiredFonts`，`--allowed-font` 覆盖）的节点（本轮改坏 / 新挂 / 原有）；有本轮问题退出码 1；项目没有该组件或字体时跳过 |
| `prefab_audit.py refs <BASE> <TO> [--forbid-loc-under 片段]` | 新出现的断引用（工程里不存在的 guid）、旧版 `UI.Text`；`--forbid-loc-under` 另报指定模块里的本地化组件。有本轮问题退出码 1 |

`<TO>` 写 `WORKTREE` 表示读资源仓库工作区。

## Unity 侧（`<skill>/snippets/review/*.cs`，MCP `execute_code` 整段粘贴运行）

| 文件 | 何时用 | 是否只读 |
|---|---|---|
| `BindAudit.cs` | 第 3 节第 1 项：绑定路径与组件新旧对比（框架信息取项目事实 `framework`，缺项时返回要调研的字段） | 只读 |
| `HierDiff.cs` | 第 3 节第 3、4、7、8 项：节点、组件、显隐、引用变化 | 只读 |
| `LocCompDiff.cs` | 第 3 节第 5 项：多语言组件增减与 id / key / 样式字段变化 | 只读 |
| `LocFontReport.cs` | 第 6.2 节：字体问题全貌 / 修复后复核（项目事实 `localization`） | 只读 |
| `LocFontFix.cs` | 第 6.2 节：直接修字体（→ 规范字体，材质按 `materialMap` 映射，删误挂组件）；`DRY` 预演后写入，返回修改记录 | 写 prefab |
| `InteractAudit.cs` | 第 3 节第 10 项：BLOCKED / MASKOFF / OVERSIZE；有旧版快照时标「原有 / 已不存在」 | 只读（临时画布用完即销毁） |
| `FxCompare.cs` | 第 6.3 节：美术版 vs 当前的效果类型对比 | 只读 |
| `RuntimeLocPlaceholder.cs` | Play 中：激活的本地化组件占位写回检查（事实缺项时列场景里的候选组件） | 只读 |
| `OpenViews.cs` | Play 中：按类名自动找 UI 管理器的打开 / 关闭方法；找不到列候选 | 只影响运行时 |
| `FxBoard.cs` | Play 中：左右对照板比较描边 / 阴影（模板，路径换成本项目的） | 只影响运行时 |

- 片段都是 `public static string Run()` 形式，常量（临时目录、过滤条件、映射表）写在文件顶部。
- 编辑器里只读的片段可加 `skip_refresh=true`；Play 中一律加，避免触发刷新。
- MCP 安全检查常拦 `File.Write*` 和绝对路径字面量，所以结果都用返回值输出。
- 快照目录约定：`Assets/_TmpUIAuditOld/<Assets 下相对路径>` 对应当前 `Assets/<相对路径>`（与 `export_snapshot.py` 一致）。
- 片段要能单独粘贴运行，所以各自带一小段读事实的代码（从 Unity 工程往上找 `.uireskin/facts.json`，环境变量 `UIRESKIN_FACTS` 可指定）。注意 funplay 片段编译在内存程序集里，`JsonUtility` 只认顶层字段：片段先截出 `framework` / `localization` 这类段再解析成扁平类，对象数组（`materialMap`）用正则取。
