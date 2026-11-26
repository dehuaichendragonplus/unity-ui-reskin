// 换皮「无报错坑」体检：换图后、Play 前先跑一遍，把只能靠截图才发现的问题前置暴露。
// 检查项：
//  1. Multiple 模式贴图切片越界（换图尺寸变化 → Sprite 为 NULL → 白底且无报错）
//  2. 预制体里引用丢失（GUID 不存在，常见于「原本就是白方块」）
//  3. 启用的 Image 没有 Sprite 且不透明（会显示白块）
//  4. 文字框宽或高 ≤ 1（自动字号被压到最小 / 竖排）
//  5. 锚点 anchorMin > anchorMax（CommonButton 模板常见，配合缩放导致按钮扁小偏移）
//  6. 本地化文字组件（ReskinConfig.locTextType）的样式字段在某语言目录下没有对应材质（运行时查不到通常静默保留预制体原材质）；
//     healthFieldMatchesMaterial=true 时另查「样式字段 ≠ 预制体当前材质的样式名」（FoodFlow1 体系 Awake 按材质名反推字段，不一致会被覆盖回旧值）
//  7. 手机 / Pad 预制体使用的 Sprite 集合差异（提示 Pad 漏改）
// 用法：ReskinHealth.Check("Assets/<模块>/Prefabs/UI")，或菜单 Tools/UIReskin/体检选中目录
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
#if UIRESKIN_SPRITE_EDITOR
using UnityEditor.U2D.Sprites;
#endif
using UnityEngine;
using UnityEngine.UI;

namespace UIReskin
{
    public static class ReskinHealth
    {
        [MenuItem("Tools/UIReskin/体检选中目录")]
        static void Menu()
        {
            var p = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(p)) { Debug.LogWarning("[RESKIN] 先在 Project 里选中模块目录"); return; }
            if (!AssetDatabase.IsValidFolder(p)) p = Path.GetDirectoryName(p).Replace('\\', '/');
            Debug.Log(Check(p));
        }

        static bool Whitish(Color c) => c.r > 0.85f && c.g > 0.85f && c.b > 0.85f;

        public static string Check(string dir)
        {
            var sb = new StringBuilder();
            int issues = 0;
            void Add(string kind, string msg) { sb.AppendLine($"[{kind}] {msg}"); issues++; }

            // 1. 切片越界
#if UIRESKIN_SPRITE_EDITOR
            var fac = new SpriteDataProviderFactories(); fac.Init();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter imp) || imp.spriteImportMode != SpriteImportMode.Multiple) continue;
                var dp = fac.GetSpriteEditorDataProviderFromObject(imp); dp.InitSpriteEditorDataProvider();
                dp.GetDataProvider<ITextureDataProvider>().GetTextureActualWidthAndHeight(out int w, out int h);
                foreach (var r in dp.GetSpriteRects())
                    if (r.rect.xMin < 0 || r.rect.yMin < 0 || r.rect.xMax > w || r.rect.yMax > h)
                        Add("切片越界", $"{path} [{r.name}] rect={r.rect} 贴图={w}x{h} → 跑 FixMultipleSpriteRects");
            }

#else
            sb.AppendLine("[切片越界·跳过] 工程没有 com.unity.2d.sprite 包，无法读 Multiple 切片");
