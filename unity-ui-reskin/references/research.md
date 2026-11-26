# 界面替换提效方案调研

> 背景：本文是 FoodFlow1 战令换皮时的选型调研（数据、尺寸均为该项目实测），保留作工具设计依据；具体做法以 references/reskin.md 为准。

> 2026-11-13，基于战令换皮复盘与开源方案调研。目标：替换后的界面里，所有文字（字号、描边/阴影、颜色、位置）和图片（位置、大小）都和效果图对齐，并且能适配不同分辨率；同时尽量把「人眼对图」变成「机器测量 + 自动修正」。

## 一句话结论

慢的根本原因是**美术只交付扁平 PNG**：图层结构、坐标、字号、描边参数全丢了，只能靠人眼和反复截图把它们反推回来。

提效分两条路：
- **治本**：让美术交付带图层的源文件（PSD 或 Figma），用解析器直接读出数值，再写到现有预制体上。
- **治标**：没有源文件时，用 CV 和 OCR 从效果图 PNG 里自动测出元素框与文字样式；运行时这一侧直接从 Unity 读精确矩形。两边做差得到差异表，再由脚本自动修正。

另外，把「改 → 截图 → 叠图」这个循环做成编辑器内的实时叠加，并加上多分辨率批量截图。

---

## 1. 痛点盘点（战令这次的实际数据）

战令换皮约 2 小时，调用了 102 次 `execute_code`、43 次截图、81 次读文件（大部分是看图）。时间主要花在下面几类事情上：

| # | 环节 | 为什么难 / 耗时 | 量级 |
|---|---|---|---|
| A | **数值反推** | 效果图是扁平 PNG，每个元素的位置、尺寸、字号、描边色和宽度、阴影偏移都要靠叠图和测色反推 | 最大头，约占一半 |
| B | **迭代循环慢** | 一轮要走：改预制体 → Play 里重开界面 → 等入场动画 → 截图 → 离线叠图 → 量偏差。每轮几分钟，一个界面要十几轮 | 大 |
| C | **无报错的隐性坑** | Multiple 切片越界导致白图、按钮模板文字框为负数尺寸、`LocalizeTextMeshProUGUI` 会反推材质、Pad 预制体被误加载。这些都只能靠截图发现 | 中，且难排查 |
| D | **切图映射** | 中文名切图要对应到旧资源；同名但用途不同（如 help_icon_02）；有些组合图美术没给，需要自己拼 | 中 |
| E | **文字样式** | 描边是用 Underlay 实现的，每种样式要建 10 种语言的材质；效果图是英文、运行时是中文，没法直接对比；字号只能估 | 中 |
| F | **状态覆盖** | 未购买、可领取、已领取等状态要靠改存档或临时切显隐才能截到 | 中 |
| G | **多分辨率** | 效果图只有一种尺寸，Pad 没有效果图；元素要挂在正确的锚点体系上才能跨屏 | 中 |
| H | **效果图与数据矛盾** | 文案 key 不一致、画的是金币而实际奖励是体力，只能转给策划确认 | 小，但会阻塞 |

**原型复核时的新发现**：用下文第 3 节的自动定位工具复测购买弹窗截图（`Library/BPShots/rt_act.png`）时，「购买上得」「升 2 级」和加号图标与效果图在纵向上还有 8–17px 的残差，面板整体高度也略有不同（运行时中文换行更多）。这说明人工叠图仍有漏网，自动测量是有必要的。这一点待下次复核。

---

## 2. 治本：拿结构化源文件（推荐优先推动）

### 2.1 Figma 路线

