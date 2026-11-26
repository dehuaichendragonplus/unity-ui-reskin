// 用途：改动过的 prefab 逐节点新旧对比（references/review.md §3 第 3、4、7、8 项）。
// 输出：REMOVED / ADDED（只报最上层）、COMP 组件增减、ACTIVE 默认激活翻转、REFNULL 项目脚本引用变空、BROKENREF 新版才有的断引用、NEWDUPNAME 新出现的同名兄弟。
// 末行 REMOVED_NAMES 是删除节点名，拿去 grep 代码。前提：Assets/_TmpUIAuditOld/ 已导出。只读。
using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class HierDiff
{
    const string OLD = "Assets/_TmpUIAuditOld/";
    static string Path(Transform t, Transform root) { var s = new List<string>(); while (t != null && t != root) { s.Add(t.name); t = t.parent; } s.Reverse(); return string.Join("/", s); }
    static Dictionary<string, Transform> Map(GameObject go, List<string> dups)
    {
        var d = new Dictionary<string, Transform>();
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) { var p = Path(t, go.transform); if (d.ContainsKey(p)) dups.Add(p); else d[p] = t; }
        return d;
    }
    static IEnumerable<string> CompNames(Transform t) => t.GetComponents<Component>().Select(c => c == null ? "MISSING_SCRIPT" : c.GetType().Name);
    static bool IsProject(Component c) => c is MonoBehaviour && (c.GetType().Namespace == null || (!c.GetType().Namespace.StartsWith("UnityEngine") && !c.GetType().Namespace.StartsWith("TMPro")));
    static HashSet<string> Broken(Dictionary<string, Transform> m)
    {
        var r = new HashSet<string>();
        foreach (var t in m)
            foreach (var c in t.Value.GetComponents<Component>())
            {
                if (c == null) { r.Add("MISSING_SCRIPT " + t.Key); continue; }
                var it = new SerializedObject(c).GetIterator();
                while (it.NextVisible(true))
                    if (it.propertyType == SerializedPropertyType.ObjectReference && it.objectReferenceValue == null && it.objectReferenceInstanceIDValue != 0)
                        r.Add($"{t.Key} {c.GetType().Name}.{it.propertyPath}");
            }
        return r;
    }

    public static string Run()
    {
        var sb = new StringBuilder();
        var removedNames = new HashSet<string>();
        foreach (var op in AssetDatabase.FindAssets("t:Prefab", new[] { OLD.TrimEnd('/') }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(x => x))
        {
            var rel = op.Substring(OLD.Length);
            var o = AssetDatabase.LoadAssetAtPath<GameObject>(op); var n = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/" + rel);
            if (n == null) { sb.AppendLine("## " + rel + " 新版不存在"); continue; }
            var od = new List<string>(); var nd = new List<string>();
            var om = Map(o, od); var nm = Map(n, nd);
            var removed = om.Keys.Where(k => !nm.ContainsKey(k)).ToList();
            var added = nm.Keys.Where(k => !om.ContainsKey(k)).ToList();
            var lines = new List<string>();
            foreach (var r in removed.Where(r => !removed.Any(x => x != r && r.StartsWith(x + "/"))))
            {
                lines.Add($"  - REMOVED {r}  [{string.Join(",", CompNames(om[r]).Where(x => x != "RectTransform" && x != "CanvasRenderer"))}] (+{removed.Count(x => x.StartsWith(r + "/"))})");
                removedNames.Add(om[r].name);
            }
            foreach (var a in added.Where(a => !added.Any(x => x != a && a.StartsWith(x + "/"))))
                lines.Add($"  + ADDED {a} (+{added.Count(x => x.StartsWith(a + "/"))})");
            foreach (var k in om.Keys.Where(nm.ContainsKey))
            {
                var oc = CompNames(om[k]).ToList(); var nc = CompNames(nm[k]).ToList();
                var rc = oc.Except(nc).ToList(); var ac = nc.Except(oc).ToList();
                if (rc.Count > 0 || ac.Count > 0) lines.Add($"  ~ COMP {k}: -[{string.Join(",", rc)}] +[{string.Join(",", ac)}]");
                if (om[k].gameObject.activeSelf != nm[k].gameObject.activeSelf) lines.Add($"  ~ ACTIVE {k}: {om[k].gameObject.activeSelf}->{nm[k].gameObject.activeSelf}");
                foreach (var c2 in nm[k].GetComponents<Component>())
                {
                    if (c2 == null || !IsProject(c2)) continue;
                    var oc2 = om[k].GetComponent(c2.GetType()); if (oc2 == null) continue;
                    var so = new SerializedObject(oc2); var sn = new SerializedObject(c2); var it = so.GetIterator();
                    while (it.NextVisible(true))
                    {
                        if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                        var np2 = sn.FindProperty(it.propertyPath);
                        if (it.objectReferenceValue != null && (np2 == null || np2.objectReferenceValue == null))
                            lines.Add($"  ! REFNULL {k} {c2.GetType().Name}.{it.propertyPath} (old={it.objectReferenceValue.name})");
                    }
                }
            }
            foreach (var b in Broken(nm).Except(Broken(om))) lines.Add("  ! BROKENREF " + b);
            foreach (var dp in nd.Distinct()) if (!od.Contains(dp)) lines.Add("  ! NEWDUPNAME " + dp);
            if (lines.Count > 0) { sb.AppendLine($"## {rel} (old {om.Count}, new {nm.Count})"); foreach (var l in lines) sb.AppendLine(l); }
        }
        sb.AppendLine("REMOVED_NAMES: " + string.Join("|", removedNames.OrderBy(x => x)));
        return sb.ToString();
    }
}
