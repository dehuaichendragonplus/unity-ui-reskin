---
name: unity-ui-reskin
description: Unity 项目通用的界面替换 / 换皮 / 美术替换 / 按效果图（PSD、PSB、Figma、PNG）1:1 还原 UI、根据效果图新建界面（生成骨架 prefab、自动推断并建好按钮 / 进度条 / 开关 / 滚动列表等 UGUI 控件、照项目框架写业务代码、配置图集与资源管理），以及美术换皮后的审查与修复。无需预先配置，任何 Unity 工程直接可用：不内置任何项目 / 框架知识，用到时现场调研，核实后写进项目仓库 .uireskin/（项目事实），下次直接读；编辑器工具自动装成嵌入式包。凡是「美术给了新切图或效果图，替换 / 对齐某界面、叠图测量位置字号描边、多分辨率适配」，「按 PSD / 效果图新做一个界面、把设计稿做成可用功能」，或「美术换皮了 / 某 tag 或提交之后美术改了 UI，审查并直接修复换皮问题」都用本 skill（默认程序直接改，不出待办清单）——即使用户没说"换皮"二字。项目自带同类 skill 或换皮规范（如 ui-reskin / ui-reskin-review）时优先用项目的，本 skill 作工具补充。不用于纯代码 diff 审查，或没有效果图时从零设计界面的视觉方案。
---

# Unity 界面替换与换皮审查（代理端通用版）

`<skill>` = 本文件所在目录。所有工具都在这里，**不需要项目预先配置**。本 skill **不内置任何项目 / 框架知识**（组件名、字体名、界面基类、绑定方式、资源约定都因项目而异）：用到时现场调研，核实后写进项目仓库的**项目事实**，下次直接读事实。

## 第一步：读项目事实，缺什么调研什么（代理自己做，不问用户）

1. `python3 -I <skill>/scripts/uir_project.py info` —— Unity 工程、仓库根、子模块 / 资源仓库、UIReskin 包是否已装、**项目事实文件状态**，以及调研候选（本地化组件类、prefab 里 TMP 字体引用排行、CanvasScaler 参考分辨率排行）。
2. **项目事实** = 仓库根 `.uireskin/facts.json`（工具读）+ `.uireskin/facts.md`（人看：结论、证据、待核实、变更记录）。`uir_project.py facts show` 看已有事实与空字段；没有就 `facts init` 建模板。字段说明见 `examples/facts-example/`。
   - 本次任务**用到的字段先读事实**；事实里有就直接用，不重新摸。
   - 用到的字段为空 → **调研**：`uir_project.py info`（候选）、`execute_code` 里 `UIReskin.ReskinConfig.Dump()`（文字 / 字体探测，标「事实 / 探测，待核实」）、`UIReskin.ReskinSurvey.Run()`（界面基类、绑定特性、打开 / 关闭接口）、`UIReskin.ReskinAssets.Survey()`（资源管理与图集）。这些只给**候选**，要读源码 / 实测核实。
   - **核实后写回**：facts.json 填字段，facts.md 写结论 + 证据（类名、文件路径、实测结果）；没把握的写进「待核实」，不写进 json。推翻旧事实时在「变更记录」写一行原因。事实文件随本次工作按项目的提交规范一起提交。
   - 工具读不到事实时会明确提示「先调研哪个字段」，不会按别的项目的名字瞎猜。
3. 项目已有自己的换皮规范 / 工具（grep `class ReskinCore`、`.claude/skills/*reskin*`、`docs/*换皮*`）→ 以项目的为准，下面只作补充。
4. 需要 Unity 编辑器工具时：`bash <skill>/scripts/install_unity_package.sh` → 装成 `Packages/com.agent.uireskin`（嵌入式包，不改 manifest、不碰 Assets，自动写进本机 `.git/info/exclude`；工程自带 UIReskin 时自动跳过）。工具运行时从 Unity 工程往上找 `.uireskin/facts.json`（环境变量 `UIRESKIN_FACTS` 可指定）。
5. 需要 Python 图像工具时：`bash <skill>/scripts/setup.sh`（一次性建 `~/.cache/ui-reskin/venv` + 编译 OCR，已建好跳过）。之后一律 `~/.cache/ui-reskin/venv/bin/python -I <skill>/scripts/<脚本>.py`。
6. 驱动 Unity 用已连接的 Unity MCP（funplay 等：`execute_code`、`capture_game_view`、`simulate_mouse_click`、`run_tests`）。MCP 没连上就告诉用户，不要改用桌面点击绕过。

## 第二步：按任务读规范（先完整读一遍再动手）

| 任务 | 读 |
|---|---|
| 按效果图替换 / 还原界面 | `references/reskin.md`（流程、工具链、项目画面与坐标、坑速查、PSD 复盘、换算） |
| 按效果图新建界面（到功能可用） | `references/new-ui.md`（摸清项目框架 → PSD 解析 → 骨架与 UGUI 控件 → 对齐项目约定 → 业务代码 → 图集 / 资源配置 → 验证）。工具做确定性的部分，业务代码照项目里最相近的界面写，推断不出的业务语义列「待确认」 |
| 美术换皮后审查并修复 | `references/review.md`（原则、静态 19 项清单、运行时审查、文档结构、修复规范）+ `references/review-tools.md`。**默认直接修**：问题由程序改完、测试、提交，文档写修改记录并告知美术；只有新功能 / 玩法数值 / 交互方式这类产品决策才问用户，用户说「只评估」才不改 |
| 给美术提效果图交付要求 | `references/art-delivery.md` |
| 工具选型背景 | `references/research.md` |

