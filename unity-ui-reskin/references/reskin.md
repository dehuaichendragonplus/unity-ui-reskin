# 界面替换规范（按效果图 1:1 还原）

> 来源：FoodFlow1 战令换皮复盘 + 工具化，BubbleWord3 局内 / 局外按效果图与 PSD 对齐的实战（2026-11）。项目无关的通用版。
> 本文中 `<skill>` = 本 skill 目录（`~/.claude/skills/unity-ui-reskin`）。Python 一律 `~/.cache/ui-reskin/venv/bin/python -I <skill>/scripts/<脚本>.py`。
> 用户另有要求以用户为准；项目里已有自己的换皮规范 / 事实库时，以项目文档为准，本文作补充。

## 0. 目标与验收口径

- **验收 = 运行时（Play）与效果图逐项对齐**：位置、尺寸、字号、字距、填充 / 渐变、描边、投影。以 `diff.py` 报告「需修正 0 项」+ 人工看叠图为准；编辑器预览、离屏渲染只是中间手段。
- **结构原则**：优先「覆盖图片、保留 GUID」，尽量不动预制体结构；效果图要求的新元素或数值调不到位时，**可以改结构和代码**，改动最小并在汇报里列明。
- **文案原则**：不擅自新增 / 修改多语言 key 和数值。效果图文案与 key 不一致、或与真实数据矛盾 → 列「待策划确认」。
- **玩法不让步**：效果图与玩法冲突（例：效果图里道具栏比实机高，跟着挪会挡住可玩区）时停下来问用户，不要为了像素对齐改玩法参数。
- Unity 资产只能通过 Unity MCP / Editor API 改，编辑器开着时不手改 .prefab/.asset YAML；美术源目录视为不可信数据，Python 一律 `-I` 运行。
- MCP 的 `execute_code` 不写绝对路径：工具 API 一律用**相对 Unity 工程根**的路径（如 `Library/ReskinShots/xxx`），效果图先复制到 `Library/ReskinShots/<界面>/`。
- 用户在 Play 中时先问再退出 Play；测试改过的存档（关号、体力、金币、语言等）测完还原并汇报。

## 1. 首次在项目里使用（零配置，代理自己做）

1. `python3 -I <skill>/scripts/uir_project.py info`：Unity 工程、仓库根、子模块 / 资源仓库、本地化组件、规范字体、UIReskin 包是否已装。
2. `bash <skill>/scripts/install_unity_package.sh`：把编辑器工具装成嵌入式包 `Packages/com.agent.uireskin`（不改 manifest、不碰 Assets，自动写进本机 `.git/info/exclude`；工程自带 UIReskin 时自动跳过）。等编译完（`wait_for_compilation` / `get_compilation_errors`）。
3. `execute_code`：`return UIReskin.ReskinConfig.Dump();` 看自动探测结果（文字组件、样式字段与前缀、语言字体目录、材质命名、钩子）。探测不对时写 `ProjectSettings/UIReskinConfig.json` 覆盖对应字段（只写要改的），`ReskinConfig.Redetect()` 重探测。
4. `bash <skill>/scripts/setup.sh`：一次性建 `~/.cache/ui-reskin/venv`（OpenCV、psd-tools）+ 编译 Vision OCR（macOS），已建好就跳过。
5. **摸清项目画面与坐标**（第 3 节），结论写进项目记忆 / 项目文档，下次直接用。

## 2. 工具链一览