#endif

            // 2~6. 预制体
            var langs = ReskinTextStyle.Langs;
            var prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { dir }).Select(AssetDatabase.GUIDToAssetPath).ToList();
            var spriteSets = new Dictionary<string, HashSet<string>>();
            foreach (var p in prefabs)
            {
                var root = PrefabUtility.LoadPrefabContents(p);
                try
                {
                    var set = new HashSet<string>();
                    foreach (var comp in root.GetComponentsInChildren<Component>(true))
                    {
                        if (comp == null) { continue; }
                        var tp = ReskinCore.PathOf(comp.transform, root.transform);
                        var so = new SerializedObject(comp);
                        var it = so.GetIterator();
                        while (it.NextVisible(true))
                            if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue == null && it.objectReferenceInstanceIDValue != 0)
                            {
                                // 无图的深色遮罩（ImgMask 等）引用丢失不影响显示，只提示
                                bool harmless = comp is Image im0 && it.propertyPath == "m_Sprite" && !Whitish(im0.color);
                                if (harmless) sb.AppendLine($"[引用丢失·提示] {p} :: {tp} {comp.GetType().Name}.{it.propertyPath}（深色无图，显示不受影响）");
                                else Add("引用丢失", $"{p} :: {tp} {comp.GetType().Name}.{it.propertyPath}");
                            }

                        if (comp is Image img)
                        {
                            if (img.sprite != null) set.Add(img.sprite.name);
                            else if (img.enabled && img.color.a > 0.05f && Whitish(img.color) && img.gameObject.activeSelf && img.GetType() == typeof(Image))
                                Add("空 Sprite", $"{p} :: {tp} 启用且不透明但无 Sprite（会显示白块）");
                        }
                        if (comp is RectTransform rt)
                        {
                            if (rt.anchorMin.x > rt.anchorMax.x || rt.anchorMin.y > rt.anchorMax.y)
                                Add("锚点异常", $"{p} :: {tp} anchorMin={rt.anchorMin} > anchorMax={rt.anchorMax} → 参考 NormalizeButton");
                        }
                        if (comp is TMPro.TMP_Text tmp && !string.IsNullOrEmpty(tmp.text) && tmp.gameObject.activeSelf)
                        {
                            var r = tmp.rectTransform.rect;
                            if (r.width <= 1 || r.height <= 1)
                                Add("文字框过小", $"{p} :: {tp} rect={r.width:F0}x{r.height:F0}（自动字号会压到最小或竖排）");
                        }
                        var cfg = ReskinConfig.Data;
                        if (!string.IsNullOrEmpty(cfg.locTextType) && comp.GetType().Name == cfg.locTextType)
                        {
                            var mProp = so.FindProperty(cfg.locMaterialField);
                            if (mProp != null && mProp.propertyType == SerializedPropertyType.String && !string.IsNullOrEmpty(mProp.stringValue))
                            {
                                var style = ReskinConfig.StyleFromField(mProp.stringValue);
                                // defaultMaterialStyle = 字体资产自带的默认材质，不是独立 .mat 文件
                                var missing = style == cfg.defaultMaterialStyle ? new List<string>() : langs.Where(l => AssetDatabase.LoadAssetAtPath<Material>(ReskinConfig.MaterialPath(l, style)) == null).ToList();
                                if (missing.Count > 0)
                                    Add("样式材质缺失", $"{p} :: {tp} {cfg.locMaterialField}='{mProp.stringValue}' 在 {string.Join("/", missing)} 下没有 {ReskinConfig.MaterialName("<语言>", style)}.mat（运行时会保留预制体原材质）");
                                {
                                    var tmpC = comp.GetComponent<TMPro.TMP_Text>();
                                    var curStyle = tmpC != null && tmpC.fontSharedMaterial != null ? ReskinConfig.StyleOfMaterial(tmpC.fontSharedMaterial.name) : null;
                                    if (curStyle != null && curStyle != style)
                                    {
                                        var msg = $"{p} :: {tp} {cfg.locMaterialField}='{mProp.stringValue}' 但当前材质样式是 '{curStyle}'（Awake 按材质名反推字段的体系会被覆盖回旧值；用 ReskinTextStyle 同时换材质与字段）";
                                        if (cfg.healthFieldMatchesMaterial) Add("材质名不一致", msg); else sb.AppendLine("[材质名不一致·提示] " + msg);
                                    }
                                }
                            }
                        }
                    }
                    int missingScripts = root.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
                    if (missingScripts > 0) Add("脚本丢失", $"{p} 有 {missingScripts} 个 Missing Script");
                    spriteSets[p] = set;
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }

            // 7. 手机 / Pad 差异
            var padTail = ReskinConfig.Data.padSuffix + ".prefab";
            foreach (var p in prefabs.Where(x => x.EndsWith(padTail)))
            {
                var phone = p.Substring(0, p.Length - padTail.Length) + ".prefab";
                if (!spriteSets.ContainsKey(phone)) continue;
                var onlyPhone = spriteSets[phone].Except(spriteSets[p]).ToList();
                var onlyPad = spriteSets[p].Except(spriteSets[phone]).ToList();
                if (onlyPhone.Count + onlyPad.Count > 0)
                    sb.AppendLine($"[Pad 差异·提示] {Path.GetFileName(p)} 仅手机用: {string.Join(", ", onlyPhone)} | 仅 Pad 用: {string.Join(", ", onlyPad)}");
            }

            return $"[RESKIN] 体检 {dir}：预制体 {prefabs.Count} 个，问题 {issues} 项\n" + sb;
        }
    }
}
