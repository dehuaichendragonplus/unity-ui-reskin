// 用途（Play 中）：用反射打开 / 关闭一个界面，便于逐个截图核对（references/review.md §4 第 2 项）。零配置，按常见 UI 管理器约定自动找打开方法。
// 用法：改下面两个常量后整段 execute_code（skip_refresh=true）。
//   VIEW   界面类名（短名或全名），例如 "SettingsPopup"、"Game.UI.UIShopWindow"
//   CLOSE  true = 关闭，false = 打开
// 查找顺序（找到第一个能调用的就用）：
//   1) 任意静态类 / 单例（静态 Instance / instance / Inst / UISystem / UIManager 属性或字段）上的泛型方法
//      ShowView<T> / Show<T> / Open<T> / OpenView<T> / OpenWindow<T> / OpenUI<T> / Push<T>（关闭：HideView / Hide / Close / CloseView / CloseWindow / CloseUI / Pop）
//   2) 同名非泛型方法，参数是 System.Type 或 string（类名）
//   方法有其他可选参数时用默认值；必需参数不止一个的跳过。
// 找不到时返回候选方法清单：从里面挑对的，按 OpenViewInPlayLoop.cs 的方式直接写调用；把确认好的打开方式记进项目记忆，下次直接用。
// 例（BubbleWord3）：GameManager.UISystem.ShowView<T>()、FishPoolWindows.Show<T>()。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

public class OpenViews
{
    const string VIEW = "";
    const bool CLOSE = false;

    static readonly string[] OpenNames = { "ShowView", "Show", "Open", "OpenView", "OpenWindow", "OpenUI", "OpenPanel", "ShowWindow", "ShowUI", "Push" };
    static readonly string[] CloseNames = { "HideView", "Hide", "Close", "CloseView", "CloseWindow", "CloseUI", "ClosePanel", "HideWindow", "HideUI", "Pop" };
    static readonly string[] SingletonNames = { "Instance", "instance", "Inst", "Ins", "UISystem", "UIManager", "Current" };
    const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    const BindingFlags I = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static string Run()
    {
        if (!Application.isPlaying) return "需要 Play 模式";
        if (string.IsNullOrEmpty(VIEW)) return "先填 VIEW";
        var asms = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.FullName.StartsWith("Unity") && !a.FullName.StartsWith("System") && !a.FullName.StartsWith("mscorlib")).ToArray();
        var types = asms.SelectMany(a => { try { return a.GetTypes(); } catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); } }).ToArray();
        var view = types.FirstOrDefault(t => t.FullName == VIEW) ?? types.FirstOrDefault(t => t.Name == VIEW);
        if (view == null) return "找不到类型 " + VIEW;
        var names = CLOSE ? CloseNames : OpenNames;
        var candidates = new List<string>();
        foreach (var host in types)
        {
            if (host.IsGenericTypeDefinition) continue;
            // 宿主对象：静态方法用 null；实例方法找静态单例（含二级：GameManager.UISystem）
            var targets = new List<(object obj, string path)> { (null, host.Name) };
            foreach (var sn in SingletonNames)
            {
                object o = null;
                try { o = host.GetProperty(sn, S)?.GetValue(null) ?? host.GetField(sn, S)?.GetValue(null); } catch { }
                if (o == null) continue;
                targets.Add((o, host.Name + "." + sn));
                foreach (var sn2 in SingletonNames)
                {
                    object o2 = null;
                    try { o2 = o.GetType().GetProperty(sn2, I)?.GetValue(o) ?? o.GetType().GetField(sn2, I)?.GetValue(o); } catch { }
                    if (o2 != null) targets.Add((o2, host.Name + "." + sn + "." + sn2));
                }
            }
            foreach (var (obj, path) in targets)
            {
                var t = obj == null ? host : obj.GetType();
                foreach (var m in t.GetMethods(obj == null ? S : I))
                {
                    if (!names.Contains(m.Name)) continue;
                    var ps = m.GetParameters();
                    if (ps.Count(p => !p.IsOptional) > 1) continue;
                    object[] args;
                    MethodInfo call = m;
                    if (m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 1 && ps.All(p => p.IsOptional))
                    {
                        try { call = m.MakeGenericMethod(view); } catch { continue; }   // 泛型约束不满足
                        args = ps.Select(p => p.DefaultValue).ToArray();
                    }
                    else if (!m.IsGenericMethodDefinition && ps.Length > 0 && ps[0].ParameterType == typeof(Type))
                        args = new object[] { view }.Concat(ps.Skip(1).Select(p => p.DefaultValue)).ToArray();
                    else if (!m.IsGenericMethodDefinition && ps.Length > 0 && ps[0].ParameterType == typeof(string))
                        args = new object[] { view.Name }.Concat(ps.Skip(1).Select(p => p.DefaultValue)).ToArray();
                    else continue;
                    candidates.Add($"{path}.{m.Name}{(m.IsGenericMethodDefinition ? "<T>" : "")}({string.Join(", ", ps.Select(p => p.ParameterType.Name))})");
                    if (!(typeof(Component).IsAssignableFrom(view) || m.IsGenericMethodDefinition || ps[0].ParameterType == typeof(Type))) continue;
                    try
                    {
                        var r = call.Invoke(obj, args);
                        return $"{(CLOSE ? "关闭" : "打开")} {view.FullName}：{path}.{m.Name} → {r ?? "ok"}\n（确认画面正确后，把这个调用记进项目记忆）";
                    }
                    catch (Exception e) { candidates[candidates.Count - 1] += "  ✗ " + (e.InnerException ?? e).Message; }
                }
            }
        }
        return $"没能自动{(CLOSE ? "关闭" : "打开")} {view.FullName}。候选方法：\n  " + string.Join("\n  ", candidates.Take(40)) +
               "\n没有合适的：grep 项目里打开该界面的调用（如 Show / Open + 类名），照着写一段 execute_code。";
    }
}
