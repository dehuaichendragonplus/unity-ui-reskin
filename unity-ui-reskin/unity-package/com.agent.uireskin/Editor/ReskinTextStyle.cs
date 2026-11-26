// 文字效果自动对齐：把效果图（PSD 图层样式）的「描边/投影像素值」换算成 TMP Underlay 参数，
// 按样式去重生成本地化材质（fontRoot 下每种语言各一份），并挂到预制体文字上；同时设置填充色 / 顶底渐变 / 字号。
// 文字组件、字体目录、材质命名都读 ReskinConfig（ProjectSettings/UIReskinConfig.json）。
//
// 换算（TMP SDF shader）：
//   1 个 SDF 单位 ≈ GradientScale 个图集像素；图集像素 → 屏幕像素 = 字号 / 图集采样字号(pointSize)
//   描边 px = UnderlayDilate × ScaleRatioC × 0.5 × GradientScale × 字号/pointSize
//   偏移 px = UnderlayOffset × ScaleRatioC × GradientScale × 字号/pointSize（向下为负 OffsetY）
//   描边与字号是比例关系，所以同一描边像素在不同字号下需要不同 dilate → 材质按「颜色+描边+偏移+字号档」去重。
// 输入 styles.json（diff.py --spec 生成）：
//   {"prefab": "...", "prefix": "RS", "styles": [{"path": "...", "fontPx": 53, "fill": [255,255,255], "gradient": [[top],[bottom]] | null,
//                                               "outline": {"color": [122,30,5], "px": 3, "offset": [0,1]} | null}]}
// 用法：ReskinTextStyle.Apply("Library/ReskinShots/main/diff_spec/styles.json", dryRun: true)
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace UIReskin
{
    public static class ReskinTextStyle
    {
        /// <summary>语言字体目录（每种语言一个子目录），来自 ReskinConfig.fontRoot。</summary>
        public static string FontRoot => ReskinConfig.Data.fontRoot;

        /// <summary>FontRoot 下实际存在的语言子目录（目录里有 fontAssetPattern 对应字体资产的才算）。</summary>
        internal static string[] Langs
        {
            get
            {
                var root = FontRoot.TrimEnd('/');
                if (!AssetDatabase.IsValidFolder(root)) return new string[0];
                var list = new List<string>();
                foreach (var dir in AssetDatabase.GetSubFolders(root))
                {
                    var l = dir.Substring(dir.LastIndexOf('/') + 1);
                    if (AssetDatabase.FindAssets(ReskinConfig.FontAssetName(l), new[] { dir }).Length > 0) list.Add(l);
                }
                return list.ToArray();
            }
        }

        [System.Serializable] public class Outline { public float[] color; public float px; public float[] offset; }
        [System.Serializable] public class Style { public string path; public float fontPx; public float[] fill; public Outline outline; public float[] gradientTop, gradientBottom; public float charSpacing = float.NaN; }

        /// 描边像素校准系数（ReskinConfig.outlineCalibration）：理论公式算出的描边在实际 SDF 字体上的粗细比（FoodFlow1 实测 1.54）。
        /// 校准方法：diff.py --spec 报告里「描边宽 实测/目标」偏离 1 时，按比值修正配置后重跑 Apply。
        public static float OutlineCalibration => ReskinConfig.Data.outlineCalibration;
        [System.Serializable] public class StyleList { public string prefab; public string prefix = "RS"; public List<Style> styles = new List<Style>(); }

        /// 像素 → Underlay 参数（按该语言字体图集的 pointSize 与材质 GradientScale）
        public static (float dilate, float offY) UnderlayFromPx(float pointSize, float gradientScale, float fontPx, float outlinePx, float offsetDownPx, float ratioC = 1f)
        {
            float texelPx = fontPx / Mathf.Max(1f, pointSize);
            float dilate = outlinePx / (0.5f * gradientScale * texelPx * ratioC * OutlineCalibration);
            float offY = -offsetDownPx / (gradientScale * texelPx * ratioC);
            return (Mathf.Clamp(dilate, -1f, 1f), Mathf.Clamp(offY, -1f, 1f));
        }

        static string Hex(float[] c) => c == null ? "none" : $"{(int)c[0]:X2}{(int)c[1]:X2}{(int)c[2]:X2}";
        static Color Col(float[] c) => c == null ? Color.white : new Color(c[0] / 255f, c[1] / 255f, c[2] / 255f, c.Length > 3 ? c[3] / 255f : 1f);

        static TMP_FontAsset FontOf(string lang)
        {
            var want = ReskinConfig.FontAssetName(lang);
            foreach (var g in AssetDatabase.FindAssets($"{want} t:TMP_FontAsset", new[] { FontRoot.TrimEnd('/') }))
            {
                var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(g));
                if (fa != null && fa.name == want) return fa;
            }
            return null;
        }

        /// 生成 / 更新各语言（FontRoot 下全部语言目录）的同名材质；返回样式名（写进本地化组件样式字段用）
        static string EnsureMaterial(string prefix, string srcSuffix, Outline o, float fontPx, StringBuilder log, bool dryRun)
        {
            int bucket = Mathf.RoundToInt(fontPx / 4f) * 4;  // 字号档：4px 一档
            float offDown = o.offset != null && o.offset.Length > 1 ? o.offset[1] : 0;
            string name = $"{prefix} {Hex(o.color)} {o.px:0.#}px {offDown:0.#} f{bucket}";  // 同名材质会按当前校准系数重算参数
            if (dryRun) { log.AppendLine($"  材质 {name}（源 {srcSuffix}）"); return name; }
            foreach (var l in Langs)
            {
                var src = ReskinConfig.MaterialPath(l, srcSuffix);
                var dst = ReskinConfig.MaterialPath(l, name);
                if (AssetDatabase.LoadAssetAtPath<Material>(src) == null) { log.AppendLine("  MISSING " + src); continue; }
                if (AssetDatabase.LoadAssetAtPath<Material>(dst) == null) AssetDatabase.CopyAsset(src, dst);
                var m = AssetDatabase.LoadAssetAtPath<Material>(dst);
                var fa = FontOf(l);
                float point = fa != null ? fa.faceInfo.pointSize : 90f;
                float gs = m.HasProperty(ShaderUtilities.ID_GradientScale) ? m.GetFloat(ShaderUtilities.ID_GradientScale) : 10f;
                m.EnableKeyword("UNDERLAY_ON");
                m.SetColor(ShaderUtilities.ID_UnderlayColor, Col(o.color));
                m.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0);
                m.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0);
                // ScaleRatioC 依赖 dilate 本身，迭代几次收敛
                float ratio = 1f; (float d, float y) u = (0, 0);
                for (int i = 0; i < 4; i++)
                {
                    u = UnderlayFromPx(point, gs, bucket, o.px, offDown, ratio);
                    m.SetFloat(ShaderUtilities.ID_UnderlayDilate, u.d);
                    m.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, u.y);
                    ShaderUtilities.GetShaderPropertyIDs();
                    TMPro.ShaderUtilities.UpdateShaderRatios(m);
                    ratio = m.HasProperty(ShaderUtilities.ID_ScaleRatio_C) ? Mathf.Max(0.05f, m.GetFloat(ShaderUtilities.ID_ScaleRatio_C)) : 1f;
                }
                EditorUtility.SetDirty(m);
                if (l == ReskinConfig.Data.reportLang) log.AppendLine($"  材质 {name}: dilate={u.d:F3} offY={u.y:F3} (point={point} gs={gs} ratioC={ratio:F2}{(ratio < 0.99f ? " 已补偿" : "")}){(Mathf.Abs(u.d) >= 0.999f ? " ⚠dilate 到上限，图集 padding 不够画这么粗的描边" : "")}");
            }
            return name;
        }

        public static string Apply(string stylesPath, bool dryRun = true)
        {
            if (Application.isPlaying && !dryRun) return "请先退出 Play";
            var list = JsonUtility.FromJson<StyleList>(File.ReadAllText(ReskinCore.Abs(stylesPath)));
            var sb = new StringBuilder($"[RESKIN] 文字样式 {(dryRun ? "DRY-RUN" : "APPLY")} {list.prefab} {list.styles.Count} 项\n");
            if (Langs.Length == 0) sb.AppendLine("  ⚠ 没探测到按语言分目录的字体（ReskinConfig.Dump() 看探测结果），描边材质项会跳过，只改字号 / 字距 / 填充");
            var root = PrefabUtility.LoadPrefabContents(list.prefab);
            var made = new Dictionary<string, string>();
            int changed = 0;
            try
            {
                foreach (var s in list.styles)
                {
                    var targets = ReskinCore.FindInPrefab(root.transform, s.path).Select(x => x.GetComponent<TMP_Text>()).Where(x => x != null).ToList();
                    if (targets.Count == 0) { sb.AppendLine("  MISSING " + s.path); continue; }
                    foreach (var t in targets) {
                    var lang = ReskinConfig.LangOfFont(t.font != null ? t.font.name : null, ReskinConfig.Data.reportLang);
                    var cur = t.fontSharedMaterial != null ? t.fontSharedMaterial.name : "";
                    var srcSuffix = ReskinConfig.StyleOfMaterial(cur);
                    bool needMat = Langs.Length > 0 && s.outline != null && s.outline.color != null && s.outline.px > 0;
                    if (needMat && string.IsNullOrEmpty(srcSuffix))
                    {
                        srcSuffix = ReskinConfig.Data.fallbackSourceStyle;
                        sb.AppendLine($"  ⚠ {s.path} 当前材质 '{cur}' 不符合 materialPattern，克隆源改用 fallbackSourceStyle '{srcSuffix}'（dry-run 若报 MISSING 请改用 ReskinMaterialStyle）");
                    }
                    var line = new StringBuilder($"  {s.path.Substring(System.Math.Max(0, s.path.Length - 60))}:");
                    // 字号（目标是屏幕像素；文字所在画布 1 单位 = 1 参考像素，再除以自身缩放）
                    if (s.fontPx > 0)
                    {
                        float acc = 1f;
                        for (var tr = t.transform; tr != null && tr != root.transform.parent; tr = tr.parent) acc *= tr.localScale.y;
                        float target = s.fontPx / Mathf.Max(0.01f, acc);
                        target = Mathf.Round(target * 10) / 10;
                        if (Mathf.Abs((t.enableAutoSizing ? t.fontSizeMax : t.fontSize) - target) > 0.5f)
                        {
                            line.Append($" 字号 {(t.enableAutoSizing ? t.fontSizeMax : t.fontSize)}→{target}");
                            if (!dryRun) { if (t.enableAutoSizing) t.fontSizeMax = target; else t.fontSize = target; }
                        }
                    }
                    // 字距（PS tracking 1/1000em → TMP characterSpacing 1/100em）
                    if (!float.IsNaN(s.charSpacing) && Mathf.Abs(t.characterSpacing - s.charSpacing) > 0.05f)
                    {
                        line.Append($" 字距 {t.characterSpacing}→{s.charSpacing}");
                        if (!dryRun) t.characterSpacing = s.charSpacing;
                    }
                    // 填充 / 渐变
                    if (s.gradientTop != null && s.gradientTop.Length >= 3 && s.gradientBottom != null && s.gradientBottom.Length >= 3)
                    {
                        line.Append($" 渐变 {Hex(s.gradientTop)}→{Hex(s.gradientBottom)}");
                        if (!dryRun) { t.color = Color.white; t.enableVertexGradient = true; t.colorGradient = new VertexGradient(Col(s.gradientTop), Col(s.gradientTop), Col(s.gradientBottom), Col(s.gradientBottom)); }
                    }
                    else if (s.fill != null && s.fill.Length >= 3)
                    {
                        var c = Col(s.fill);
                        if (!t.enableVertexGradient && ((Vector4)(t.color - c)).magnitude > 0.03f)
                        {
                            line.Append($" 填充 {ColorUtility.ToHtmlStringRGB(t.color)}→{Hex(s.fill)}");
                            if (!dryRun) t.color = new Color(c.r, c.g, c.b, t.color.a);
                        }
                    }
                    // 描边 → 材质
                    if (Langs.Length > 0 && s.outline != null && s.outline.color != null && s.outline.color.Length >= 3 && s.outline.px > 0)
                    {
                        float fpx = s.fontPx > 0 ? s.fontPx : t.fontSize;
                        var key = $"{Hex(s.outline.color)}|{s.outline.px}|{(s.outline.offset != null && s.outline.offset.Length > 1 ? s.outline.offset[1] : 0)}|{Mathf.RoundToInt(fpx / 4f) * 4}|{srcSuffix}";
                        if (!made.TryGetValue(key, out var matName)) made[key] = matName = EnsureMaterial(list.prefix, srcSuffix, s.outline, fpx, sb, dryRun);
                        line.Append($" 描边材质 {srcSuffix}→{matName}");
                        if (!dryRun)
                        {
                            AssetDatabase.SaveAssets();
                            var m = AssetDatabase.LoadAssetAtPath<Material>(ReskinConfig.MaterialPath(lang, matName));
                            if (m != null) t.fontSharedMaterial = m;
                            else sb.AppendLine($"  ⚠ {s.path} 语言 {lang} 下没有 {ReskinConfig.MaterialPath(lang, matName)}，未换材质，也不改样式字段");
                            var loc = ReskinConfig.LocText(t);
                            if (m != null && loc != null) { var so = new SerializedObject(loc); var p = so.FindProperty(ReskinConfig.Data.locMaterialField); if (p != null) { p.stringValue = ReskinConfig.FieldFromStyle(matName); so.ApplyModifiedPropertiesWithoutUndo(); } }
                        }
                    }
                    sb.AppendLine(line.ToString() + (targets.Count > 1 || ReskinCore.PathOf(t.transform, root.transform) != s.path ? $"  ← 映射到 {ReskinCore.PathOf(t.transform, root.transform)}" : ""));
                    changed++;
                    }
                }
                if (!dryRun) { AssetDatabase.SaveAssets(); PrefabUtility.SaveAsPrefabAsset(root, list.prefab); }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            sb.AppendLine(dryRun ? $"共 {changed} 项（未写入），新材质 {made.Count} 种" : $"已写入 {changed} 项，材质 {made.Count} 种");
            if (!dryRun && made.Count > 0) sb.AppendLine(ReskinConfig.RunAfterMaterialsHook());
            return sb.ToString();
        }
    }
}