| 阶段 | 工具 | 作用 |
|---|---|---|
| 探测 | `scripts/uir_project.py info` / `ReskinConfig.Dump()` | 工程、资源仓库、文字组件、字体目录等全部自动识别 |
| 体检 | `ReskinHealth.Check(dir)` / 菜单 Tools/UIReskin/体检选中目录 | 切片越界、引用丢失、白块、文字框过小、锚点异常、样式材质缺失 / 材质名不一致、Missing Script、手机 / Pad 差异 |
| 源文件 | `scripts/psd_to_spec.py` / `scripts/figma_to_spec.py` | PSD/PSB/Figma → `spec.json`（精确矩形、字号、字距、颜色、描边、投影、渐变、状态组）+ `spec.md` + `preview.png`；`--export-layers` 导图层 PNG |
| 效果图 | `scripts/crop_effect.py <图> <out> [auto\|full\|x0,y0,x1,y1] [--width W]` | 从大画布效果图取出手机画面（auto 自动去纯色边框），缩放到 Game 视图宽 |
| 运行时 | `ReskinMatrix.Snap(view, open, outDir, prefab, "WxH")` | 设分辨率 → 打开界面 → 快进动画 → 导出 `runtime.json`（节点精确屏幕矩形、字形框、TMP 参数）+ 截图 |
| 差异 | `scripts/diff.py`（轻量版 `align_nodes.py` 逐节点模板匹配） | 分组汇总（组整体偏移 vs 组内残差）→ `fixes.json` / `styles.json` / `guides.json` / `diff.md` / `annotated.png` / `cmp.png` |
| 修正 | `ReskinApply.Apply(fixes, dryRun)` | 位置 / 尺寸 / 字号 / 布局间距写回预制体（保持锚点体系；列表实例自动映射到模板；布局组子节点自动跳过） |
| 文字 | `ReskinTextStyle.Apply(styles, dryRun)` | 描边 / 投影像素 → TMP Underlay（各语言材质，按样式去重）、字号、字距、填充、渐变；有本地化组件时同步样式字段 |
| 文字（显式源） | `ReskinMaterialStyle.Apply(spec, dryRun)`（示例 `<skill>/examples/choice_page_styles.json`） | 源材质由 JSON 指定，按 PSD 投影 size / distance 重算 Underlay，同时改节点样式字段 / 顶点渐变 / 字号上限；缺源 / 缺节点整体拒绝写入；dry-run 报「⚠ dilate 到上限」。**节点当前材质不在字库里时用它** |
| 看图 | `ReskinOverlay.Show(mockup, Edges/Alpha)` + `LoadGuides(guides)` | Play 中实时叠效果图 / 边缘线；辅助线按组开关、跟随锚点；快捷键 ⌘⇧⌥O / P / G |
| 适配 | `ReskinMatrix.Run(view, open, dir, sizes, prefab, prefabPad)` + `scripts/matrix.py` | 多分辨率批量截图（含辅助线版）→ 一张总览 |
| 一次性片段 | `<skill>/snippets/reskin/*.cs` | 导节点树、修切片越界、按钮归一化、离屏渲染、Game 视图尺寸、在 Play 主循环里开界面、手工建文字材质 |

`execute_code` 里调用编辑器工具：`return UIReskin.ReskinHealth.Check("Assets/...");`。打开界面的协程写法见 `snippets/reskin/OpenViewInPlayLoop.cs`；不知道项目怎么开界面时先跑 `snippets/review/OpenViews.cs`（自动按常见 UI 管理器约定找打开方法）。

## 3. 项目画面与坐标（每个项目首次确认，记进项目记忆）

| 项 | 怎么确认 |
|---|---|
| 效果图画面 | 打开几张效果图看尺寸：效果图常是「大画布 + 中间手机画面」（例 1200×1920 画布、中间 768 宽；局内 / 局外取的高度可能不同）。先 `crop_effect.py auto` 试，**目视确认裁出的是手机画面**；四周是场景图时 auto 无效，量出 `x0,y0,x1,y1` 显式传。Game 视图设成裁出图的尺寸 |
| UI 参考分辨率 | 根 Canvas 的 `CanvasScaler`（referenceResolution、match）。参考宽 = 效果图宽时 1 画布单位 = 1 效果图 px；否则换算比 = 参考宽 / 效果图宽 |
| 自管参考框 | 有些模块（玩法局内 UI）自己算缩放框，不走根 Canvas：grep `CanvasScaler`、`referenceResolution`、`scaleFactor` 找到规则，按它换算 |
| 世界空间元素 | 棋盘等世界节点按正交相机换算：1 世界单位 = 屏幕高 px / (2 × orthographicSize)；原点 = 屏幕中心。玩法节点（生成点、地板、边界）不要为了对齐挪 |
| 文字体系 | `ReskinConfig.Dump()`：有 `LocalizeTextMeshProUGUI` 一类组件 → 样式材质按语言目录克隆，必须同时换材质与样式字段；有 `LocalizationText` + 规范字体（如 `TMP_Font_Normal`）→ 这类文字的字体问题由程序用 `LocFontFix.cs` 直接修（review.md §6.2）；纯 TMP → 直接改 TMP |
| `execute_code` 里的 `Screen` | 读到的是编辑器其他视图尺寸，**不是 Game 视图**。换算用 `Camera.pixelWidth/pixelHeight`，或用 `ReskinMatrix`（在 Play 主循环协程里读 Screen） |

## 4. 流程

