// 用途：统计「挂了本地化组件、却不是项目规范字体」的文字节点（references/review.md §6.2）。直接修复用 LocFontFix.cs（同一套判定与映射）。
// 组件名 / id 字段 / 规范字体 / 标题字体 / 材质映射全部取项目事实 .uireskin/facts.json 的 localization 段（不内置任何项目的名字）；缺项时返回需要调研的项。
// 每行给出：prefab、节点路径、多语言 id、当前字体 / 材质、建议字体、建议材质（materialMap：美术材质去掉字体名前缀后的样式名 → 预设材质名），
// 以及换皮前（OLD）的状态，用来区分"本轮改坏 / 本轮新挂 / 原有"；也列出"动态文本误挂本地化组件（id 未设置）"。
// 前提：Assets/_TmpUIAuditOld/（换皮前）已导出；ART 指向美术版副本或直接用 "Assets/"（取美术用的材质名）。只读。
using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using TMPro;

public class LocFontReport
{
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

    public static string Run()
    {
        var need = LoadFacts(); if (need != null) return need;
        string normalName = lf.requiredFonts[0], titleName = string.IsNullOrEmpty(lf.titleFont) ? normalName : lf.titleFont;
        var sb = new StringBuilder(); var cnt = new Dictionary<string, int>(); var missing = new HashSet<string>();
        void Count(string k) => cnt[k] = cnt.TryGetValue(k, out var c) ? c + 1 : 1;
        foreach (var rel in AssetDatabase.FindAssets("t:Prefab", new[] { OLD.TrimEnd('/') }).Select(AssetDatabase.GUIDToAssetPath).Select(p => p.Substring(OLD.Length)).OrderBy(x => x))
        {
            var cur = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/" + rel); if (cur == null) continue;
            var om = Map(AssetDatabase.LoadAssetAtPath<GameObject>(OLD + rel));
            var am = Map(AssetDatabase.LoadAssetAtPath<GameObject>(ART + rel));
            var lines = new List<string>();
            foreach (var tmp in cur.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                // 嵌套实例里没有覆盖的节点由源 prefab 那一行负责，这里跳过，避免重复
                if (PrefabUtility.IsPartOfPrefabInstance(tmp) && !PrefabUtility.GetPropertyModifications(cur)?.Any(m => m.target == PrefabUtility.GetCorrespondingObjectFromSource(tmp)) == true) continue;
                var loc = Loc(tmp); if (loc == null) continue;
                var path = P(tmp.transform, cur.transform);
                om.TryGetValue(path, out var ot); var otmp = ot ? ot.GetComponent<TextMeshProUGUI>() : null; var oloc = otmp ? Loc(otmp) : null;
                if (otmp != null && oloc == null && Unset(LocId(loc)) && !IsNormal(tmp.font))
                { Count("动态文本误挂"); lines.Add($"| `{path}` | 0 | {N(tmp.font)} / {N(tmp.fontSharedMaterial)} | 删掉 {lf.component}（纯动态文本不需要），字体可保持 | 本轮新挂 |"); continue; }
                if (IsNormal(tmp.font)) continue;
                string status = otmp == null ? "本轮新节点" : (IsNormal(otmp.font) ? "本轮改坏" : (oloc != null ? "原有" : "本轮新挂"));
                Count(status);
                am.TryGetValue(path, out var at); var src = (at ? at.GetComponent<TextMeshProUGUI>() : null) ?? tmp;
                var mname = N(src.fontSharedMaterial); var fname = N(src.font);
                var suffix = mname.StartsWith(fname) ? mname.Substring(fname.Length).Trim() : mname;
                string sug;
                if (MatMap.TryGetValue(suffix, out var target)) sug = target == "" ? "默认材质" : target;
                else { sug = $"缺预设（原材质 {mname}）"; missing.Add(mname); }
                var font = otmp != null && N(otmp.font) == titleName ? titleName : normalName;
                lines.Add($"| `{path}` | {LocId(loc)} | {N(tmp.font)} / {N(tmp.fontSharedMaterial)} | 字体 → {font}，材质 → {sug} | {status} |");
            }
            if (lines.Count == 0) continue;
            sb.AppendLine($"\n### {rel}\n\n| 节点 | 多语言 id | 当前字体 / 材质 | 建议修改 | 状态 |\n|---|---|---|---|---|");
            foreach (var l in lines) sb.AppendLine(l);
        }
        var head = $"## 挂 {lf.component} 的字体问题清单（规范字体 {string.Join(" / ", lf.requiredFonts)}）\n\n汇总：" + "" + string.Join("，", cnt.Select(kv => kv.Key + " " + kv.Value)) + "\n";
        if (missing.Count > 0) head += "\n项目事实 materialMap 里没有映射的美术材质（修复时会用默认材质；确认后补进 materialMap）：" + string.Join("、", missing) + "\n";
        return cnt.Count == 0 ? $"没有发现挂 {lf.component} 却不是规范字体的节点" : head + sb;
    }
}
