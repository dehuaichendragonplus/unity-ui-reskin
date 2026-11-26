// 用途：LocalizationText / LocalizeTextMeshProUGUI / TMTextWrap 的新挂、去掉、参数变化（多语言 id、Term、样式名），新旧对比（references/review.md §3 第 5 项）。
// 前提：Assets/_TmpUIAuditOld/ 已导出。只读。注意：别在这里读 TMP.text 或比较字体，会触发大字体加载导致超时。
using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class LocCompDiff
{
    const string OLD = "Assets/_TmpUIAuditOld/";
    static readonly HashSet<string> Names = new HashSet<string> { "LocalizationText", "LocalizeTextMeshProUGUI", "TMTextWrap" };
    static string P(Transform t, Transform r) { var s = new List<string>(); while (t != null && t != r) { s.Add(t.name); t = t.parent; } s.Reverse(); return string.Join("/", s); }
    static string Desc(Component c)
    {
        var so = new SerializedObject(c); var tn = c.GetType().Name;
        if (tn == "LocalizationText") return $"LocText(id={so.FindProperty("m_LocalizationID")?.intValue})";
        if (tn == "LocalizeTextMeshProUGUI") return $"LocTMP(term='{so.FindProperty("Term")?.stringValue}',mat='{so.FindProperty("m_Matrial")?.stringValue}')";
        return tn;
    }
    public static string Run()
    {
        var sb = new StringBuilder();
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
                if (!om.TryGetValue(p, out var ot)) continue;
                var ncs = t.GetComponents<MonoBehaviour>().Where(c => c != null && Names.Contains(c.GetType().Name)).ToList();
                var ocs = ot.GetComponents<MonoBehaviour>().Where(c => c != null && Names.Contains(c.GetType().Name)).ToList();
                foreach (var nc in ncs) { var oc = ocs.FirstOrDefault(x => x.GetType() == nc.GetType()); if (oc == null) lines.Add($"  + {Desc(nc)} {p}"); else if (Desc(oc) != Desc(nc)) lines.Add($"  ~ {Desc(oc)} -> {Desc(nc)} {p}"); }
                foreach (var oc in ocs) if (!ncs.Any(x => x.GetType() == oc.GetType())) lines.Add($"  - {Desc(oc)} {p}");
            }
            if (lines.Count > 0) { sb.AppendLine("## " + rel); foreach (var l in lines) sb.AppendLine(l); }
        }
        return sb.ToString();
    }
}
