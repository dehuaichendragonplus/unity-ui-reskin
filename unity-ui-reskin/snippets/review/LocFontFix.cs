// 用途：直接修复「挂了本地化组件、却不是项目规范字体」的文字节点（references/review.md §6.2）。判定与材质映射同 LocFontReport.cs。
// 组件名 / id 字段 / 规范字体 / 标题字体 / 材质映射全部取项目事实 .uireskin/facts.json 的 localization 段；缺项时返回需要调研的项，不改任何东西。
// 做什么（只处理本轮改动过的 prefab = Assets/_TmpUIAuditOld/ 里出现的那批）：
//   1. 字体 → requiredFonts[0]（换皮前是 titleFont 的保持 titleFont）
//   2. 材质 → 美术版材质去掉字体名前缀后的样式名，按 materialMap 映射到预设材质（保留描边 / 阴影）；没有映射或预设不存在 → 字体默认材质，结果里列出
//   3. 动态文本上新挂的本地化组件（id 未设置，换皮前该节点没有这个组件）→ 删掉组件
//   不改"原有"（换皮前就不合规）的节点，除非 FIX_PRE = true。
// 用法：先 DRY = true 跑看计划，确认后 DRY = false；嵌套 prefab 先内后外，重复运行到「本轮零改动」。返回值是 Markdown 修改记录。
using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using TMPro;

