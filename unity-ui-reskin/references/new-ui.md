# 新建界面：从 PSD 到可用功能

> 目标：拿到效果图源文件后，一路做到「界面能在游戏里打开、控件齐全、按钮有功能、资源能打包加载、和效果图对齐」。
> 分工：**确定性的事交给工具**（骨架、控件、切图、图集、资源登记、对齐测量）；**依赖项目框架的业务代码由代理照项目里最相近的界面写**；**推断不出的业务语义列「待确认」**，不编造。
> `<skill>` = 本 skill 目录；Python 一律 `~/.cache/ui-reskin/venv/bin/python -I <skill>/scripts/<脚本>.py`。

## 0. 输入要求

| 输入 | 能做到哪 |
|---|---|
| PSD / PSB（推荐） | 全流程：骨架、控件、精确字号 / 描边、切图导出 |
| Figma 节点 JSON | 同上（`figma_to_spec.py`；切图从 Figma 导出） |
| 只有 PNG | 没有图层，生成不了骨架：参照工程里相近界面复制一个 prefab 改，或用 MCP 建节点；之后第 5 步起照常（对齐、资源、代码） |

## 1. 摸清项目（每个项目第一次做，结论记进项目记忆）

1. `python3 -I <skill>/scripts/uir_project.py info`；没装编辑器工具就 `bash <skill>/scripts/install_unity_package.sh`。
2. `execute_code`：`return UIReskin.ReskinSurvey.Run();` —— 界面基类分布与典型例子（含脚本路径）、绑定特性、打开 / 关闭界面的管理器方法、多语言组件、事件总线。`Run("Shop")` 按关键字找最像的参照界面。
3. `return UIReskin.ReskinAssets.Survey();` —— 资源管理方式（Resources / Addressables / YooAsset）、图集数量与打包方式。
4. 选**参照界面**：和新界面功能最像、基类相同的那个。读它的脚本、prefab 结构（根节点、遮罩、动画节点）、注册方式（类特性 / UI 配置表 / 枚举）、谁来打开它。后面每一步都照它做。
5. UI 参考分辨率：根 Canvas 的 `CanvasScaler.referenceResolution`（第 2 步的 `--ref-width` 用）。

## 2. 解析源文件 → 骨架布局

```bash
python -I <skill>/scripts/psd_to_spec.py 效果图.psd --out <工作目录> --export-layers [--all-states]
python -I <skill>/scripts/spec_to_layout.py <工作目录>/spec.json --prefab Assets/<按项目目录约定>/UIXxx.prefab --frame auto|full|x0,y0,x1,y1 --ref-width <参考宽>
```

产物：`layout.json`（节点树）、`styles.json`（文字描边 / 投影）、`bindings.json`（每个节点的功能与建议）、`layout.md`（人看的树）。

**先读 `layout.md` 复核功能推断**：
- 「（形状推断）」的项是按结构猜的（底图 + 文字 = 按钮、大面板右上角小方块 = 关闭、底 + 填充长条 = 进度条、重复结构 = 列表），**逐个核对**；
- 名字推断错了、漏了，直接改 `layout.json` 的 `role`（button / close / progress / toggle / list / input / 空）、`anchor`、`name` 再生成；
- 手机画面裁切不对（`⚠ auto`）就传 `--frame` 重跑。

## 3. 生成 prefab 与控件

```
UIReskin.ReskinBuild.Build("<工作目录相对工程根>/layout.json", dryRun: true)    // 计划：节点数、切图命中 / 导入 / 占位、控件
UIReskin.ReskinBuild.Build("…/layout.json", dryRun: false)                     // 写入（已存在拒绝，overwrite: true 才覆盖）
UIReskin.ReskinTextStyle.Apply("…/styles.json", dryRun: true / false)          // 描边 / 投影 / 渐变（需要语言字体体系）
UIReskin.ReskinHealth.Check("<prefab 所在目录>")
```

- 工作目录放 `<Unity 工程>/Library/ReskinShots/<界面>/`（execute_code 只能用相对路径）。
- 切图：工程里有同名 Sprite 直接用（PSD 默认图层名「图层 12 / 组 3」不参与匹配）；否则导入图层 PNG（内容相同的「拷贝」图层自动复用一张），导入设置照抄目录邻居；都没有放品红占位。
- 控件：Button（组按钮自动加透明接射线图）、Slider（不可拖，填充按 PSD 长度换算初始值）、Toggle、ScrollRect（Viewport + RectMask2D + Content + LayoutGroup，间距 / 边距按 PSD 量，背景留在视口外，第一项是模板）。

## 4. 对齐项目约定（Editor API 改 prefab）

照参照界面把骨架改成项目的样子：
- **结构**：根节点套项目惯用的层（例：`Root` / 半透明遮罩 / 入场动画节点）；
- **命名**：按项目绑定方式改节点名（绑定特性写路径的，路径要和代码一致；有 `Btn_` / `Txt_` 前缀约定的照改）；
- **切图改名**：导入的切图按项目资源命名规范改英文名（`AssetDatabase.RenameAsset`，GUID 不变），默认名「组_8059_拷贝」不能留；
- **文字**：静态文案挂项目的多语言组件并找 key（多语言表里 grep 同义词条复用；没有的不擅自新增，先用占位并列待策划）；字体换成项目规范字体 / 样式材质；
- **动态文字**（`dynamicText`）：不挂静态多语言组件，由代码写入。

