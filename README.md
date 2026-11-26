# unity-ui-reskin（代理端界面替换 / 换皮审查工具包）

把 BubbleWord3（源自 FoodFlow1）沉淀的「按效果图 1:1 还原界面」与「美术换皮审查」两套 skill + 工具，再加上「按 PSD 新建界面到功能可用」，做成 **Claude Code 用户级 skill**：装一次，任何 Unity 项目直接可用，**不需要预先配置**：项目差异现场调研，结论存进项目仓库的 `.uireskin/`，下次直接用。

## 安装

```bash
bash install.sh            # 装到 ~/.claude/skills/unity-ui-reskin
bash install.sh --python   # 可选：同时建 Python 图像环境（首次用到时代理也会自己建）
```

装完新开一个 Claude Code 会话，直接说需求即可触发，例如：
- 「美术给了新切图和 PSD，按效果图把商店界面还原一下」
- 「按这份 PSD 新做一个签到界面，接好功能」（生成骨架与控件 → 照项目框架写代码 → 配图集与资源 → 打开验证）
- 「美术在 v1.2.0 之后换皮了，审查并直接修好」（默认程序直接改 prefab、字体、代码，测试后提交，最后出修改记录告知美术）

## 项目差异怎么处理：不内置框架，现场调研，事实存仓库

不同项目的 UI 框架、本地化组件、字体规范、资源管理都不一样，本包**不写死任何项目 / 框架的名字**。做法是：

1. **读项目事实**：仓库根 `.uireskin/facts.json`（工具读）+ `.uireskin/facts.md`（人看：结论、证据、待核实、变更记录）。用到的字段有值就直接用。
2. **缺什么调研什么**：工具只给候选、不下结论——`uir_project.py info`（本地化组件类、TMP 字体引用排行、CanvasScaler 参考分辨率）、`ReskinConfig.Dump()`（文字 / 字体，逐字段标「事实 / 探测，待核实」）、`ReskinSurvey.Run()`（界面基类、绑定特性、打开 / 关闭接口）、`ReskinAssets.Survey()`（资源管理与图集）。代理读源码、实测核实。
3. **写回仓库**：核实的结论写进 facts.json，证据写进 facts.md，随本次工作一起提交；下次（换人、换会话）直接读，不再重新摸。
4. **缺事实时不瞎猜**：审查片段、prefab_audit 等读不到事实会明确提示「先调研哪个字段」，不会套用别的项目的组件名 / 字体名。

| facts.json 段 | 内容 | 谁在用 |
|---|---|---|
| `unity` | Unity 工程目录、资源仓库 / 子模块 | 审查脚本 |
| `ui` | UI 参考分辨率、效果图手机画面裁切框（按名字记）、新 prefab / 切图目录 | crop_effect、align_nodes、spec_to_layout、生成器、InteractAudit |
| `text` | 本地化文字组件与样式字段、字体目录与命名模板、默认字体、描边校准、写材质后的钩子 | ReskinConfig → 体检 / 文字样式 / 生成器 |
| `localization` | 静态多语言组件、id / key 字段、规范字体、标题字体、材质映射、不该挂的模块 | prefab_audit、LocFontReport / Fix、LocCompDiff、RuntimeLocPlaceholder |
| `framework` | 界面基类、绑定特性及路径 / 可空 / 组件成员、注册特性及 prefab 键、打开 / 关闭、参照界面 | BindAudit、新建界面写业务代码 |

示例见 `unity-ui-reskin/examples/facts-example/`（虚构项目）。

纯结构性的事仍然全自动、不需要事实：

| 项目差异 | 自动处理方式 |
|---|---|
| Unity 工程在哪 | 从当前目录往上找；仓库根下 2 层内找（排除子模块里的美术工程） |
| 资源在主仓 / 子模块 / 软链进来的独立仓库 | 按用户给的 tag / 提交，在主仓、所有子模块、Assets 下软链指向的仓库里找能解析它的那个 |
| 编辑器工具（C#） | 代理首次需要时运行 `install_unity_package.sh`，装成嵌入式包 `Packages/com.agent.uireskin`：不改 `manifest.json`、不碰 `Assets/`，自动写进本机 `.git/info/exclude` 不会被误提交；工程自带 UIReskin 时自动跳过；asmdef 有版本宏保护，缺 TMP / 2D Sprite 包时安全跳过而不是让工程报错 |
| 写材质后要刷新的字库清单 | 项目里给一个静态方法标 `[UIReskin.UIReskinHook]` 即自动调用，或写进事实 `text.afterMaterialsHook` |
| 效果图手机画面 | `crop_effect.py auto` 自动去纯色边框；整屏设计时提示代理目视后传坐标，确认后记进事实 `ui.effectFrames` |