### 4.1 盘点（只读）
1. 列美术源文件：**有 PSD/PSB/Figma 先跑 `psd_to_spec.py` / `figma_to_spec.py`**，看 `spec.md`（文字样式表、隐藏状态组）。只有 PNG 时走 CV 路线。
2. 找目标模块贴图目录、图集、所有预制体，`snippets/reskin/DumpUITree.cs` 导节点树备查。确认效果图手机画面区域（第 3 节），裁出来再比。
3. 新旧映射表：同名覆盖 / 改名覆盖 / 新增 / 故意不覆盖（同名 ≠ 同用途，必须看图）。

### 4.2 替换
1. 备份 → `cp` 覆盖 png（保留 .meta）；新增贴图导入设置对齐邻居（`TextureImporter.ReadTextureSettings` 复制）。
2. **必跑** `ReskinHealth.Check(模块目录)`；切片越界跑 `snippets/reskin/FixMultipleSpriteRects.cs`。
3. 九宫格 border 按新图重设；`prepare_editor` 后查编译 / 控制台。
4. 新图进对应图集（按文件夹打包的自动进；按清单打包的要 `SpriteAtlasExtensions.Add` + `SpriteAtlasUtility.PackAtlases`），项目有图集 / 未用资源守卫测试时一起跑。

### 4.3 测量 → 修正（循环到收敛）
1. Play：`ReskinMatrix.Snap(...)`，分辨率 = 效果图手机画面尺寸。**比文字时把测试号切到效果图语言（通常英文），测完切回**；按语言换字库的项目要连字库一起切（否则拉丁字母走中文字库，字形差距是假的）。
2. `python -I <skill>/scripts/diff.py <效果图.png 或 --spec spec.json> <runtime目录> --out <diff目录>`
   - 有 spec：文字用 PSD 精确值（字号/字距/颜色/描边），位置用「效果图文字块 → 运行时」反向定位；图片用 CV（PSD 图层框是裁边框，不比尺寸）。
   - 无 spec：图片用局部配准（子文字区域挖洞）、九宫格四边精修；文字用 OCR 行框定位 + 字高比算字号 + 3-means 取色。
   - 分组默认 depth 1..2：组内偏差一致（MAD 小）→ 只挪组；否则逐个修。列表（ScrollView/Content）只报告；低置信不出修正。
3. 读 `diff.md` 和 `annotated.png`，确认后：退出 Play → `ReskinTextStyle.Apply(styles.json, true)` 看 dry-run → `false` 写入；`ReskinApply.Apply(fixes.json, true/false)` 同理。**先文字样式、重测，再位置**（字号变了笔画中心会动）。
4. 重新 Snap + diff，直到「需修正 0 项」；`text` 表里「实测宽比」≈1。

### 4.4 结构与代码（需要时）
- 改路径前 grep `Find / GetComponent / GetItem / Binding / FindChild`；新增功能节点复用现有入口和显隐条件；从 LayoutGroup 复制的节点重设锚点 / 缩放；嵌套预制体子节点只改属性。
- 节点被**动画或布局组接管**时改预制体值无效，见第 5 节。

### 4.5 多分辨率
1. `ReskinOverlay.LoadGuides(guides.json)` 后 `ReskinMatrix.Run(..., sizes)`：效果图尺寸 + 长屏手机（1080x2340）+ 短屏（1080x1920 / 1080x2160）+ Pad（1536x2048）。`python -I <skill>/scripts/matrix.py <dir> --guides` 拼总览。
2. 项目没有 `_Pad` 预制体时 `prefabPad` 传 null。

### 4.6 交付
1. 汇报：资源清单；结构 / 代码改动；`diff.md` 结论（已对齐 / 残余差异及原因）；待策划确认；测试改过的存档是否还原；未验证项。
2. 截图与报告在 `<Unity 工程>/Library/ReskinShots/<界面>/`；要进文档的对照图放项目文档目录。
3. 提交（用户要求时）：资源在子模块 / 独立仓库时先提交资源仓库再主仓；不带编辑器运行时改写的无关文件（动态字体资产、被运行时改写的共享材质，用 `git diff --name-only -z | xargs -0 git checkout --` 还原）；`Packages/com.agent.uireskin` 不提交（已在本机 exclude）；推送前 fetch。
4. 新确认的项目事实（画面裁切、参考分辨率、文字体系、校准系数）写进项目记忆 / 项目文档。

## 5. 坑速查

