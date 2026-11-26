# UIReskin 项目事实（示例：虚构项目 SampleGame）

> 真实项目放在仓库根 `.uireskin/facts.md`，与 `facts.json` 同步。每条写证据；没把握的放「待核实」，不进 json。

## 画面与坐标
- UI 参考分辨率 750×1334，按宽适配 —— 证据：`Assets/Bundles/UI/UIRoot.prefab` 的 CanvasScaler（referenceResolution 750×1334、match 0）。
- 效果图：设计稿 1200×1334 中间 750 宽是手机画面，裁切框记为 `padded`（225,0,975,1334）；纯手机稿记为 `phone` —— 证据：`效果图/商店.png` 与运行时截图对齐实测偏差 ≤1px。
- 新 prefab 放 `Assets/Bundles/UI/Prefabs/<模块>/`，切图放 `Assets/Bundles/UI/Textures/<模块>/` —— 证据：现有 42 个窗口都按此放。

## 文字与字体
- 本地化文字组件 `SampleGame.UI.LocText`，多语言 key 字段 `key`，样式字段 `styleName`（值不带前缀）—— 证据：`LocText.cs` 第 30~60 行，Awake 按当前材质名回写 styleName（所以 healthFieldMatchesMaterial = true）。
- 规范字体 `Main SDF` / 标题 `Title SDF`：LocText 只在这两个字体上挂语言回退 —— 证据：`LocText.ApplyFallback()`。
- 写完新样式材质要重建 `FontStyleTable`（钩子 `SampleGame.EditorTools.FontStyleTable.Rebuild`）—— 证据：不重建时运行时找不到新材质（实测）。
- 描边校准系数 1.2 —— 证据：商店标题 3px 描边，diff 报告实测宽比 0.99。

## UI 框架（写业务代码用）
- 界面基类 `WindowBase`（OnOpen / OnClose / OnRefresh）。
- 节点绑定：字段上 `[UINode("Root/Btn_Close")]`（路径是第 1 个构造参数），`Optional = true` 允许缺节点。
- 注册：类上 `[Window("UI/Shop/ShopWindow")]`（PrefabPath），层级在 `WindowLayer` 枚举。
- 打开 / 关闭：`UIManager.Instance.Open<T>()` / `Close<T>()`。
- 参照界面：每日奖励 `DailyRewardWindow`（列表 + 领取）、商店 `ShopWindow`（购买）。

## 资源管理与图集
- YooAsset：`Assets/Bundles/UI` 整目录一个收集器（DefaultPackage / UI 组，PackDirectory）。
- 图集：每个模块切图目录一个按文件夹打包的 SpriteAtlas（V2）。

## 待核实
- 战斗内（`/Battle/`）文字是否完全不能挂 LocText（目前只看到战斗代码里自己写字）。

## 变更记录
- 2026-11-26 初次调研写入。