## 新建界面能自动做到哪

| 环节 | 自动程度 | 说明 |
|---|---|---|
| PSD → 骨架 prefab | 全自动 | 层级、RectTransform、锚点（第一层按画面位置推断）、图片、文字（字号 / 颜色 / 字距 / 渐变）、隐藏状态组 |
| UGUI 控件 | 全自动 + 复核 | 按名字和结构推断：按钮（底图 + 文字）、关闭键（面板右上角小方块）、进度条（底 + 填充长条，转 Slider）、开关、滚动列表（重复结构 → ScrollRect + LayoutGroup）；形状推断的会标出来请代理复核 |
| 按钮功能 | 推断建议 | 按按钮文字推断（Free → 看广告、价格 → 购买、Claim → 领取、Activate → 激活……），写进 `bindings.json` |
| 切图 | 全自动 | 工程同名 Sprite 优先；否则导入图层 PNG（同内容去重、导入设置照抄邻居） |
| 图集 / 资源管理 | 全自动（预演后写入） | 图集按工程现有设置新建或补图；Addressables / YooAsset 照抄最近的组 / 收集器登记；Resources 直接可用 |
| 业务代码 | 代理照项目写 | `ReskinSurvey` 普查出项目的界面基类、绑定方式、打开接口；代理照最相近的界面写，推断不出的数据来源 / 配置 id / 价格列「待确认」，不编造 |
| 对齐与验证 | 全自动测量 | 生成后直接走还原流程：运行时测量 → 差异 → 自动修正；真实点击验证按钮 |

实测（2026-11-26，Unity 6000.3 全新工程，无任何配置）：战令界面 PSD（278 个图层）→ 108 个节点的 prefab，1.1 秒生成，导入 57 张切图（10 张内容相同自动复用）、6 个按钮、1 个进度条；默认图层名的结算界面按结构识别出 Free / 900 两个按钮；合成列表界面生成的 ScrollRect 间距、边距与 PSD 一致，关闭键 / 进度条 / 开关实例化后位置与 PSD 坐标逐像素一致。BubbleWord3 上 `ReskinSurvey` 找出 `DragonCD3.UI.View`（95 个界面）、`[Binding("Root/...")]` 绑定、`UISystem.ShowView<T>` 打开接口。

## 用到的技术

