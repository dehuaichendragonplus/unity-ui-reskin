// 用途：统计"挂了 LocalizationText 却不是 TMP_Font_Normal(_Title)"的文字节点（references/review.md §6.2）。直接修复用 LocFontFix.cs（同一套判定与映射）。
// 适用：DragonCD3 宿主多语言体系（LocalizationText 只在 TMP_Font_Normal / _Title 上挂语言回退）。工程里没有这两个字体时直接返回「不适用」。
// 只读，不改任何 prefab：用于修复前看全貌、修复后复核（应只剩"原有"项）。
// 每行给出：prefab、节点路径、多语言 id、当前字体 / 材质、建议字体、建议材质（按美术维护的预设映射表 MatMap），
// 以及换皮前（OLD）的状态，用来区分"本轮改坏 / 本轮新挂 / 原有"。也会列出"动态文本误挂 LocalizationText(id=0)"建议美术删组件。
// 前提：Assets/_TmpUIAuditOld/（换皮前）已导出；ART 指向美术版副本或直接用 "Assets/"（取美术用的 LocaleFont 材质名）。
// 输出按 prefab 分组，可直接贴进审查文档（也可用 scripts/prefab_audit.py locfont --appendix 出 git 侧版本）。
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
    // 美术用的 LocaleFont_En SDF <X> -> 建议的 TMP_Font_Normal 预设（美术维护；新增 3<X> 预设时同步补这里）。"" = 字体默认材质。
    static readonly Dictionary<string, string> MatMap = new Dictionary<string, string>
    {
        { "ButtonGreen", "TMP_Font_Normal 3ButtonGreen" }, { "ButtonBlue", "TMP_Font_Normal 3ButtonBlue" },
        { "ButtonOrange", "TMP_Font_Normal 3ButtonOrange" }, { "ButtonRed", "TMP_Font_Normal 3ButtonRed" },
        { "ButtonGray", "TMP_Font_Normal 3ButtonGray" }, { "Shadow Orange", "TMP_Font_Normal 3Shadow Orange" },
        { "Shadow Blue", "TMP_Font_Normal 3Shadow Blue" }, { "Shadow Purple", "TMP_Font_Normal 3Shadow Purple" },
        { "Shadow Red", "TMP_Font_Normal 3Shadow Red" },
        { "Shadow", "TMP_Font_Normal OutLine Black" }, { "Describe", "TMP_Font_Normal OutLine Black" }, { "Title", "TMP_Font_Normal OutLine Black" },
        { "Button", "" }, { "Shadow Small", "" }, { "Outline", "" }, { "Material", "" }, { "", "" },
    };
    static string P(Transform t, Transform r) { var s = new List<string>(); while (t != null && t != r) { s.Add(t.name); t = t.parent; } s.Reverse(); return string.Join("/", s); }
    static bool IsNormal(TMP_FontAsset f) => f != null && (f.name == "TMP_Font_Normal" || f.name == "TMP_Font_Normal_Title");
    static MonoBehaviour Loc(Component c) => c.GetComponents<MonoBehaviour>().FirstOrDefault(m => m != null && m.GetType().Name == "LocalizationText");
    static int LocId(MonoBehaviour m) => new SerializedObject(m).FindProperty("m_LocalizationID").intValue;
    static Dictionary<string, Transform> Map(GameObject g) { var d = new Dictionary<string, Transform>(); if (g) foreach (var t in g.GetComponentsInChildren<Transform>(true)) { var k = P(t, g.transform); if (!d.ContainsKey(k)) d[k] = t; } return d; }
    static string N(UnityEngine.Object o) => o ? o.name : "null";

    public static string Run()
    {
        if (AssetDatabase.FindAssets("TMP_Font_Normal t:TMP_FontAsset").Length == 0) return "工程里没有 TMP_Font_Normal 字体，本项不适用（按项目自己的字体规范检查）";
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
                if (otmp != null && oloc == null && LocId(loc) == 0 && !IsNormal(tmp.font))
                { Count("动态文本误挂"); lines.Add($"| `{path}` | 0 | {N(tmp.font)} / {N(tmp.fontSharedMaterial)} | 删掉 LocalizationText（纯动态文本不需要），字体可保持 | 本轮新挂 |"); continue; }
                if (IsNormal(tmp.font)) continue;
                string status = otmp == null ? "本轮新节点" : (IsNormal(otmp.font) ? "本轮改坏" : (oloc != null ? "原有" : "本轮新挂"));
                Count(status);
                am.TryGetValue(path, out var at); var src = (at ? at.GetComponent<TextMeshProUGUI>() : null) ?? tmp;
                var mname = N(src.fontSharedMaterial); var fname = N(src.font);
                var suffix = mname.StartsWith(fname) ? mname.Substring(fname.Length).Trim() : mname;
                string sug;
                if (MatMap.TryGetValue(suffix, out var target)) sug = target == "" ? "默认材质" : target;
                else { sug = $"缺预设（原材质 {mname}）"; missing.Add(mname); }
                var font = otmp != null && N(otmp.font) == "TMP_Font_Normal_Title" ? "TMP_Font_Normal_Title" : "TMP_Font_Normal";
                lines.Add($"| `{path}` | {LocId(loc)} | {N(tmp.font)} / {N(tmp.fontSharedMaterial)} | 字体 → {font}，材质 → {sug} | {status} |");
            }
            if (lines.Count == 0) continue;
            sb.AppendLine($"\n### {rel}\n\n| 节点 | 多语言 id | 当前字体 / 材质 | 建议修改 | 状态 |\n|---|---|---|---|---|");
            foreach (var l in lines) sb.AppendLine(l);
        }
        var head = "## 挂 LocalizationText 的字体问题清单\n\n汇总：" + string.Join("，", cnt.Select(kv => kv.Key + " " + kv.Value)) + "\n";
        if (missing.Count > 0) head += "\n缺少对应 TMP_Font_Normal 预设的美术材质（修复时会用默认材质，告知美术补 3<X> 预设）：" + string.Join("、", missing) + "\n";
        return cnt.Count == 0 ? "没有发现挂 LocalizationText 却不是 TMP_Font_Normal 的节点" : head + sb;
    }
}