public class LocFontFix
{
    const bool DRY = true;
    const bool FIX_PRE = false;
    const string OLD = "Assets/_TmpUIAuditOld/";
    const string ART = "Assets/";
    [Serializable] public class LF { public string component, idField, titleFont; public string[] requiredFonts; }
    static LF lf; static Dictionary<string, string> MatMap;
    // funplay 片段编译在内存程序集里，JsonUtility 只认顶层字段：先截出某一段再解析成扁平类
    static string Section(string j, string key)
    {
        if (j == null) return null;
        int k = j.IndexOf("\"" + key + "\"", StringComparison.Ordinal); if (k < 0) return null;
        int i = j.IndexOf('{', k); if (i < 0) return null;
        int depth = 0; bool str = false;
        for (int p = i; p < j.Length; p++)
        {
            char c = j[p];
            if (str) { if (c == '\\') p++; else if (c == '"') str = false; continue; }
            if (c == '"') str = true; else if (c == '{') depth++; else if (c == '}' && --depth == 0) return j.Substring(i, p - i + 1);
        }
        return null;
    }
    static string FactsJson()
    {
        var env = Environment.GetEnvironmentVariable("UIRESKIN_FACTS");
        if (!string.IsNullOrEmpty(env) && System.IO.File.Exists(env)) return System.IO.File.ReadAllText(env);
        var d = new System.IO.DirectoryInfo(System.IO.Path.GetDirectoryName(Application.dataPath));
        for (int i = 0; d != null && i < 6; i++, d = d.Parent) { var p = System.IO.Path.Combine(d.FullName, ".uireskin", "facts.json"); if (System.IO.File.Exists(p)) return System.IO.File.ReadAllText(p); }
        return null;
    }
    /// 读项目事实 localization 段；缺项返回说明（null = 齐全）
    static string LoadFacts()
    {
        var json = FactsJson();
        var sec = Section(json, "localization");
        lf = JsonUtility.FromJson<LF>(sec ?? "{}") ?? new LF();
        var miss = new List<string>();
        if (string.IsNullOrEmpty(lf.component)) miss.Add("localization.component（本地化文字组件类名）");
        if (lf.requiredFonts == null || lf.requiredFonts.Length == 0) miss.Add("localization.requiredFonts（挂该组件的文字必须用的字体）");
        MatMap = new Dictionary<string, string>();
        // materialMap 是对象数组，片段里 JsonUtility 读不了：用正则取 {"from": "...", "to": "..."}（两字段顺序不限）
        if (sec != null)
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(sec, @"\{[^{}]*\}"))
            {
                var f = System.Text.RegularExpressions.Regex.Match(m.Value, @"""from""\s*:\s*""([^""]*)"""); var t = System.Text.RegularExpressions.Regex.Match(m.Value, @"""to""\s*:\s*""([^""]*)""");
                if (f.Success) MatMap[f.Groups[1].Value] = t.Success ? t.Groups[1].Value : "";
            }
        if (miss.Count == 0) return null;
        return (json == null ? "没有项目事实 .uireskin/facts.json。" : "") + "项目事实缺：" + string.Join("、", miss) +
               "。先调研（uir_project.py info 列本地化组件候选；读组件源码看它在哪些字体上做语言回退 / 切换）后写入；项目没有「本地化组件 + 规范字体」约束时本项不适用。";
    }
    static bool IsNormal(TMP_FontAsset f) => f != null && lf.requiredFonts.Contains(f.name);
    static MonoBehaviour Loc(Component c) => c.GetComponents<MonoBehaviour>().FirstOrDefault(m => m != null && (m.GetType().Name == lf.component || m.GetType().FullName == lf.component));
    static string LocId(MonoBehaviour m)
    {
        if (string.IsNullOrEmpty(lf.idField)) return "-";
        var p = new SerializedObject(m).FindProperty(lf.idField);
        return p == null ? "-" : p.propertyType == SerializedPropertyType.String ? p.stringValue : p.intValue.ToString();
    }
    static bool Unset(string id) => id == "0" || id == "" ;
    static string P(Transform t, Transform r) { var s = new List<string>(); while (t != null && t != r) { s.Add(t.name); t = t.parent; } s.Reverse(); return string.Join("/", s); }
    static Dictionary<string, Transform> Map(GameObject g) { var d = new Dictionary<string, Transform>(); if (g) foreach (var t in g.GetComponentsInChildren<Transform>(true)) { var k = P(t, g.transform); if (!d.ContainsKey(k)) d[k] = t; } return d; }
    static string N(UnityEngine.Object o) => o ? o.name : "null";
    static T Exact<T>(string name, string type) where T : UnityEngine.Object
    {
        foreach (var g in AssetDatabase.FindAssets($"\"{name}\" t:{type}"))
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(g));
            if (a != null && a.name == name) return a;
        }
        return null;
    }

    public static string Run()
    {
        var need = LoadFacts(); if (need != null) return need;
        var normal = Exact<TMP_FontAsset>(lf.requiredFonts[0], "TMP_FontAsset");
        if (normal == null) return $"项目事实里的规范字体 {lf.requiredFonts[0]} 在工程里找不到，先核对事实";
        var title = (string.IsNullOrEmpty(lf.titleFont) ? null : Exact<TMP_FontAsset>(lf.titleFont, "TMP_FontAsset")) ?? normal;
        var sb = new StringBuilder(); var cnt = new Dictionary<string, int>(); var missing = new HashSet<string>(); int skippedNested = 0;
        void Count(string k) => cnt[k] = cnt.TryGetValue(k, out var c) ? c + 1 : 1;
        foreach (var rel in AssetDatabase.FindAssets("t:Prefab", new[] { OLD.TrimEnd('/') }).Select(AssetDatabase.GUIDToAssetPath).Select(p => p.Substring(OLD.Length)).OrderBy(x => x))
        {
            var path = "Assets/" + rel;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;
            var om = Map(AssetDatabase.LoadAssetAtPath<GameObject>(OLD + rel));
            var am = Map(AssetDatabase.LoadAssetAtPath<GameObject>(ART + rel));
            var root = PrefabUtility.LoadPrefabContents(path);
            var lines = new List<string>(); bool dirty = false;
            try
            {
                foreach (var tmp in root.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    var loc = Loc(tmp); if (loc == null) continue;
                    var np = P(tmp.transform, root.transform);
                    om.TryGetValue(np, out var ot); var otmp = ot ? ot.GetComponent<TextMeshProUGUI>() : null; var oloc = otmp ? Loc(otmp) : null;
                    bool nested = PrefabUtility.IsPartOfPrefabInstance(tmp);
                    // 3) 动态文本误挂本地化组件（id 未设置）：删组件
                    if (otmp != null && oloc == null && Unset(LocId(loc)) && !IsNormal(tmp.font))
                    {
                        if (nested || PrefabUtility.IsPartOfPrefabInstance(loc)) { skippedNested++; continue; }   // 嵌套实例里的组件在源 prefab 里删
                        Count("删误挂组件"); lines.Add($"| `{np}` | 0 | 删掉 {lf.component}（纯动态文本），字体保持 {N(tmp.font)} | 本轮新挂 |");
                        if (!DRY) { UnityEngine.Object.DestroyImmediate(loc, true); dirty = true; }
                        continue;
                    }
                    if (IsNormal(tmp.font)) continue;
                    if (nested) { skippedNested++; continue; }   // 嵌套实例节点在源 prefab 里改，外层不产生覆盖
                    string status = otmp == null ? "本轮新节点" : (IsNormal(otmp.font) ? "本轮改坏" : (oloc != null ? "原有" : "本轮新挂"));
                    if (status == "原有" && !FIX_PRE) { Count("原有（未改）"); continue; }
                    am.TryGetValue(np, out var at); var src = (at ? at.GetComponent<TextMeshProUGUI>() : null) ?? tmp;
                    var mname = N(src.fontSharedMaterial); var fname = N(src.font);
                    var suffix = mname.StartsWith(fname) ? mname.Substring(fname.Length).Trim() : mname;
                    var font = otmp != null && N(otmp.font) == title.name ? title : normal;
                    Material mat = null; string matDesc;
                    if (MatMap.TryGetValue(suffix, out var target) && target != "")
                    {
                        mat = Exact<Material>(target, "Material");
                        if (mat == null) { missing.Add(target + "（资产不存在）"); matDesc = $"默认材质（预设 {target} 不存在）"; }
                        else matDesc = target;
                    }
                    else if (MatMap.ContainsKey(suffix)) matDesc = "默认材质";
                    else { missing.Add(mname); matDesc = $"默认材质（原材质 {mname} 无对应预设，效果会丢）"; }
                    Count(status);
                    lines.Add($"| `{np}` | {LocId(loc)} | {N(tmp.font)} / {N(tmp.fontSharedMaterial)} → {font.name} / {matDesc} | {status} |");
                    if (!DRY)
                    {
                        tmp.font = font;
                        tmp.fontSharedMaterial = mat != null ? mat : font.material;
                        EditorUtility.SetDirty(tmp); dirty = true;
                    }
                }
                if (!DRY && dirty) PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            if (lines.Count == 0) continue;
            sb.AppendLine($"\n### {rel}\n\n| 节点 | 多语言 id | 修改 | 状态 |\n|---|---|---|---|");
            foreach (var l in lines) sb.AppendLine(l);
        }
        if (cnt.Count == 0) return "没有需要修复的节点（本轮零改动）" + (skippedNested > 0 ? $"；{skippedNested} 处在嵌套实例里，改源 prefab" : "");
        var head = $"## 字体修复记录（{(DRY ? "DRY 预演，未写入" : "已由程序修改")}）\n\n汇总：" + string.Join("，", cnt.Select(kv => kv.Key + " " + kv.Value)) + "\n";
        if (skippedNested > 0) head += $"\n嵌套实例里的 {skippedNested} 处跳过（在源 prefab 里改；源 prefab 不在本轮改动里时，把它加进 _TmpUIAuditOld 或单独改）；写入后重跑到零改动。\n";
        if (missing.Count > 0) head += "\n没有映射 / 预设不存在、已用默认材质（效果会变；确认后补进项目事实 materialMap 或请美术补预设）：" + string.Join("、", missing) + "\n";
        return head + sb;
    }
}