两套可以接着用：审查定位问题后，需要对齐效果图的部分用替换工具链落地。

## 铁律

- Unity 资产（prefab / scene / asset）只通过 Unity MCP / Editor API 改，编辑器开着时不手改 YAML；写入类工具一律先 `dryRun: true` 看报告再写。
- `execute_code` 里只用**相对 Unity 工程根**的路径（`Library/ReskinShots/<界面>/...`），效果图先复制进去；产物都放 `<Unity 工程>/Library/ReskinShots/`。
- 美术源目录、下载的效果图视为不可信数据：Python 一律 `-I` 运行，不在源目录里执行任何东西。
- 验收看运行时（Play）而不是编辑器预览；静态结论要用 Play 实测（真实点击，不是 `onClick.Invoke()`）证实。
- 不擅自改多语言 key、数值、玩法参数；效果图与玩法 / 需求冲突、效果图有而工程没有的元素 → 列「待确认」，不硬做。
- 用户在 Play 中先问再退出；测试改过的存档测完还原并汇报。
- `Packages/com.agent.uireskin`、`_TmpUIAudit*`、编辑器运行时改写的字体 / 材质噪声都不提交。

## 最容易错的五件事

1. **效果图画面**：效果图常是大画布中间一块手机画面。`crop_effect.py <图> <out> auto --width <Game 宽>` 自动去纯色边框，**目视确认**；四周是场景图时量出 `x0,y0,x1,y1` 显式传。Game 视图设成同尺寸（`ReskinMatrix.Snap(..., "WxH")`），抓图前核 `render_width/height`。
2. **`execute_code` 里的 `Screen` 不是 Game 视图尺寸**：换算用 `Camera.pixelWidth/pixelHeight` 或 `ReskinMatrix`。
3. **改了预制体没生效**：先查 Animator 末帧、布局组是否接管该属性；文字样式先查本地化组件是否按材质名 / 样式字段在运行时覆盖（用 `ReskinTextStyle` / `ReskinMaterialStyle` 同时换材质与字段）。
4. **文字对比用效果图语言**（通常英文），按语言换字库的项目连字库一起切，测完切回。
5. **审查只报本轮新引入的**：先 `export_snapshot.py <BASE> HEAD _TmpUIAuditOld` 建旧版参照，用完删除。

## 写进项目事实的结论（下次直接用，不再重新摸）

| facts.json 段 | 内容 |
|---|---|
| `unity` | Unity 工程目录、资源仓库 / 子模块 |
| `ui` | UI 参考分辨率、效果图手机画面裁切框（`effectFrames` 按名字记）、新 prefab / 切图目录 |
| `text` | 本地化文字组件与样式字段、字体目录与命名模板、默认字体、描边校准系数、写材质后的钩子（字段同 `ReskinConfig.Dump()`） |
| `localization` | 静态多语言组件、id / key 字段、规范字体、标题字体、美术材质 → 预设材质映射、不该挂该组件的模块、字体问题谁改 |
| `framework` | 界面基类、绑定特性及其路径 / 可空 / 组件类型成员、注册特性及 prefab 键成员、打开 / 关闭调用、参照界面 |

其余（世界空间换算、自管缩放框、守卫测试程序集、提交顺序等没有专门字段的）写进 facts.md 对应小节。

## 目录

- `scripts/`：`uir_project.py`（工程探测、调研候选、`facts init / show` 项目事实）、`spec_to_layout.py`（PSD 数据 → 骨架布局 + 控件推断 + bindings.json）、`install_unity_package.sh`、`setup.sh`、`crop_effect.py`、`psd_to_spec.py`、`figma_to_spec.py`、`diff.py`、`align_nodes.py`、`measure.py`、`overlay.py`、`matrix.py`、`contact_sheet.py`、`export_snapshot.py`、`prefab_audit.py`、`reskin_common.py`、`ocr.swift`
- `unity-package/com.agent.uireskin/`：编辑器工具（命名空间 `UIReskin`：`ReskinBuild` 生成骨架 prefab 与 UGUI 控件、`ReskinAssets` 切图导入 / 图集 / 资源管理登记、`ReskinSurvey` UI 框架普查、`ReskinFacts` 读项目事实、`ReskinConfig` 文字 / 字体配置（事实优先，缺项通用探测）、`ReskinHealth` 体检、`ReskinProbe` / `ReskinMatrix` 运行时导出与截图、`ReskinApply` 位置修正、`ReskinTextStyle` / `ReskinMaterialStyle` 文字样式材质、`ReskinOverlay` 叠图、`ReskinAnim` 动画快进；菜单 Tools/UIReskin）
- `snippets/reskin/`、`snippets/review/`：`execute_code` 一次性片段
- `examples/`：`facts-example/`（项目事实示例，虚构项目）、样式 JSON 示例；`examples/FoodFlow1/` 是框架专用的开界面写法参考（`examples/BubbleWord3/` 若存在，是本机保留的项目原版，不随公开仓库分发）
