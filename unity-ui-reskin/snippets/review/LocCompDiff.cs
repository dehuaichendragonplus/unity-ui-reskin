// 用途：本地化文字组件的新挂、去掉、参数变化（多语言 id / key / 样式字段……），新旧对比（references/review.md §3 第 5 项）。
// 比哪些组件：项目事实里的 localization.component 与 text.locTextType；没有事实时比名字含 Locali[sz]e 的组件（通用）。
// 参数：组件上所有序列化的字符串 / 整数 / 布尔字段逐个比，不写死字段名。
// 前提：Assets/_TmpUIAuditOld/ 已导出。只读。注意：别在这里读 TMP.text 或比较字体，会触发大字体加载导致超时。
using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;

public class LocCompDiff
{
    const string OLD = "Assets/_TmpUIAuditOld/";
    [Serializable] public class LF { public string component; }
    [Serializable] public class TF { public string locTextType; }
    static HashSet<string> names;
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
    static bool Match(MonoBehaviour c) => c != null && (names.Count > 0 ? names.Contains(c.GetType().Name) || names.Contains(c.GetType().FullName) : Regex.IsMatch(c.GetType().Name, "Locali[sz]", RegexOptions.IgnoreCase));
    static string P(Transform t, Transform r) { var s = new List<string>(); while (t != null && t != r) { s.Add(t.name); t = t.parent; } s.Reverse(); return string.Join("/", s); }
    static string Desc(Component c)
    {
        var so = new SerializedObject(c); var it = so.GetIterator(); var parts = new List<string>();
        for (bool enter = true; it.NextVisible(enter); enter = false)
        {
            if (it.name == "m_Script") continue;
            switch (it.propertyType)
            {
                case SerializedPropertyType.String: parts.Add($"{it.name}='{it.stringValue}'"); break;
                case SerializedPropertyType.Integer: parts.Add($"{it.name}={it.intValue}"); break;
                case SerializedPropertyType.Boolean: parts.Add($"{it.name}={it.boolValue}"); break;
            }
        }
        return $"{c.GetType().Name}({string.Join(",", parts)})";
    }
    public static string Run()
    {
        var json = FactsJson();
        var lf = JsonUtility.FromJson<LF>(Section(json, "localization") ?? "{}"); var tf = JsonUtility.FromJson<TF>(Section(json, "text") ?? "{}");
        names = new HashSet<string>(new[] { lf?.component, tf?.locTextType }.Where(x => !string.IsNullOrEmpty(x)));
        var sb = new StringBuilder(names.Count > 0 ? $"比对组件（项目事实）：{string.Join("、", names)}\n" : "项目事实没写本地化组件：按名字含 Locali[sz]e 的组件通用比对（调研后写入 localization.component）\n");
        foreach (var op in AssetDatabase.FindAssets("t:Prefab", new[] { OLD.TrimEnd('/') }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(x => x))
        {
            var rel = op.Substring(OLD.Length);
            var o = AssetDatabase.LoadAssetAtPath<GameObject>(op); var n = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/" + rel);
            if (n == null) continue;
            var om = new Dictionary<string, Transform>(); foreach (var t in o.GetComponentsInChildren<Transform>(true)) om[P(t, o.transform)] = t;
            var lines = new List<string>();
            foreach (var t in n.GetComponentsInChildren<Transform>(true))
            {
                var p = P(t, n.transform);
                var ncs = t.GetComponents<MonoBehaviour>().Where(Match).ToList();
                if (!om.TryGetValue(p, out var ot)) { foreach (var nc in ncs) lines.Add($"  + {Desc(nc)} {p}（新节点）"); continue; }
                var ocs = ot.GetComponents<MonoBehaviour>().Where(Match).ToList();
                foreach (var nc in ncs) { var oc = ocs.FirstOrDefault(x => x.GetType() == nc.GetType()); if (oc == null) lines.Add($"  + {Desc(nc)} {p}"); else if (Desc(oc) != Desc(nc)) lines.Add($"  ~ {Desc(oc)} -> {Desc(nc)} {p}"); }
                foreach (var oc in ocs) if (!ncs.Any(x => x.GetType() == oc.GetType())) lines.Add($"  - {Desc(oc)} {p}");
            }
            if (lines.Count > 0) { sb.AppendLine("## " + rel); foreach (var l in lines) sb.AppendLine(l); }
        }
        return sb.ToString();
    }
}