| 方案 | 说明 | 适配度 |
|---|---|---|
| [UnityFigmaBridge](https://github.com/simonoliver/UnityFigmaBridge)（开源，OpenUPM） | 把 Figma 文档导入为 uGUI 预制体，使用 TMP；有自定义描边 shader，支持按节点名绑定脚本 | 直接生成新预制体，**会和现有预制体结构、代码绑定冲突** → 只适合全新界面，或把它当作「读数器」 |
| [cdmvision/unity-figma-importer](https://cdmdocs.gitbook.io/unity-figma-importer/)、[FigForge](https://github.com/havokentity/FigForge) | 同类 uGUI 导入器；FigForge 自带面向 Claude 和 Cursor 的 AI 桥 | 同上 |
| [Figma 官方 MCP Server](https://developers.figma.com/docs/figma-mcp-server/tools-and-prompts)（`get_design_context` / `get_variable_defs`） | 让 Claude 直接读选中节点的层级、尺寸、样式、文字属性 | **最契合我们的做法**：不生成预制体，只读数值，再映射到现有节点 |

### 2.2 PSD 路线（美术更常用）

| 方案 | 说明 |
|---|---|
| [psd-tools](https://pypi.org/project/psd-tools/)（Python，MIT） | 读图层树和 bbox、导出图层 PNG；文字图层的 `engine_dict` 里有字号、字体、颜色；图层效果描述符里有描边（FrFX）、投影（DrSh）、渐变叠加（GrFl）。**推荐作为解析器**（效果字段需要实测确认） |
| [Baum2](https://github.com/kyubuns/Baum2)（MIT，已停更）、[PSD2UGUI_X](https://github.com/sunsvip/PSD2UGUI_X)、[UnityPSDLayoutTool](https://github.com/pharan/UnityPSDLayoutTool) | PSD 一键生成 uGUI 预制体。思路可参考，但同样会和现有结构冲突 |
| Photoshop JSX 导出 JSON（[Rapo 的示例](https://medium.com/@andrew.rapo/using-adobes-javascript-based-extendscript-jsx-to-automate-photoshop-workflow-tasks-f3b6690650d9)、[psdtojson](https://www.npmjs.com/package/psdtojson)） | 让美术在 PS 里一键导出「图层 + 文字 + 效果」JSON，不依赖解析库的兼容性 |

### 2.3 推荐落地形态：spec JSON → ApplySpec

```
PSD / Figma ──解析──▶ spec.json（每个元素：名字、rect、锚点归属、sprite、文字{字号, 颜色, 描边色/宽, 阴影偏移, 渐变}）
                         │
现有预制体 ◀──ApplySpec.cs（按 sprite 名 / 节点名映射到现有节点，只改数值，不改结构）
```

- px 转换为 `anchoredPosition` / `sizeDelta` 时，按 CanvasScaler（768 宽适配）和父节点的锚点换算，**保持节点原有的锚点体系**，这样跨分辨率不会坏。
- 描边、阴影换算成 TMP Underlay 参数后，复用 `LocalizedTextMaterial.cs` 生成 10 种语言的材质。

### 2.4 需要和美术约定的交付规范

1. 交付 PSD（或 Figma 链接），画布宽 768；手机效果图给两种屏高（如 1664 / 1850），有条件的话再给一张 Pad 图。
2. 图层名 = 切图名（英文、与资源名一致）；组名 = 界面区块名。
3. **文字保留为文字图层**，不要栅格化；描边、投影、渐变用图层样式，不要画进位图。
4. 九宫格切图标注拉伸区；每个元素标注它跟随顶、底还是中部（或跟随某个面板）。
5. 每个状态各给一张图（未购买、可领取、已领取……）；效果图使用游戏真实文案的 key 或英文原文。

---

## 3. 治标：没有源文件时，从 PNG 自动测量（已做原型）

### 3.1 思路

```
效果图 PNG ──CV/OCR──▶ 元素框 + 文字样式 ┐
                                          ├─▶ 差异表(dx, dy, 缩放, 字号差, 颜色差) ─▶ 自动修正 ─▶ 重测
运行时 ──Unity API──▶ 节点精确屏幕矩形 + TMP 参数 ┘
```

运行时这一侧**不需要 CV**：用 `RectTransform.GetWorldCorners` 或 MCP 的 `get_object_screen_bounds` 能拿到像素级精确的矩形，TMP 参数也能直接读。需要用 CV 和 OCR 的只有效果图这一侧。

### 3.2 原型结果（脚本在 `scripts/proto/`）

**切图定位**（`locate.py`）：OpenCV 多尺度模板匹配，带 alpha 掩码，粗搜后在全分辨率下精修。测试对象是购买弹窗的 7 张切图，跑一次约 28 秒。

| 切图 | 效果图置信度 | 运行时置信度 | dx | dy | 缩放比 |
|---|---|---|---|---|---|
| 加号 icon | 0.95 | 0.95 | 0 | −8 | 0.94 |
| 倒计时 icon | 1.00 | 0.86 | −6 | 11 | 1.05 |
| 升 2 级 | 0.87 | 0.90 | 2 | −17 | 1.00 |
| 购买上得 | 0.80 | 0.97 | 1 | −15 | 0.99 |
| 大面板 ×3（九宫格、被内容遮挡） | 0.45–0.58 | — | 失败 | | |

- **非拉伸的图标和插图：可用。**偏差能直接读出来。
- **九宫格大面板：整图匹配失败。**改用四角分别匹配后，单个角的置信度有 0.85–0.97，但因为有多个面板长得一样，角会配错。改进方法是用运行时矩形做初值，在 ±80px 内做带几何约束的局部搜索。这个方法还没有实现。

**文字识别**（`ocr.swift`，macOS 自带 Vision 框架，零依赖）：效果图上 16 行文字全部识别出来，包括按钮里的描边字 `$9.99`、`Skip to Stage 2`、`2d 17h`，置信度都是 1.0，并给出了行框。
- Tesseract 对描边游戏字的召回很差，**不推荐**。备选是 PaddleOCR（Apache-2.0，跨平台）。
- 文字颜色：在行框内按亮度取亮像素和暗像素，得到填充色和描边色。例如 `$9.99` 测得白字 + 深绿描边 RGB(5,122,8)，可以直接用作 Underlay 颜色。
- 字号：行框高度 ÷「字体行高系数」。这个系数需要先对每种字体做一次标定：在 Unity 里渲染已知字号，测出行框高度。
- 描边宽度：对暗色区域做距离变换，取中位数。阴影偏移：暗色质心相对亮色质心的偏移。
- **前提**：运行时要切到和效果图相同的语言（通常是英文）再比文字，否则换行和字宽都对不上。

### 3.3 元素配对

- 图片：效果图里匹配到的切图名 = 图集 sprite 名 → 预制体里引用这个 sprite 的 Image 节点。
- 文字：OCR 文本 = 英文本地化值 → 反查 Term → 挂了这个 Term 的 `LocalizeTextMeshProUGUI` 节点。
- 配不上的元素（动态奖励、数字）列为「动态内容」，只对齐容器。

---

## 4. 缩短迭代循环

| 手段 | 做法 | 收益 |
|---|---|---|
| **编辑器内实时叠图** | 在运行时 UI 最顶层加一个 `RawImage`，显示效果图，半透明或差值混合，按快捷键切换 | 改一个数立刻能看到重影，不用再截图跑离线叠图；预计迭代成本下降最多 |
| 动画快进 | 测试时把入场动画直接 `Evaluate` 到结束，或把 timeScale 调大 | 每轮省掉等待 |
| 状态夹具 | 每个界面写一个 `ReskinFixture`，一键切到未购买、可领取、已领取 | 不再手改存档 |
| 无报错坑自动体检 | 换图后自动跑：Multiple 切片越界检查、sprite 为 NULL 的 Image、文字框尺寸为负、Pad 变体漏改 | C 类问题前置到一开始就暴露 |
| 回归比对 | 对齐完成后存基线截图；以后改动时用 [odiff](https://github.com/dmtrkovalenko/odiff) 或 pixelmatch 比，CI 可以用 [Unity Graphics Test Framework 的 ImageAssert](https://docs.unity3d.com/Packages/com.unity.testframework.graphics@9.0/api/UnityEngine.TestTools.Graphics.ImageAssert.html) | 防止后续改动把对齐弄坏 |

### 4.1 元素分组 + 辅助线（有帮助，建议做进叠图组件）

**分组：先对组，再对组内**
- 按**锚点归属**分组，而不是按视觉上挨得近不近。例如：顶栏（跟顶）、进度条区、滚动列表、底栏（跟底）、弹窗面板（跟中），每组对应预制体里的一个父节点。
- 对齐顺序：先对齐组容器的矩形，再对齐组内元素相对组的偏移。
  - 一次改动修正一整组，自由度从「N 个绝对坐标」降到「1 个组矩形 + N 个相对偏移」。
  - 组内的相对关系在任何分辨率下都不变，所以对齐一次就能跨屏成立。
- 自动判别：差异表按组汇总。如果组内各元素的 dy 基本一致，说明是组整体偏移，只需要改父节点。例：购买弹窗的三个图标 dy 分别是 −8、−15、−17，应该先查面板的位置和高度，而不是逐个去挪图标。如果组内偏差各不相同，才逐个调。
- 分组也直接决定了「差值换算到哪个锚点下」，把第 5 节的锚点问题前置解决。

**辅助线：从效果图自动生成，叠加在运行时画面上**
- 线的来源：第 3 节测出的元素框和 OCR 行框，自动生成以下几类线：
  - 元素的四边和中线；
  - 文字的基线和行顶线，用来判断字号和垂直位置；
  - 组的外框；
  - 锚点参考线，例如屏幕顶、屏幕底、屏高 0.6 处、安全区边界。
- 显示方式：效果图的线用一种颜色，运行时节点的实际框（从 Unity 读的精确值）用另一种颜色，两者之间直接标出差值的像素数；可以按组开关，避免线太多变成噪声。
- 准确率：人眼判断「线和边是否重合」能做到 1px 级，比判断两张半透明图的重影准得多。描边、阴影、发光会让重影的边界很模糊，但线的位置不受影响。
- **跨分辨率**：这是辅助线最大的价值。辅助线按各自所属组的锚点存储（例如「距底 120px」「面板内 x=40」），换到别的分辨率时，线会跟着锚点移动，标出元素「应该在的位置」。这样没有效果图的分辨率（20:9、Pad）也能检查。叠整张效果图在别的长宽比下是对不上的，辅助线不受这个限制。

**注意**
- 组必须对应真实的父节点和锚点；视觉上分在一组、但挂在不同父节点下的元素，要拆开。
- 动态内容（奖励图标、数字、滚动位置）只画容器的线。
- 有入场动画的界面，要等动画结束、或把动画快进到最后一帧再比。

---

## 5. 分辨率适配

1. **对齐的是锚点关系，不是绝对像素。**每个元素先归类：跟顶、跟底、跟中，或跟随某个面板，再把差值换算到它自己的锚点下。上次的「插图和面板之间露出色带」就是因为锚点归属错了。
2. **批量截图矩阵**：一个脚本循环 Game 视图尺寸（768x1664、768x1850 / 20:9、19.5:9、16:10、Pad 1536x2048），每个界面、每个状态都截一张，拼成一张总览图，一眼看完。
3. **安全区**：用 Device Simulator 看刘海和底部横条。它的 `safeArea` 在部分机型上有[已知偏差](https://issuetracker.unity3d.com/issues/rectangle-of-safe-area-covers-not-all-safe-area-of-the-screen-or-covers-the-area-outside-safe-area-on-some-devices-in-the-device-simulator-window)，关键机型仍要真机确认。
4. **Pad**：没有效果图时，以「手机效果图 × Pad 内容缩放比」为参考，自动测量工具同样可以套用（把效果图按比例缩放后作为参考）。

---

## 6. 不推荐的方向

- **全自动生成预制体**（Figma / PSD → Prefab 一键生成）：会丢掉现有节点路径、代码绑定、动画和 Pad 变体。我们的约束是保持结构，所以只「读数值、写回现有节点」。
- **截图转 UI 的 AI 方案**（如 UIED、ScreenCoder 一类）：面向 Web 和 App 的元素检测，精度达不到像素级，还依赖云端 OCR。
- **Tesseract**：识别不了描边游戏字。

---

## 7. 落地计划（建议）

| 阶段 | 内容 | 预计工作量 | 依赖 |
|---|---|---|---|
| P0 | 编辑器内叠图组件（含按组开关的辅助线）+ 动画快进 + 无报错坑体检脚本 | 1 天 | 无 |
| P1 | 运行时节点矩形 / TMP 参数导出 + 效果图测量（locate + Vision OCR + 测色）→ 差异表 | 1–2 天 | 无 |
| P2 | 差异表 → 自动修正（按锚点换算）+ 多分辨率截图矩阵 | 2 天 | P1 |
| P3 | 和美术约定 PSD / Figma 交付规范；psd-tools 或 Figma MCP 解析成 spec.json → ApplySpec | 2–3 天 | 美术配合 |

P0+P1 不依赖任何人，可以先做，下次换皮就能用上。P3 收益最大，但需要美术改变交付方式，建议同步去沟通。

---

## 参考来源

- [simonoliver/UnityFigmaBridge](https://github.com/simonoliver/UnityFigmaBridge) ／ [OpenUPM](https://openupm.com/packages/com.simonoliver.unityfigma/)
- [havokentity/FigForge](https://github.com/havokentity/FigForge)、[cdmvision unity-figma-importer](https://cdmdocs.gitbook.io/unity-figma-importer/)、[TrackMan FigmaToUnity（UI Toolkit）](https://github.com/trackman/unity.package.figmatounity)
- [Figma MCP Server 工具说明](https://developers.figma.com/docs/figma-mcp-server/tools-and-prompts)
- [psd-tools](https://pypi.org/project/psd-tools/)、[kyubuns/Baum2](https://github.com/kyubuns/Baum2)、[sunsvip/PSD2UGUI_X](https://github.com/sunsvip/PSD2UGUI_X)、[pharan/UnityPSDLayoutTool](https://github.com/pharan/UnityPSDLayoutTool)、[psdtojson](https://www.npmjs.com/package/psdtojson)
- [MulongXie/UIED](https://www.github.com/MulongXie/UIED)
- [odiff](https://github.com/dmtrkovalenko/odiff)、[Unity Graphics Test Framework ImageAssert](https://docs.unity3d.com/Packages/com.unity.testframework.graphics@9.0/api/UnityEngine.TestTools.Graphics.ImageAssert.html)
- [Device Simulator 文档](https://docs.unity3d.com/Packages/com.unity.device-simulator@1.0/manual)、[safeArea 已知问题](https://issuetracker.unity3d.com/issues/rectangle-of-safe-area-covers-not-all-safe-area-of-the-screen-or-covers-the-area-outside-safe-area-on-some-devices-in-the-device-simulator-window)

---

## 8. 实施结果（2026-11-13，P0–P3 已落地）

工具清单与用法见 `SKILL.md` 第 1 节。下面是在战令界面上的实测结果：

| 验证项 | 结果 |
|---|---|
| 体检 `ReskinHealth` | 战令目录 12 个预制体，约 1 秒；能检出引用丢失、白块等问题（深色无图遮罩按提示级处理） |
| 购买弹窗（只有 PNG，走 CV + OCR） | 第一轮识别出「整个弹窗 Root 偏 −14px，组内离散 1.3px」→ 只挪 Root；共写回 18 项后复测：整体偏差 **−14 → −0.2px**，剩余 2 项在噪声阈值附近，已收敛 |
| 差异分析耗时 | 有 OCR 缓存时约 7 秒/界面；首次 OCR 每张图约 10–60 秒 |
| 多分辨率矩阵 | 5 种分辨率（20:9 / 19.5:9 / 1664 / 16:10 / 4:3），全自动约 1 分钟，带辅助线截图 + 总览图 |
| 主界面 + PSB（走 spec） | 见下 |

### PSB 源文件对效果的提升（`战令_cut_cut.psb`，主界面 768x1850）

PSB 里有 278 个图层，其中 40 个是可编辑文字图层，带完整的图层样式。解析耗时约 2 秒。和「只用 PNG 目测 / CV」相比：

| 维度 | 只有 PNG | 有 PSB |
|---|---|---|
| 字号 | OCR 行高估算，误差 ±5–10%，要靠阻尼多轮收敛 | **精确**（如按钮 53px，目测做成了 44/46） |
| 字距 | 测不出 | **精确**（标题 −30 → TMP −3；做之前标题被撑宽并顶满文字框） |
| 描边 / 投影颜色 | 3-means 取色，误差 10–50（色差） | **精确**（标题描边 (201,98,14)，目测做成了 (132,50,5)） |
| 描边宽度 / 偏移 | 估算 | **精确**：3px，下移 1px（投影 + 阻塞 100%） |
| 分列 / 分状态的差异 | 很难发现 | 直接可见：宝箱格三列数字描边色各不相同；隐藏状态组可以一起导出 |
| 渐变 | 看不出 | 色标精确（标题其实是几乎纯黄，不是黄→橙渐变） |
| 位置 | CV 配准 ±1–2px | 文字用 PSB 文字块反向定位，同样是 ±1–2px；**图片层名大多是「组 8442」这类默认名，按名配对收益有限，仍要靠 CV** |

结论：
- **文字类效果（字号、字距、颜色、描边、投影、渐变）提升最大**，从「估算 + 多轮试」变成「一次写对」。校准后描边的实测宽度比为 0.99–1.04。
- **位置类提升中等**：PSB 本身是精确的，但图层名不规范时要靠图像匹配。按《美术交付规范》要求「图层名 = 切图名」后，图片也能按名字精确配对。
- 已用 PSB 数据写回主界面：3 种描边材质（×10 种语言）、标题和按钮的字号与字距。

### 校准记录
- `ReskinTextStyle.OutlineCalibration = 1.54`：理论换算出的描边实测会粗 1.52–1.56 倍，用 PSB 对比实测得出；校准后实测宽比为 0.99–1.04。
- 字号阻尼 50%：OCR 估字号在相邻两轮出现过 30.9→34→28.7 的振荡，加阻尼后收敛。
