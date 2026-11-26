// 用途：修复后对比"美术版 vs 当前"每个字体有变化的 TMP 节点的效果类型（描边 / 阴影 / 发光），列出不一致项交美术确认（references/review.md §6.2、§6.3）。
// 前提：Assets/_TmpUIAuditArt/ 已导出美术版。只读。
using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using TMPro;

public class FxCompare
{
    const string ART = "Assets/_TmpUIAuditArt/";
    static string P(Transform t, Transform r) { var s = new List<string>(); while (t != null && t != r) { s.Add(t.name); t = t.parent; } s.Reverse(); return string.Join("/", s); }
    static string Fx(Material m)
    {
        if (m == null) return "none";
        var s = new List<string>();
        if (m.IsKeywordEnabled("OUTLINE_ON")) s.Add("描边");
        if (m.IsKeywordEnabled("UNDERLAY_ON") || m.IsKeywordEnabled("UNDERLAY_INNER")) s.Add("阴影");
        if (m.IsKeywordEnabled("GLOW_ON")) s.Add("发光");
        return s.Count == 0 ? "无" : string.Join("+", s);
    }
    public static string Run()
    {
        int total = 0, same = 0; var sb = new StringBuilder(); var pairs = new Dictionary<string, int>();
        foreach (var ap in AssetDatabase.FindAssets("t:Prefab", new[] { ART.TrimEnd('/') }).Select(AssetDatabase.GUIDToAssetPath))
        {
            var rel = ap.Substring(ART.Length);
            var a = AssetDatabase.LoadAssetAtPath<GameObject>(ap); var n = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/" + rel);
            if (n == null) continue;
            var am = new Dictionary<string, Transform>(); foreach (var t in a.GetComponentsInChildren<Transform>(true)) { var k = P(t, a.transform); if (!am.ContainsKey(k)) am[k] = t; }
            foreach (var tmp in n.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (!am.TryGetValue(P(tmp.transform, n.transform), out var at)) continue;
                var atmp = at.GetComponent<TextMeshProUGUI>(); if (atmp == null || atmp.font == tmp.font) continue;
                total++;
                var am2 = atmp.fontSharedMaterial ? atmp.fontSharedMaterial : atmp.font?.material;
                var fa = Fx(am2); var fn = Fx(tmp.fontSharedMaterial);
                var key = $"{(am2 ? am2.name : "null")}【{fa}】 -> {(tmp.fontSharedMaterial ? tmp.fontSharedMaterial.name : "null")}【{fn}】";
                pairs[key] = pairs.TryGetValue(key, out var c) ? c + 1 : 1;
                if (fa == fn) same++; else sb.AppendLine($"  ≠ {rel}:{P(tmp.transform, n.transform)}  {key}");
            }
        }
        var head = new StringBuilder($"字体与美术版不同的节点 {total}，效果类型一致 {same}，不一致 {total - same}\n");
        foreach (var kv in pairs.OrderByDescending(x => x.Value)) head.AppendLine($"  x{kv.Value} {kv.Key}");
        return head + "-- 不一致明细：\n" + sb;
    }
}
