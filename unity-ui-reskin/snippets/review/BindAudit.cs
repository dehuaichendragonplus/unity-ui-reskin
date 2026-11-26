// 用途：DragonCD3 UI 框架（View + [Binding] + [ViewDescriptor]）下所有界面的绑定路径与组件类型，新版 vs 旧版各跑一遍，只关心"新版才失败"的项（references/review.md §3 第 1 项）。
// 前提：已用 scripts/export_snapshot.py 把旧版导到 Assets/_TmpUIAuditOld/。运行：funplay execute_code，整段粘贴；只读，可加 skip_refresh=true。
// 输出：每个改动过的界面一行；[REGRESS] = 本轮新引入，[PRE] = 原来就有，[OLDONLY] = 本轮反而修好了。
using System;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class BindAudit
{
    const string OLD = "Assets/_TmpUIAuditOld/";
    static Type viewT, bindT, descT;

    public static string Run()
    {
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type t;
            if ((t = a.GetType("DragonCD3.UI.View")) != null) viewT = t;
            if ((t = a.GetType("DragonCD3.UI.BindingAttribute")) != null) bindT = t;
            if ((t = a.GetType("DragonCD3.UI.ViewDescriptorAttribute")) != null) descT = t;
        }
        if (viewT == null || bindT == null || descT == null) return "本项目没有 DragonCD3 UI 绑定体系（DragonCD3.UI.View / BindingAttribute / ViewDescriptorAttribute），本项不适用：改用 grep 代码里的 Find / GetComponent 路径 + HierDiff 的删除节点名";
        var olds = AssetDatabase.FindAssets("t:Prefab", new[] { OLD.TrimEnd('/') }).Select(AssetDatabase.GUIDToAssetPath).ToList();
        var all = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath).Where(p => !p.StartsWith("Assets/_Tmp")).ToList();
        var outp = new StringBuilder($"旧版副本 {olds.Count} 个\n");
        foreach (var vt in TypeCache.GetTypesDerivedFrom(viewT).Where(t => !t.IsAbstract))
        {
            var desc = vt.GetCustomAttributes(descT, true).FirstOrDefault();
            var key = desc == null ? null : descT.GetField("PrefabKey").GetValue(desc) as string;
            if (string.IsNullOrEmpty(key)) continue;
            // PrefabKey 有两种写法：文件名，或小写相对路径
            var newPath = key.Contains("/")
                ? all.FirstOrDefault(p => p.ToLowerInvariant().EndsWith("/" + key.ToLowerInvariant()))
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
        for (var t = vt; t != null && t != typeof(object); t = t.BaseType)
        {
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                var b = f.GetCustomAttribute(bindT); if (b == null) continue;
                var p = (string)bindT.GetField("Path").GetValue(b);
                var cn = (bool)bindT.GetField("CanBeNull").GetValue(b) ? " (CanBeNull)" : "";
                var tr = string.IsNullOrEmpty(p) ? root : root.Find(p);
                if (tr == null) { res.Add($"MISS {ctx}{t.Name}.{f.Name} '{p}'{cn}"); continue; }
                var ft = f.FieldType;
                if (ft == typeof(GameObject) || ft == typeof(Transform)) continue;
                if (ft.IsSubclassOf(viewT)) { Check(ft, tr, ctx + ft.Name + "[" + p + "]>", depth + 1, res); continue; }
                if (tr.GetComponent(ft) == null) res.Add($"NOCOMP {ctx}{t.Name}.{f.Name} '{p}' need {ft.Name}{cn}");
            }
            foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                var b = m.GetCustomAttribute(bindT); if (b == null) continue;
                var p = (string)bindT.GetField("Path").GetValue(b);
                var ct = (Type)bindT.GetField("ComponentType").GetValue(b);
                var cn = (bool)bindT.GetField("CanBeNull").GetValue(b) ? " (CanBeNull)" : "";
                var tr = string.IsNullOrEmpty(p) ? root : root.Find(p);
                if (tr == null) { res.Add($"MISS {ctx}{t.Name}.{m.Name}() '{p}'{cn}"); continue; }
                if (tr.GetComponent(ct) == null) res.Add($"NOCOMP {ctx}{t.Name}.{m.Name}() '{p}' need {ct.Name}{cn}");
            }
        }
    }
}
