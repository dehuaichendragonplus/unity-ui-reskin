// 用途（Play 中）：列出当前激活的本地化文字组件及其多语言 id / key、TMP 实际文字（references/review.md §4 第 4 项）。
// id 未设置、显示值又等于 prefab 占位（如 1234、"Large Bundle"）= 占位写回问题。用 FILTER 只看某个界面（节点路径包含的字符串）。
// 组件名与 id 字段取项目事实 .uireskin/facts.json 的 localization.component / idField / keyField；没有事实时列出名字含 Locali[sz]e 的组件候选。
// 运行：execute_code，skip_refresh=true（Play 中别触发刷新）。
using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class RuntimeLocPlaceholder
{
    const string FILTER = "";   // 例如 "UIShop"；空 = 全部
    [Serializable] public class LF { public string component, idField, keyField; }
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
    static string P(Transform t) { var s = new List<string>(); while (t != null) { s.Add(t.name); t = t.parent; } s.Reverse(); return string.Join("/", s); }
    public static string Run()
    {
        if (!Application.isPlaying) return "需要 Play 模式";
        var json = FactsJson();
        var lf = JsonUtility.FromJson<LF>(Section(json, "localization") ?? "{}") ?? new LF();
        var all = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        if (string.IsNullOrEmpty(lf.component))
        {
            var cands = all.Where(m => m != null && System.Text.RegularExpressions.Regex.IsMatch(m.GetType().Name, "Locali[sz]", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                .GroupBy(m => m.GetType().FullName).Select(g => $"{g.Key} ×{g.Count()}");
            return "项目事实缺 localization.component / idField。当前场景里的本地化组件候选：" + string.Join("，", cands) + "。读组件源码确认后写入事实再跑。";
        }
        var sb = new StringBuilder();
        foreach (var c in all.Where(m => m != null && m.isActiveAndEnabled && (m.GetType().Name == lf.component || m.GetType().FullName == lf.component)))
        {
            var p = P(c.transform);
            if (FILTER.Length > 0 && !p.Contains(FILTER)) continue;
            var so = new SerializedObject(c);
            string V(string field) { if (string.IsNullOrEmpty(field)) return "-"; var sp = so.FindProperty(field); return sp == null ? "?" : sp.propertyType == SerializedPropertyType.String ? sp.stringValue : sp.intValue.ToString(); }
            var tmp = c.GetComponent<TMPro.TMP_Text>();
            sb.AppendLine($"{p} id={V(lf.idField)} key={V(lf.keyField)} tmp='{tmp?.text}' font={(tmp && tmp.font ? tmp.font.name : "null")}");
        }
        return sb.Length == 0 ? $"没有匹配的激活 {lf.component}" : sb.ToString();
    }
}