| 层 | 技术 | 用在哪 |
|---|---|---|
| 代理编排 | Claude Code Skill（`SKILL.md` 入口 + `references/` 按需加载） | 按任务只读需要的规范，流程、铁律、坑速查都由代理执行，不靠人记 |
| 驱动 Unity | Unity MCP（funplay：`execute_code`、`capture_game_view`、`simulate_mouse_click`、`run_tests`） | 在编辑器里跑工具、截图、真实点击验证遮挡、跑守卫测试 |
| 调研候选与项目事实 | Unity `TypeCache` + 反射；AssetDatabase 扫描 TMP 字体资产并按「目录名 ∈ 资产名」分组；prefab YAML 抽样；git 子模块 / 软链仓库解析 | 自动识别本地化组件与样式字段、语言字体目录与材质命名、资源仓库，结果缓存在 `Library/` |
| 编辑器工具分发 | 嵌入式 UPM 包 + asmdef `versionDefines` / `defineConstraints` + `.git/info/exclude` | 不改 manifest、不碰 Assets、缺 TMP / 2D Sprite 时安全跳过、不会误提交 |
| 运行时测量 | `RectTransformUtility` 世界坐标 → 屏幕像素、TMP `textBounds` 字形框、`GameViewSizes` 反射设分辨率、`ScreenCapture` | 导出每个节点的精确屏幕矩形与文字参数（`runtime.json`），运行时一侧零误差 |
| 动画快进 | DOTween（反射）、Animator、Legacy Animation、PlayableDirector 推到末帧 | 截图 / 测量前跳过入场动画，避免截到中间态 |
| 效果图测量 | OpenCV 多尺度模板匹配（`TM_CCOEFF_NORMED`）、局部配准 + 九宫格四边精修、numpy、Pillow | 从 PNG 效果图量出元素框；自动裁出手机画面 |
| 文字识别 | macOS Vision 框架 OCR（Swift 编译）+ 字高比估字号 + 3-means 取色 | 没有源文件时识别文字位置、字号、颜色、描边色 |
| 源文件解析 | psd-tools（PSD / PSB 图层、文字、图层样式）、Figma 节点 JSON | 直接读出精确的字号、字距、颜色、描边、投影、渐变、隐藏状态组 |
| 差异与修正 | 按父节点分组的稳健统计（中位数 / MAD：组内一致只挪组）、字号阻尼收敛、锚点体系内换算 | 生成 `fixes.json` / `styles.json`，写回时保持原锚点，跨分辨率不坏 |
| 文字样式 | TMP SDF Underlay 换算（`dilate = px / (0.5·GradientScale·字号/pointSize·ratioC·校准)`，`ScaleRatioC` 迭代收敛）、每种语言克隆材质 | 把 PSD 描边 / 投影像素精确落到 TMP 材质，校准后实测宽比 0.99–1.04 |
| 写回 prefab | `PrefabUtility.LoadPrefabContents` / `SaveAsPrefabAsset`、`SerializedObject`、SpriteAtlas API、2D Sprite 数据提供器 | 只改数值不改结构；体检切片越界、引用丢失等「无报错坑」 |
| 编辑器叠图 | `EditorApplication.update` 驱动的叠加画布（Alpha / 边缘线）+ 跟随锚点的辅助线 | Play 中实时对齐，换分辨率辅助线跟着锚点走 |
| 换皮审查 | git 区间 diff + `git show` 导出旧版快照、prefab YAML 解析（fileID / guid）、全工程 guid 索引 | 不开 Unity 就能报出断引用、旧版 Text、字体改动、规范字体违规，只报本轮新引入的 |
| 新建界面 | 图层树 → 节点树、按名字 + 几何结构推断控件（底图包文字、右上角小方块、共左边长条、重复子结构）、UGUI 控件组装（Slider / Toggle / ScrollRect + RectMask2D + LayoutGroup）、文件内容哈希去重 | 从 PSD 直接生成可交互的骨架 prefab 与控件 |
| 资源接线 | SpriteAtlas V1 / V2（`SpriteAtlasAsset`、`SpriteAtlasImporter`）、Addressables / YooAsset 反射调用、TextureImporter 设置照抄 | 不依赖这些包也能编译；按工程现有约定登记 |
| 框架普查 | 反射扫描项目程序集：界面基类分布、字段 / 类特性、带泛型约束的打开 / 关闭方法排序 | 给代理写业务代码当依据，照项目已有写法而不是通用模板 |

## 效率对比（与传统手工方式）

数据来源：**FoodFlow1 战令换皮**（传统做法全程记录 + 工具落地后同一界面复测）、**BubbleWord3** 2026-11 换皮审查（117 个 prefab 区间上实测脚本耗时）。表中「估算」是按经验单价推的，没有实测，单独标出。

### 按效果图还原界面

| 环节 | 传统方式 | 用本工具 | 提升 | 依据 |
|---|---|---|---|---|
| 整个界面 | 约 2 小时；102 次 execute_code、43 次截图、81 次读文件（大部分在看图） | 没做整界面计时；按下面各环节，单轮秒级、两三轮收敛，剩下的主要是人工确认与提交 | — | 传统侧战令实测；工具侧未整界面计时 |
| 每轮「改 → 重开界面 → 等动画 → 截图 → 叠图 → 量偏差」 | 每轮几分钟，一个界面十几轮 | 差异分析约 7 秒 / 界面（有 OCR 缓存；首次 OCR 10–60 秒），动画自动快进 | 单轮从分钟级降到秒级 | 战令实测 |
| 收敛轮数 | 十几轮人眼叠图 | 购买弹窗 2 轮收敛：整体偏差 −14px → −0.2px，写回 18 项 | 约 5 倍以上 | 战令实测 |
| 无报错坑排查（切片越界、负尺寸文字框、引用丢失） | 只能靠截图偶然发现 | 体检 12 个预制体约 1 秒一次报全 | 从「靠运气」到秒级全量 | 战令实测 |
| 多分辨率检查 | 手动逐个切分辨率截图 | 5 种分辨率全自动约 1 分钟，带辅助线 + 总览图 | — | 战令实测 |
| 文字样式（有 PSD / PSB） | 字号 OCR 估算误差 ±5–10%；描边色目测色差 10–50；字距测不出；要多轮试 | 解析约 2 秒，字号 / 字距 / 描边色 / 宽度 / 偏移 / 渐变全部精确，一次写对 | 精度从「估算」到「精确」 | 战令实测（278 个图层 / 40 个文字图层） |
| 人眼漏掉的偏差 | 叠图后仍残留 8–17px 纵向偏差未被发现 | 自动定位逐项报出 | — | 战令实测 |