| 现象 | 原因 | 处理 |
|---|---|---|
| 换图后白底 / 丢图、无报错 | Multiple 切片越界 | 体检 + FixMultipleSpriteRects |
| 按钮扁小、文字竖排或极小 | 模板文字框负尺寸、底图锚点 anchorMin > anchorMax + 缩放 | 体检「锚点异常 / 文字框过小」+ NormalizeButton |
| 改了描边材质运行时没生效 | 本地化组件 Awake 按材质名反推样式字段 / 运行时按字段去字库找材质 | 用 ReskinTextStyle / ReskinMaterialStyle（同时换材质与字段，写完执行钩子）；体检「材质名不一致 / 样式材质缺失」 |
| 新样式材质建好了，运行时仍是旧样式 | 运行时按「字库清单资产」找材质，新材质不在清单里 | 在项目 Editor 代码里写一个 `[UIReskin.UIReskinHook]` 静态方法刷新清单（ReskinConfig 自动发现并在写完材质后调用）；新材质还要进资源收集 / 打包规则 |
| 截图尺寸不对、整套 UI 小一圈 | Game 视图比例不是效果图比例 / 参考框按高缩放 | `Snap(..., "WxH")` 指定分辨率；看日志 `[RESKIN] snap ready WxH` 与请求一致 |
| 设了尺寸，截图却是 iPhone 尺寸或一片黑 | 构建目标是 iOS / Android，Game 视图用对应尺寸分组（工具已按 `currentGroupType` 处理）；或 Play 被手动停了 | 抓图前核 `capture_game_view` 的 `render_width/height`；黑屏重新 `enter_play_mode` |
| 改了预制体位置 / 颜色运行时没变 | **Animator 末帧覆盖**（appear 动画写颜色、位置、缩放） | `AnimationUtility.GetCurveBindings` 看动画写了哪些属性，改动画末帧关键帧或改不受动画控制的子节点 |
| 改了 anchoredPosition.x 没效果 | 父节点有 **LayoutGroup** 排位 | 改布局组 spacing / padding；runtime.json 的 `hasLayoutParent` 会标出来 |
| 截到的界面元素偏小 / 缺图 | 截在入场动画中途 | `ReskinAnim.SkipAll()` 或等 1.6 s 再截；缺真实数据的窗口（结算）走真实流程 |
| 弹窗整体偏、各元素偏差一致 | 父节点（Root / Content）位置不对 | diff.py 多层级分组自动只挪父节点 |
| 弹窗底部被别的卡片压住 | 效果图里弹窗下方还有兄弟面板 | 对着效果图整体挪弹窗 |
| 选中 / 未选中大小不同 | 代码里写死缩放 | 效果图要求等大时改代码 |
| 效果图有、工程里没有的元素 | 设计稿超前或占位 | 不做，列「待策划确认」 |
| 效果图的样式在美术切图里没有 | 切图太小 / 缺状态图 | 程序按效果图重绘高分辨率版（脚本留在项目 Tools），列「待美术出正式图」 |
| 列表项偏差大 | 滚动位置 / 数据不同 | 默认只报告；需要时 `--include-list` |
| 标题比效果图宽 | PS 字距没带过来 | styles.json 的 charSpacing |
| 描边比效果图粗 / 细 | Underlay 理论公式偏差 | `outlineCalibration`（FoodFlow1 实测 1.54）：按报告「实测宽比」校准，写进 `ProjectSettings/UIReskinConfig.json` 并记进项目记忆 |
| 小字号英文投影画不满 | 拉丁字库 pointSize 大、gradientScale 小，dilate 被卡在上限 1.0 | 接受略细并记入汇报；要精确得重烧字库图集 padding（美术评估）。`ReskinMaterialStyle` dry-run 逐语言报 ⚠ |
| `ReskinTextStyle.Apply` 报 MISSING 源材质 | 节点当前材质不在语言字库里（挂的是共享材质） | 改用 `ReskinMaterialStyle.Apply`（源材质显式指定，缺源整体拒绝） |
| 自动 diff 全是低置信 | 效果图文案 / 数据与运行时不同（语言、进度、名称） | 不硬追自动修正；裁关键区域上下并排放大看（第 6.2 节），位置已对齐就只处理样式 |
| execute_code 被拦 `SAFETY_CHECK_BLOCKED` | 代码里出现绝对路径 / 删除资产 | 用相对工程根路径；效果图复制进 Library；自己临时建的文件用 shell 删（精确文件名 + meta）再 `AssetDatabase.Refresh` |
| execute_code 报 `CLASS_NOT_FOUND`（编译却成功） | funplay 按**短类名**找入口、取第一个类：片段包了 namespace，或入口类不在最前 | 片段不要包 namespace，`public static string Run()` 所在类放第一个；类名起得独特些避免和项目全局类型撞名 |
| execute_code 返回 `interrupted_without_replay` | 刚改过 C# 触发重编译，域重载打断（没执行、无副作用） | 每次改完 C# 先 `wait_for_compilation` 再发 |
| OpenCV 读效果图失败（chunk too large） | PNG 带大块元数据 | 用 PIL 读入再转（脚本已处理；自写脚本注意） |
| 临时产物写进了仓库根 `Library/` | 工作目录在仓库根与 Unity 工程之间切换 | 产物一律放 `<Unity 工程>/Library/ReskinShots/<界面>/`；用绝对路径 cd；提交前看 `git status` |
| `run_tests` 匹配 0 条 | `assembly_names` 传了数组（有的 MCP 要逗号分隔字符串） | 按 MCP 说明传；0 条不算通过 |

