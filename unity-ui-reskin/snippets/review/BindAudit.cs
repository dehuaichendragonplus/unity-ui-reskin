// 用途：界面代码里「按路径绑定节点」的新旧对比（references/review.md §3 第 1 项）：新版 prefab 才找不到的节点 / 组件 = 本轮改坏。
// 框架信息全部来自项目事实 .uireskin/facts.json 的 framework 段（不内置任何框架）：
//   viewBase             界面基类全名（例：MyGame.UI.ViewBase）
//   bindingAttribute     字段 / 方法上的绑定特性全名
//   bindingPathMember    特性上存节点路径的字段 / 属性名（或 "ctor:0" = 第 1 个构造参数）
//   bindingOptionalMember  可空标记成员（可选）；bindingComponentMember  方法绑定时的组件类型成员（可选）
//   descriptorAttribute + descriptorKeyMember  类上记录 prefab 名 / 路径的特性与成员
// 事实里没有 → 返回需要调研的项（用 UIReskin.ReskinSurvey.Run() 找基类与绑定特性，读特性源码确认成员名后写入）。
// 前提：scripts/export_snapshot.py 已把旧版导到 Assets/_TmpUIAuditOld/。只读，可加 skip_refresh=true。
// 输出：[REGRESS] 本轮新引入，[PRE] 原来就有，[OLDONLY] 本轮反而修好了。
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class BindAudit
{
    const string OLD = "Assets/_TmpUIAuditOld/";
    [Serializable] public class FW { public string viewBase, bindingAttribute, bindingPathMember, bindingOptionalMember, bindingComponentMember, descriptorAttribute, descriptorKeyMember; }
    static Type viewT, bindT, descT; static FW fw;

    static string FactsJson()
    {
        var env = Environment.GetEnvironmentVariable("UIRESKIN_FACTS");
        if (!string.IsNullOrEmpty(env) && File.Exists(env)) return File.ReadAllText(env);
        var d = new DirectoryInfo(System.IO.Path.GetDirectoryName(Application.dataPath));
        for (int i = 0; d != null && i < 6; i++, d = d.Parent) { var p = System.IO.Path.Combine(d.FullName, ".uireskin", "facts.json"); if (File.Exists(p)) return File.ReadAllText(p); }
        return null;
    }
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
    static Type FindType(string n) { if (string.IsNullOrEmpty(n)) return null; foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { var t = a.GetType(n); if (t != null) return t; } return null; }
    static object Member(object attr, Attribute raw, MemberInfo owner, string member)
    {
        if (string.IsNullOrEmpty(member) || attr == null) return null;
        if (member.StartsWith("ctor:"))
        {
            int idx = int.Parse(member.Substring(5));
            var data = owner.GetCustomAttributesData().FirstOrDefault(x => x.AttributeType == attr.GetType());
            return data != null && idx < data.ConstructorArguments.Count ? data.ConstructorArguments[idx].Value : null;
        }
        var t = attr.GetType(); const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return t.GetField(member, F)?.GetValue(attr) ?? t.GetProperty(member, F)?.GetValue(attr);
    }

    public static string Run()
    {
        var json = FactsJson();
        fw = JsonUtility.FromJson<FW>(Section(json, "framework") ?? "{}") ?? new FW();
        viewT = FindType(fw.viewBase); bindT = FindType(fw.bindingAttribute); descT = FindType(fw.descriptorAttribute);
        var miss = new List<string>();
        if (viewT == null) miss.Add("framework.viewBase"); if (bindT == null) miss.Add("framework.bindingAttribute");
        if (string.IsNullOrEmpty(fw.bindingPathMember)) miss.Add("framework.bindingPathMember");
        if (descT == null || string.IsNullOrEmpty(fw.descriptorKeyMember)) miss.Add("framework.descriptorAttribute / descriptorKeyMember");
        if (miss.Count > 0) return (json == null ? "没有项目事实 .uireskin/facts.json。" : "") + "项目事实缺：" + string.Join("、", miss) +
            "。先调研（UIReskin.ReskinSurvey.Run() 列出界面基类与绑定特性；读特性源码确认路径成员名）后写入事实再跑；项目不是「特性按路径绑定」的框架时，本项改为 grep 代码里的 Find / GetComponent 路径 + HierDiff 的删除节点名。";
        var olds = AssetDatabase.FindAssets("t:Prefab", new[] { OLD.TrimEnd('/') }).Select(AssetDatabase.GUIDToAssetPath).ToList();
        var all = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath).Where(p => !p.StartsWith("Assets/_Tmp")).ToList();
        var outp = new StringBuilder($"旧版副本 {olds.Count} 个\n");
        foreach (var vt in TypeCache.GetTypesDerivedFrom(viewT).Where(t => !t.IsAbstract))
        {
            var desc = vt.GetCustomAttributes(descT, true).FirstOrDefault();
            var key = Member(desc, null, vt, fw.descriptorKeyMember) as string;
            if (string.IsNullOrEmpty(key)) continue;
            // prefab 键可能是文件名，也可能是相对路径
            var newPath = key.Contains("/")
                ? all.FirstOrDefault(p => p.ToLowerInvariant().EndsWith("/" + key.ToLowerInvariant().TrimStart('/')) || p.ToLowerInvariant().EndsWith("/" + key.ToLowerInvariant().TrimStart('/') + ".prefab"))
                : all.FirstOrDefault(p => System.IO.Path.GetFileNameWithoutExtension(p) == key);
            if (newPath == null) continue;
            var rel = newPath.Substring("Assets/".Length);
            var oldPath = olds.FirstOrDefault(p => p.Substring(OLD.Length) == rel);
            if (oldPath == null) continue;   // 本轮没改这个 prefab
            var nRes = new List<string>(); var oRes = new List<string>();
            Check(vt, AssetDatabase.LoadAssetAtPath<GameObject>(newPath).transform, "", 0, nRes);
            Check(vt, AssetDatabase.LoadAssetAtPath<GameObject>(oldPath).transform, "", 0, oRes);
            outp.AppendLine($"== {vt.Name} @ {rel}  new-fail={nRes.Count} old-fail={oRes.Count}");
            foreach (var r in nRes.Except(oRes)) outp.AppendLine("  [REGRESS] " + r);
            foreach (var r in nRes.Intersect(oRes)) outp.AppendLine("  [PRE] " + r);
            foreach (var r in oRes.Except(nRes)) outp.AppendLine("  [OLDONLY] " + r);
        }
        return outp.ToString();
    }

    static void Check(Type vt, Transform root, string ctx, int depth, List<string> res)
    {
        if (depth > 6) return;
        const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (var t = vt; t != null && t != typeof(object); t = t.BaseType)
        {
            foreach (var f in t.GetFields(F))
            {
                var b = f.GetCustomAttribute(bindT); if (b == null) continue;
                var p = Member(b, b, f, fw.bindingPathMember) as string;
                var cn = Member(b, b, f, fw.bindingOptionalMember) is bool ob && ob ? " (可空)" : "";
                var tr = string.IsNullOrEmpty(p) ? root : root.Find(p);
                if (tr == null) { res.Add($"MISS {ctx}{t.Name}.{f.Name} '{p}'{cn}"); continue; }
                var ft = f.FieldType;
                if (ft == typeof(GameObject) || ft == typeof(Transform) || ft == typeof(RectTransform)) continue;
                if (viewT.IsAssignableFrom(ft)) { Check(ft, tr, ctx + ft.Name + "[" + p + "]>", depth + 1, res); continue; }
                if (typeof(Component).IsAssignableFrom(ft) && tr.GetComponent(ft) == null) res.Add($"NOCOMP {ctx}{t.Name}.{f.Name} '{p}' need {ft.Name}{cn}");
            }
            foreach (var m in t.GetMethods(F))
            {
                var b = m.GetCustomAttribute(bindT); if (b == null) continue;
                var p = Member(b, b, m, fw.bindingPathMember) as string;
                var ct = Member(b, b, m, fw.bindingComponentMember) as Type;
                var cn = Member(b, b, m, fw.bindingOptionalMember) is bool ob && ob ? " (可空)" : "";
                var tr = string.IsNullOrEmpty(p) ? root : root.Find(p);
                if (tr == null) { res.Add($"MISS {ctx}{t.Name}.{m.Name}() '{p}'{cn}"); continue; }
                if (ct != null && tr.GetComponent(ct) == null) res.Add($"NOCOMP {ctx}{t.Name}.{m.Name}() '{p}' need {ct.Name}{cn}");
            }
        }
    }
}