### 换皮审查与修复

| 环节 | 传统方式 | 用本工具 | 依据 |
|---|---|---|---|
| 建立换皮前参照 | 手动 checkout 旧版、另开工程对照 | `export_snapshot.py` 117 个 prefab 约 2 秒 | BubbleWord3 实测 |
| 断引用 / 旧版 Text 排查 | 逐个打开 prefab 看 Missing | `prefab_audit refs` 117 个 prefab 约 14 秒，报出本轮新增 21 处（另一区间） | BubbleWord3 实测 |
| 字体改动 / 规范字体违规统计 | 逐节点看 TMP 字体 | `fonts` / `locfont` 各约 5 秒，报出 204 个违规节点并分「本轮改坏 / 新挂 / 原有」 | BubbleWord3 实测 |
| 整轮静态核对 117 个 prefab | 在 Unity 里逐个打开对照，按每个 2–5 分钟约 4–10 小时 | git 侧全量约 30 秒，再加 Unity 侧片段（绑定、层级、多语言、可点击）各一次 | 传统侧为**估算** |
| 修 204 个字体节点 | 逐个改字体 + 挑材质，按每个约 1 分钟约 3 小时以上，且曾因退回旧材质丢了 109 / 192 个节点的描边阴影 | `LocFontFix.cs` 预演后批量写入，材质按美术版映射到预设 | 传统侧为**估算**；批量修复片段已编译通过，尚未在真实 prefab 上跑过 |
| 新项目接入 | 照搬工具要改硬编码路径、组件名、字体目录 | 不改代码：缺的事实现场调研（候选探测一次约 7 秒，BubbleWord3 编辑器实测） | 实测 |

### 结论

- **最大的提升在「测量」**：运行时一侧直接读精确值，效果图一侧有源文件时直接读数值，没有时用 CV + OCR 自动测；人眼叠图从「主力」变成「最后确认」。单轮迭代从几分钟降到几秒，收敛从十几轮降到两三轮。
- **文字样式从估算变成精确**：有 PSD / PSB 时字号、字距、描边一次写对；描边校准后实测宽比 0.99–1.04。
- **审查从小时级降到分钟级**：git 侧 117 个 prefab 的全量统计约 30 秒，只报本轮新引入的问题，修复也改为批量预演后写入。
- **代价**：首次在机器上要建一次 Python 环境；PNG 效果图四周是场景图时要人工给一次手机画面坐标；效果图文案和数据与运行时不同时，自动差异只能作参考，仍要人眼并排看。

## 依赖

- Claude Code + 一个能 `execute_code` 的 Unity MCP（如 funplay），用于驱动编辑器、截图、真实点击、跑测试。
- Unity 2022.3 / 6000.x（已用 2022.3.62 与 6000.3.13 的编译器离线编译通过）。
- macOS（OCR 用 Vision 框架；其余 Python 工具跨平台）。Python 3 + `setup.sh` 自动装的 OpenCV / psd-tools / Pillow / numpy。

## 目录

```
unity-ui-reskin/                 ← 整个目录就是 skill
  SKILL.md                       入口：识别项目 → 按任务读规范 → 铁律 / 易错点
  references/
    new-ui.md                    新建界面全流程（PSD → 骨架 / 控件 → 业务代码 → 图集 / 资源 → 验证）
    reskin.md                    界面替换规范（流程、工具链、项目画面与坐标、坑速查、PSD 复盘、换算）
    review.md                    换皮审查规范（原则、静态 19 项清单、运行时审查、文档结构、修复、提交、收尾）
    review-tools.md              审查工具说明
    art-delivery.md              给美术的效果图交付规范
    research.md                  工具选型调研（FoodFlow1）
  scripts/                       Python / Shell（uir_project.py 工程探测 / 调研候选 / 项目事实、spec_to_layout.py 骨架 + 控件推断、install_unity_package.sh、crop_effect、psd_to_spec、diff、align_nodes、export_snapshot、prefab_audit ……）
  unity-package/com.agent.uireskin/   编辑器工具（命名空间 UIReskin，菜单 Tools/UIReskin）
  snippets/reskin|review/        execute_code 一次性片段
  examples/                      样式 JSON 示例；FoodFlow1 开界面写法（项目特化参考）
```
