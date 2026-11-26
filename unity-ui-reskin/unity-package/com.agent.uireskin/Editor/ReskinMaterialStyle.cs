// 文字样式材质「手工克隆」工具：从各语言已有的样式材质（默认 Shadow Blue）克隆出新样式，按 PSD 元数据的投影像素重算 Underlay，
// 并把预制体文字节点的样式字段（ReskinConfig.locMaterialField）/ 顶点渐变 / 字号上限一次改好，最后执行 ReskinConfig.afterMaterialsHook。
//
// 为什么不用 ReskinTextStyle.Apply：Apply 以节点「当前材质」当克隆源，节点当前材质名在字库里不存在时（例：卡片预制体挂的是共享材质），
// 一个材质也建不出来。本工具的源材质由 JSON 显式指定，缺源会在 dry-run 里报出来并拒绝写入。
//
// 输入 JSON（示例见 ~/.claude/skills/unity-ui-reskin/examples/choice_page_styles.json）：
//   {"materials":[{"name":"Shadow Blue 67","source":"Shadow Blue","color":[5,57,152],"fontPx":67.2,"px":3,"offsetDown":2}],
//    "nodes":[{"prefab":"Assets/.../X.prefab","path":"Root/TextTitle/Text","material":"Shadow Blue 67",
//              "gradientTop":[255,255,255],"gradientBottom":[241,221,244],"fontSizeMax":67.2}]}
//   materials[].px / offsetDown 取 PSD 投影的 size / distance（阻塞 100% 当实心外扩）；name 是样式名，文件按 ReskinConfig.materialPattern 命名。
//   nodes[].material 是写进样式字段的值（带不带前缀按 ReskinConfig.locMaterialPrefix，工具会自动剥前缀找材质）；
//   gradientTop / gradientBottom 同时给才开顶点渐变，否则关；fontSizeMax <= 0 不改。
// 用法：ReskinMaterialStyle.Apply("Library/ReskinShots/<界面>/styles_clone.json", dryRun: true)（写入需退出 Play）
// 注意：必须先 dry-run 看有无「缺源 / 缺节点 / ⚠ dilate 到上限」。拉丁字库 pointSize 大（~110）时，小字号（约 40px 以下的 3px 投影）会到上限，画不满。
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace UIReskin
{
    public static class ReskinMaterialStyle
    {
        [System.Serializable] public class MatDef { public string name; public string source; public float[] color; public float fontPx; public float px; public float offsetDown; }
        [System.Serializable] public class NodeDef { public string prefab; public string path; public string material; public float[] gradientTop; public float[] gradientBottom; public float fontSizeMax; }
        [System.Serializable] public class Spec { public List<MatDef> materials = new List<MatDef>(); public List<NodeDef> nodes = new List<NodeDef>(); }

        static Color Col(float[] c) => new Color(c[0] / 255f, c[1] / 255f, c[2] / 255f, 1f);

        static string MatPath(string lang, string name) => ReskinConfig.MaterialPath(lang, name);

        static TMP_FontAsset FontOf(string lang) =>
            AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(ReskinConfig.FontAssetPath(lang));

        public static string Apply(string specPath, bool dryRun = true)
        {
            if (Application.isPlaying && !dryRun) return "请先退出 Play";
            var spec = JsonUtility.FromJson<Spec>(File.ReadAllText(ReskinCore.Abs(specPath)));
            foreach (var m in spec.materials) if (string.IsNullOrEmpty(m.source)) m.source = ReskinConfig.Data.defaultCloneSource;
            if (spec.materials.Any(m => string.IsNullOrEmpty(m.source))) return "⚠ materials[].source 没写，项目事实 text.defaultCloneSource 也没有：先确认用哪个已有样式材质当克隆源";
            if (string.IsNullOrEmpty(ReskinConfig.Data.locTextType)) return "⚠ ReskinConfig.locTextType 为空：本工具要写本地化文字组件的样式字段，项目没有这类组件时改用 ReskinTextStyle 或手工换材质";
            var langs = ReskinTextStyle.Langs;
            var sb = new StringBuilder($"[RESKIN] 材质克隆 {(dryRun ? "DRY-RUN" : "APPLY")} 语言 {string.Join("/", langs)} 材质 {spec.materials.Count} 种 节点 {spec.nodes.Count} 个\n");
            if (langs.Length == 0) return sb.AppendLine("  ⚠ FontRoot 下没有语言目录：" + ReskinTextStyle.FontRoot).ToString();

            // 1) 校验：缺源就整体拒绝（不留半成品）
            var blocked = false;
            foreach (var m in spec.materials)
                foreach (var l in langs)
                    if (AssetDatabase.LoadAssetAtPath<Material>(MatPath(l, m.source)) == null) { sb.AppendLine($"  ✗ 缺源 {MatPath(l, m.source)}"); blocked = true; }
            foreach (var n in spec.nodes)
            {
                if (!AssetDatabase.LoadAssetAtPath<GameObject>(n.prefab)) { sb.AppendLine($"  ✗ 缺预制体 {n.prefab}"); blocked = true; continue; }
                var name = ReskinConfig.StyleFromField(n.material);
                var willMake = spec.materials.Exists(m => m.name == name);
                foreach (var l in langs)
                    if (!willMake && AssetDatabase.LoadAssetAtPath<Material>(MatPath(l, name)) == null) { sb.AppendLine($"  ✗ {n.path} 的材质 {n.material} 在 {l} 不存在，也不在本次 materials 里"); blocked = true; }
            }
            if (blocked) return sb.AppendLine("存在 ✗，未写入任何东西。").ToString();

            // 2) 材质
            foreach (var m in spec.materials)
            {
                foreach (var l in langs)
                {
                    var dst = MatPath(l, m.name);
                    var exists = AssetDatabase.LoadAssetAtPath<Material>(dst) != null;
                    var fa = FontOf(l);
                    var src = AssetDatabase.LoadAssetAtPath<Material>(MatPath(l, m.source));
                    float point = fa != null ? fa.faceInfo.pointSize : 90f;
                    float gs = src.GetFloat(ShaderUtilities.ID_GradientScale);
                    // dry-run 也要在临时副本上算，才能报「dilate 到上限」
                    var mat = dryRun ? new Material(src) : CloneIfMissing(MatPath(l, m.source), dst);
                    mat.EnableKeyword("UNDERLAY_ON");
                    mat.SetColor(ShaderUtilities.ID_UnderlayColor, Col(m.color));
                    mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0);
                    mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0);
                    float ratio = 1f; (float d, float y) u = (0, 0);
                    for (int i = 0; i < 4; i++)   // ScaleRatioC 依赖 dilate 本身，迭代几次收敛
                    {
                        u = ReskinTextStyle.UnderlayFromPx(point, gs, m.fontPx, m.px, m.offsetDown, ratio);
                        mat.SetFloat(ShaderUtilities.ID_UnderlayDilate, u.d);
                        mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, u.y);
                        ShaderUtilities.GetShaderPropertyIDs();
                        ShaderUtilities.UpdateShaderRatios(mat);
                        ratio = Mathf.Max(0.05f, mat.GetFloat(ShaderUtilities.ID_ScaleRatio_C));
                    }
                    if (dryRun) Object.DestroyImmediate(mat); else EditorUtility.SetDirty(mat);
                    sb.AppendLine($"  {l,-2} {m.name}: dilate={u.d:F3} offY={u.y:F3} (point={point} gs={gs}){(exists ? " [已存在，按当前参数重算]" : "")}{(Mathf.Abs(u.d) >= 0.999f ? "  ⚠ dilate 到上限，投影画不满" : "")}");
                }
            }
            if (!dryRun) AssetDatabase.SaveAssets();

            // 3) 预制体节点
            foreach (var group in GroupByPrefab(spec.nodes))
            {
                var root = PrefabUtility.LoadPrefabContents(group.Key);
                try
                {
                    foreach (var n in group.Value)
                    {
                        var t = root.transform.Find(n.path);
                        if (t == null) { sb.AppendLine($"  ✗ 缺节点 {group.Key} :: {n.path}"); continue; }
                        var loc = ReskinConfig.LocText(t);
                        var tmp = t.GetComponent<TMP_Text>();
                        if (loc == null || tmp == null) { sb.AppendLine($"  ✗ {n.path} 不是 {ReskinConfig.Data.locTextType}"); continue; }
                        var so = new SerializedObject(loc);
                        var p = so.FindProperty(ReskinConfig.Data.locMaterialField);
                        if (p == null) { sb.AppendLine($"  ✗ {n.path} 的 {ReskinConfig.Data.locTextType} 没有字段 {ReskinConfig.Data.locMaterialField}"); continue; }
                        var grad = n.gradientTop != null && n.gradientBottom != null;
                        sb.AppendLine($"  {n.path}: {p.stringValue} → {n.material}  渐变={(grad ? "开" : "关")}{(n.fontSizeMax > 0 ? $"  字号上限 {tmp.fontSizeMax}→{n.fontSizeMax}" : "")}");
                        if (dryRun) continue;
                        p.stringValue = n.material; so.ApplyModifiedPropertiesWithoutUndo();
                        tmp.enableVertexGradient = grad;
                        if (grad) { var top = Col(n.gradientTop); var bot = Col(n.gradientBottom); tmp.colorGradient = new VertexGradient(top, top, bot, bot); }
                        if (n.fontSizeMax > 0) tmp.fontSizeMax = n.fontSizeMax;
                    }
                    if (!dryRun) PrefabUtility.SaveAsPrefabAsset(root, group.Key);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }

            if (!dryRun) sb.AppendLine(ReskinConfig.RunAfterMaterialsHook());
            sb.AppendLine(dryRun ? "（未写入）" : "已写入。提交前：git checkout 编辑器运行时噪声（动态字体资产、被运行时改写的共享材质），并更新依赖「字库样式数」的守卫测试。");
            return sb.ToString();
        }

        static Material CloneIfMissing(string srcPath, string dstPath)
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(dstPath) == null) AssetDatabase.CopyAsset(srcPath, dstPath);
            return AssetDatabase.LoadAssetAtPath<Material>(dstPath);
        }

        static Dictionary<string, List<NodeDef>> GroupByPrefab(List<NodeDef> nodes)
        {
            var d = new Dictionary<string, List<NodeDef>>();
            foreach (var n in nodes) { if (!d.TryGetValue(n.prefab, out var l)) d[n.prefab] = l = new List<NodeDef>(); l.Add(n); }
            return d;
        }
    }
}
