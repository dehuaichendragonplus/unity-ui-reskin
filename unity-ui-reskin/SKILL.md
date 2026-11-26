---
name: unity-ui-reskin
description: Unity 项目通用的界面替换 / 换皮 / 美术替换 / 按效果图（PSD、PSB、Figma、PNG）1:1 还原 UI，以及美术换皮后的审查与修复。零配置，任何 Unity 工程直接可用（自动探测工程、资源子模块、本地化文字组件、字体目录，编辑器工具自动装成嵌入式包）。凡是「美术给了新切图或效果图，替换 / 对齐某界面、叠图测量位置字号描边、多分辨率适配」，或「美术换皮了 / 某 tag 或提交之后美术改了 UI，审查要不要改代码、给美术出修改清单、修复换皮问题」都用本 skill——即使用户没说"换皮"二字。项目自带同类 skill 或换皮规范（如 ui-reskin / ui-reskin-review）时优先用项目的，本 skill 作工具补充。不用于纯代码 diff 审查或从零设计新界面。
---

# Unity 界面替换与换皮审查（代理端通用版）

`<skill>` = 本文件所在目录。所有工具都在这里，**不需要项目做任何配置**；项目差异靠运行时自动探测，探测结论由你记进项目记忆。

## 第一步：识别项目（每个项目首次使用，代理自己做，不问用户）

1. `python3 -I <skill>/scripts/uir_project.py info` —— Unity 工程、仓库根、子模块 / 资源仓库、本地化组件、规范字体、UIReskin 包是否已装。
2. 项目已有自己的换皮规范 / 工具（grep `class ReskinCore`、`.claude/skills/*reskin*`、`docs/*换皮*`）→ 以项目的为准，下面只作补充。
3. 需要 Unity 编辑器工具时：`bash <skill>/scripts/install_unity_package.sh` → 装成 `Packages/com.agent.uireskin`（嵌入式包，不改 manifest、不碰 Assets，自动写进本机 `.git/info/exclude`；工程自带 UIReskin 时自动跳过）。等编译完，`execute_code`：`return UIReskin.ReskinConfig.Dump();` 核对自动探测结果；不对就写 `ProjectSettings/UIReskinConfig.json` 只覆盖错的字段，再 `ReskinConfig.Redetect()`。
4. 需要 Python 图像工具时：`bash <skill>/scripts/setup.sh`（一次性建 `~/.cache/ui-reskin/venv` + 编译 OCR，已建好跳过）。之后一律 `~/.cache/ui-reskin/venv/bin/python -I <skill>/scripts/<脚本>.py`。
5. 驱动 Unity 用已连接的 Unity MCP（funplay 等：`execute_code`、`capture_game_view`、`simulate_mouse_click`、`run_tests`）。MCP 没连上就告诉用户，不要改用桌面点击绕过。

## 第二步：按任务读规范（先完整读一遍再动手）

| 任务 | 读 |
|---|---|
| 按效果图替换 / 还原界面 | `references/reskin.md`（流程、工具链、项目画面与坐标、坑速查、PSD 复盘、换算） |
| 美术换皮后审查 / 评估 / 修复 | `references/review.md`（原则、静态 19 项清单、运行时审查、文档结构、修复规范）+ `references/review-tools.md` |
| 给美术提效果图交付要求 | `references/art-delivery.md` |
| 工具选型背景 | `references/research.md` |

两套可以接着用：先审查出清单，再用替换工具链落地。

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

## 每个项目要记进项目记忆的结论（下次直接用，不再重新摸）

效果图规格与手机画面裁切框、UI 参考分辨率与自管缩放框、世界空间换算、文字体系（`ReskinConfig.Dump()` 结论、规范字体约束、字体问题谁改）、打开各界面的调用方式（`OpenViews.cs` 确认过的）、描边校准系数、守卫测试程序集、资源仓库 / 子模块与提交顺序。

## 目录

- `scripts/`：`uir_project.py`（零配置探测）、`install_unity_package.sh`、`setup.sh`、`crop_effect.py`、`psd_to_spec.py`、`figma_to_spec.py`、`diff.py`、`align_nodes.py`、`measure.py`、`overlay.py`、`matrix.py`、`contact_sheet.py`、`export_snapshot.py`、`prefab_audit.py`、`reskin_common.py`、`ocr.swift`
- `unity-package/com.agent.uireskin/`：编辑器工具（命名空间 `UIReskin`：`ReskinConfig` 自动探测、`ReskinHealth` 体检、`ReskinProbe` / `ReskinMatrix` 运行时导出与截图、`ReskinApply` 位置修正、`ReskinTextStyle` / `ReskinMaterialStyle` 文字样式材质、`ReskinOverlay` 叠图、`ReskinAnim` 动画快进；菜单 Tools/UIReskin）
- `snippets/reskin/`、`snippets/review/`：`execute_code` 一次性片段
- `examples/`：样式 JSON 示例；`examples/FoodFlow1/` 是框架专用的开界面写法参考（`examples/BubbleWord3/` 若存在，是本机保留的项目原版，不随公开仓库分发）