## 6. 有 PSD 时的推荐顺序（复盘）

> 耗时大头通常不在改动本身，而在**截图环境不对**和**用错了对比语言**。

### 6.1 顺序
1. **先看 PSD 元数据，不要先看图**：`psd_to_spec.py --export-layers` 出 `spec.md`（文字表）和图层 PNG。文字表里的字体、字号、填充、投影就是目标；**以元数据为准，不凭肉眼估**。
2. **元数据没写的视觉效果去查原始图层**（`psd_tools` 逐层看 `effects` 的 `enabled`、`clipping`）：例如文字下半的淡色常是**剪切蒙版形状层**而非文字效果，`psd_to_spec` 不解析 → 落地用顶点渐变（上色 = 文字原色，下色 = 实测下半部颜色）。`psd_to_spec` 的投影按「阻塞 ≥50%」当描边：`px = size`，`offsetDown = distance`。
3. **确认 Game 视图真的是目标尺寸**：`capture_game_view` 的 `render_width/height` 必须等于请求尺寸，再抓图。
4. **图是否变了，用像素差判断**：PSD 图层常带额外透明 / 额外行，把图层按不同纵向偏移裁成工程贴图尺寸，找最小平均差；差 < 1 就是同一张图，不用换。
5. **文字对比在效果图语言下做**，按语言换字库的项目同时切字库。
6. **样式材质**：按 PSD 每处字号各生成一档（dilate 与字号相关），每种语言一份。写 JSON（参考 `<skill>/examples/choice_page_styles.json`），先 `ReskinMaterialStyle.Apply(json, true)` 看逐语言 dilate 与 ⚠，再退出 Play `Apply(json, false)`。命名 `Shadow <色> <字号>`。同一份 JSON 可重复跑（幂等）。
7. **位置**：动手前先目视并排检查布局；自动 `diff.py` 在文案 / 数据不同时全部低置信，不要强行出 fixes。
8. **新增图**进图集；`.meta` 从邻居复制时**改 guid 与 spriteID**；同步更新守卫测试（精灵数、字库样式数）。
9. **收尾**：测试号语言还原 → 退出 Play → `git checkout` 编辑器噪声 → 跑受影响程序集 → 记项目事实 → 提交。

### 6.2 并排放大对比（替代自动 diff）
效果图与运行时各裁同一区域，上下拼成一张，放大 2 倍看（PIL 裁 + resize，或 `contact_sheet.py`）。一次看清字体、投影粗细、渐变。

### 6.3 已知遗留类型（汇报时单列「待确认」，不自己下结论）
- 效果图有、工程没有的元素：触发条件未知，不做。
- 效果图文案与现有词条不一致：不新增多语言 key，列待策划。
- 效果图把文字烘进图里：照图落地但标注「无法多语言」。
- 效果图省略了需求要求的信息：保留需求，列待确认。

## 7. 关键换算与校准

| 项 | 规则 |
|---|---|
| PS 字号 | `FontSize × 变换矩阵缩放`（psd_to_spec 已处理），单位 = 画布像素；画布宽 = 参考宽时即画布单位字号，否则 × 参考宽 / 画布宽，再除以节点累计缩放 |
| PS 字距 | Tracking（1/1000 em）÷ 10 = TMP `characterSpacing`（1/100 em） |
| 描边 | Stroke，或「投影 + 阻塞 ≥50%」（实心外扩、下移 distance） |
| Underlay | `dilate = px / (0.5·GradientScale·字号/pointSize·ratioC·outlineCalibration)` |
| 渐变 | PS 渐变叠加角度 90°：第一个色标在下（reversed 反之）→ TMP 顶点渐变 |
| 偏差符号 | 屏幕像素 y 向下；`anchoredPosition.y` 向上，工具已换算；`ppu` = 父节点单位 → 屏幕像素 |