## 5. 业务代码（代理写，照参照界面）

1. 新建界面脚本：基类、生命周期方法、绑定写法、类上的注册特性**全部照参照界面**（`ReskinSurvey` 已列出）。不发明新框架。
2. 按 `bindings.json` 逐项接：

| role | 怎么接 |
|---|---|
| close | 调项目 UI 管理器的关闭接口（参照界面怎么关就怎么关） |
| button | 按 `label` 推断的功能接项目已有接口：看广告 → 项目广告接口，成功回调里发奖；购买 → 计费 / 扣币接口，价格读商店配置；领取 → 发奖 + 奖励飞行 + 刷新状态；前往 / 开始 → 跳转对应界面或进关。**找不到对应接口、配置 id、价格来源的写 `// TODO(待确认)` 并列进清单，不编造 id / 价格** |
| progress | 找数据来源（grep 相关系统的进度字段），设置 `value`（0~1）；找不到列待确认 |
| toggle | `onValueChanged` 回调；需要持久化的写存档（照项目存档方式） |
| list | 数据驱动：第一个子项当模板，`Instantiate` 或项目已有的列表 / 对象池组件；子项里的控件同样绑定 |
| dynamicText | 代码写入（数量、倒计时、价格），格式照项目已有工具函数（数字缩写、时间格式） |
| input | 复制项目已有输入框的结构与校验 |

3. **注册**：照参照界面登记（类特性的 prefab 路径 / UI 配置表 / 枚举 / 层级）。
4. **入口**：需求说了从哪打开就接到那里；没说就只提供打开方法，入口列待确认。
5. 编译：`wait_for_compilation` → `get_compilation_errors` 清零。

## 6. 资源配置

```
UIReskin.ReskinAssets.EnsureAtlas("<切图目录>", dryRun: true / false)
UIReskin.ReskinAssets.EnsureLoadable(new[]{ "<prefab>" }, dryRun: true / false)
```

- 图集：已被收录跳过；按清单打包的图集补图；都没有就按工程现有图集的设置新建（按文件夹打包，V1 / V2 跟工程）；工程完全不用图集不建。
- 资源管理：Resources 下直接可用；Addressables 建条目（组跟同目录已有条目）；YooAsset 已被收集器覆盖就跳过，否则照抄路径最接近的收集器新建。**项目另有约定（如活动资源单独分组、按需下载标签、不进首包）以项目为准**，先读项目记忆 / 文档。
- 改了资源配置要跑项目的资源 / 图集守卫测试。

## 7. 验证

1. Play 中打开：走真实入口；还没接入口时 `ReskinMatrix.Snap("<prefab 名>", () => UIReskin.ReskinBuild.PreviewCo("<prefab>", 参考宽, 参考高), "Library/ReskinShots/<界面>", "<prefab>", "WxH")`。
2. 对齐效果图：`diff.py` → `ReskinApply` / `ReskinTextStyle` 循环到「需修正 0 项」（`references/reskin.md` 第 4.3 节）。
3. 功能：`simulate_mouse_click` 逐个点按钮，确认回调执行、无报错、不被遮挡（`InteractAudit.cs`）；列表滚到首尾看裁剪。
4. 多分辨率：`ReskinMatrix.Run` 截矩阵。
5. 跑受影响的测试程序集。

## 8. 交付

- **修改记录**：新增的脚本 / prefab / 切图 / 图集、注册与资源配置改动、对齐结果。
- **待确认清单**（按角色）：推断不出的按钮功能、数据来源、配置 id / 价格、文案 key、占位切图、入口位置。
- 提交按项目约定（资源在子模块 / 独立仓库时先提交资源仓库）；`Packages/com.agent.uireskin`、`Library/` 下的工作文件不提交。

## 9. 坑

| 现象 | 原因 | 处理 |
|---|---|---|
| 推断成按钮的其实是倒计时 / 条目底 | 形状像按钮 | 工具已排除时间格式与「背景 / 底板 / 任务」类名字；仍有误判就改 `layout.json` 的 role |
| 小方块被当成关闭键 | 右上角的信息图标 / 角标 | 形状推断都要复核；不是就把 role 清空 |
| 文字节点没有字体 | 工程没导入 TMP Essentials，也没有任何 TMP 字体 | 导入后重新生成（`overwrite: true`），或第 4 步统一换字体 |
| 列表子项错位 | 原 PSD 子项间距不均 | LayoutGroup 用的是间距中位数；确实不等距的改回手动布局（去掉 LayoutGroup） |
| 生成的 prefab 在别的分辨率下跑位 | 第一层锚点推断不符合设计意图 | 改 `layout.json` 的 anchor（top / middle / bottom - left / center / right / stretchX / stretch）重生成 |
| 切图名是中文 / 默认名 | PSD 图层没按规范命名 | 第 4 步改名；长期按 `references/art-delivery.md` 要求美术「图层名 = 切图名」 |
