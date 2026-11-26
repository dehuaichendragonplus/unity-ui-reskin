# unity-ui-reskin（代理端界面替换 / 换皮审查工具包）

把 BubbleWord3（源自 FoodFlow1）沉淀的「按效果图 1:1 还原界面」与「美术换皮审查」两套 skill + 工具，做成 **Claude Code 用户级 skill**：装一次，任何 Unity 项目直接可用，**项目里不需要任何配置文件**。

## 安装

```bash
bash install.sh            # 装到 ~/.claude/skills/unity-ui-reskin
bash install.sh --python   # 可选：同时建 Python 图像环境（首次用到时代理也会自己建）
```

装完新开一个 Claude Code 会话，直接说需求即可触发，例如：
- 「美术给了新切图和 PSD，按效果图把商店界面还原一下」
- 「美术在 v1.2.0 之后换皮了，审查并直接修好」（默认程序直接改 prefab、字体、代码，测试后提交，最后出修改记录告知美术）

## 零配置是怎么做到的

| 项目差异 | 自动处理方式 |
|---|---|
| Unity 工程在哪 | 从当前目录往上找；仓库根下 2 层内找（排除子模块里的美术工程） |
| 资源在主仓 / 子模块 / 软链进来的独立仓库 | 按用户给的 tag / 提交，在主仓、所有子模块、Assets 下软链指向的仓库里找能解析它的那个 |
| 编辑器工具（C#） | 代理首次需要时运行 `install_unity_package.sh`，装成嵌入式包 `Packages/com.agent.uireskin`：不改 `manifest.json`、不碰 `Assets/`，自动写进本机 `.git/info/exclude` 不会被误提交；工程自带 UIReskin 时自动跳过；asmdef 有版本宏保护，缺 TMP / 2D Sprite 包时安全跳过而不是让工程报错 |
| 本地化文字组件、样式字段、多语言 key 字段 | `ReskinConfig` 运行时用 TypeCache 识别（优先 `LocalizeTextMeshProUGUI`，否则找带材质名字符串字段的组件） |
| 语言字体目录、字体 / 样式材质命名 | 扫描 TMP 字体资产：名字里含所在目录名的按「根目录 + 模板」分组，取语言数最多的一组 |
| 写材质后要刷新的字库清单 | 项目里给一个静态方法标 `[UIReskin.UIReskinHook]` 即自动调用（不需要就不用管） |
| 审查用的本地化组件 / 规范字体 | 按脚本名、资产名自动查 guid；项目没有就自动跳过该项并说明「不适用」 |
| 效果图手机画面 | `crop_effect.py auto` 自动去纯色边框；整屏设计时提示代理目视后传坐标 |

探测结果缓存在 `Library/UIReskin/detected.json`（不进版本库）。确实探测错了才需要写 `ProjectSettings/UIReskinConfig.json` 覆盖个别字段。每个项目确认过的画面裁切、参考分辨率、开界面方式等，由代理写进该项目的记忆，下次直接用。

## 依赖

- Claude Code + 一个能 `execute_code` 的 Unity MCP（如 funplay），用于驱动编辑器、截图、真实点击、跑测试。
- Unity 2022.3 / 6000.x（已用 2022.3.62 与 6000.3.13 的编译器离线编译通过）。
- macOS（OCR 用 Vision 框架；其余 Python 工具跨平台）。Python 3 + `setup.sh` 自动装的 OpenCV / psd-tools / Pillow / numpy。

## 目录

```
unity-ui-reskin/                 ← 整个目录就是 skill
  SKILL.md                       入口：识别项目 → 按任务读规范 → 铁律 / 易错点
  references/
    reskin.md                    界面替换规范（流程、工具链、项目画面与坐标、坑速查、PSD 复盘、换算）
    review.md                    换皮审查规范（原则、静态 19 项清单、运行时审查、文档结构、修复、提交、收尾）
    review-tools.md              审查工具说明
    art-delivery.md              给美术的效果图交付规范
    research.md                  工具选型调研（FoodFlow1）
  scripts/                       Python / Shell（uir_project.py 零配置探测、install_unity_package.sh、crop_effect、psd_to_spec、diff、align_nodes、export_snapshot、prefab_audit ……）
  unity-package/com.agent.uireskin/   编辑器工具（命名空间 UIReskin，菜单 Tools/UIReskin）
  snippets/reskin|review/        execute_code 一次性片段
  examples/                      样式 JSON 示例；FoodFlow1 开界面写法（项目特化参考）
```
