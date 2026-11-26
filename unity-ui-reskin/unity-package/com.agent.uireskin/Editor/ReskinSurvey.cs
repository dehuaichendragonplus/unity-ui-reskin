// 只读普查项目的 UI 框架，给代理写新界面业务代码当依据（照着项目里最相近的界面写，而不是套通用模板）。
// 输出：界面类的基类分布与典型例子（含脚本路径）、字段 / 类上用的绑定特性、打开 / 关闭界面的管理器方法、多语言组件、事件总线候选。
// 用法（execute_code）：return UIReskin.ReskinSurvey.Run();   可选 Run("Shop") 只看名字含关键字的界面，便于找最像的参照
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace UIReskin
{
    public static class ReskinSurvey
    {
        static readonly Regex ViewName = new Regex(@"(^UI[A-Z_]|View$|Window$|Popup$|Panel$|Dialog$|Page$|Form$|Layer$)");
        static readonly Regex NotView = new Regex(@"(System|Manager|Mgr|Root|Controller|Ctrl|Helper|Utils?|Config|Data|Model|Service|Attribute|Extensions?|Editor|Inspector|Drawer|Builder|Factory|Pool|Loader|Settings?)$");
        static readonly string[] OpenNames = { "ShowView", "OpenView", "OpenWindow", "OpenUI", "OpenPanel", "ShowWindow", "ShowUI", "Open", "Show", "Push" };
        static readonly string[] CloseNames = { "HideView", "CloseView", "CloseWindow", "CloseUI", "ClosePanel", "HideWindow", "HideUI", "Close", "Hide", "Pop" };

        // 常见第三方 SDK / 库的程序集前缀（不是项目自己的 UI 框架）
        static readonly Regex SdkPrefix = new Regex(@"^(Facebook|Amazon|dnlib|Google|Firebase|AppLovin|MaxSdk|IronSource|Applovin|Adjust|AppsFlyer|Spine|Coffee|Lean|TextMeshPro|Cinemachine|Febucci|Demigiant|Zenject|UniRx|UniTask|Cysharp|MessagePack|protobuf|Google\.Protobuf|Odin|Plugins)", RegexOptions.IgnoreCase);

        static bool ProjectAsm(Assembly a)
        {
            var n = a.GetName().Name;
            return !(n.StartsWith("Unity") || n.StartsWith("System") || n == "mscorlib" || n == "netstandard" || n.StartsWith("Mono.") || n.StartsWith("nunit")
                     || n.StartsWith("Newtonsoft") || n.StartsWith("UIReskin") || n.StartsWith("Funplay") || n.StartsWith("JetBrains") || n.StartsWith("Bee.") || n.StartsWith("DOTween")
                     || n.StartsWith("Sirenix") || n.Contains("Editor") || SdkPrefix.IsMatch(n));
        }

        static IEnumerable<Type> SafeTypes(Assembly a) { try { return a.GetTypes(); } catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); } }

        public static string Run(string filter = null)
        {
            var asms = AppDomain.CurrentDomain.GetAssemblies().Where(ProjectAsm).Where(a => !a.GetName().Name.EndsWith(".Tests") && !a.GetName().Name.Contains("Test")).ToArray();
            var types = asms.SelectMany(SafeTypes).Where(t => t.IsClass && !t.IsAbstract && !t.IsGenericTypeDefinition && t.Namespace?.StartsWith("UnityEngine") != true).ToArray();
            var views = types.Where(t => ViewName.IsMatch(t.Name) && !NotView.IsMatch(t.Name) && t.BaseType != null && t.BaseType != typeof(object)
                                         && !typeof(UnityEngine.EventSystems.UIBehaviour).IsAssignableFrom(t) && !typeof(EditorWindow).IsAssignableFrom(t)
                                         && !typeof(UnityEditor.Editor).IsAssignableFrom(t) && !typeof(ScriptableObject).IsAssignableFrom(t)).ToList();
            if (!string.IsNullOrEmpty(filter)) views = views.Where(t => t.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            var sb = new StringBuilder($"[RESKIN] UI 框架普查：项目程序集 {asms.Length}，界面类 {views.Count}{(filter != null ? $"（过滤 {filter}）" : "")}\n");

            // 1) 基类分布：取「项目里定义的、离 MonoBehaviour / object 最近的」那层基类当框架基类
            Type FrameworkBase(Type t)
            {
                Type last = null;
                for (var b = t.BaseType; b != null && b != typeof(object) && b != typeof(MonoBehaviour) && b != typeof(Behaviour) && b != typeof(Component); b = b.BaseType)
                    if (ProjectAsm(b.Assembly)) last = b.IsGenericType ? b.GetGenericTypeDefinition() : b;
                return last ?? t.BaseType;
            }
            var byBase = views.GroupBy(FrameworkBase).OrderByDescending(g => g.Count()).Take(6).ToList();
            sb.AppendLine("\n## 界面基类（新界面照数量最多、和需求最像的那类写）");
            foreach (var g in byBase)
            {
                bool mb = typeof(MonoBehaviour).IsAssignableFrom(g.First());
                sb.AppendLine($"- {Nice(g.Key)}：{g.Count()} 个（{(mb ? "MonoBehaviour" : "非 MonoBehaviour，代码驱动 prefab")}）");
                foreach (var t in g.OrderBy(t => t.Name.Length).Take(5)) sb.AppendLine($"    {t.FullName}  {ScriptPath(t)}");
                // 基类上的可重写方法（生命周期）
                var life = g.Key.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(m => (m.IsVirtual || m.IsAbstract) && !m.IsFinal && !m.IsSpecialName).Select(m => m.Name).Distinct().Take(14);
                sb.AppendLine($"    可重写：{string.Join(", ", life)}");
            }

            // 2) 绑定特性：界面类字段 / 类上出现的自定义特性
            var fieldAttr = new Dictionary<string, int>(); var classAttr = new Dictionary<string, int>();
            void Inc(Dictionary<string, int> d, string k) => d[k] = d.TryGetValue(k, out var c) ? c + 1 : 1;
            foreach (var t in views)
            {
                foreach (var a in t.GetCustomAttributes(false)) Inc(classAttr, a.GetType().FullName);
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    foreach (var a in f.GetCustomAttributes(false)) Inc(fieldAttr, a.GetType().FullName);
            }
            sb.AppendLine("\n## 节点绑定方式");
            sb.AppendLine("- 字段特性：" + string.Join("，", fieldAttr.OrderByDescending(kv => kv.Value).Take(8).Select(kv => $"{kv.Key} ×{kv.Value}")));
            sb.AppendLine("- 类特性：" + string.Join("，", classAttr.OrderByDescending(kv => kv.Value).Take(8).Select(kv => $"{kv.Key} ×{kv.Value}")));
            var topAttr = fieldAttr.Where(kv => !kv.Key.StartsWith("System.")).OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault();
            var pool = byBase.Count > 0 ? byBase[0].AsEnumerable() : views;
            var sample = pool.Concat(views).FirstOrDefault(t => t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Count(f => f.GetCustomAttributes(false).Any(a => a.GetType().FullName == topAttr)) >= 2);
            if (sample != null)
            {
                sb.AppendLine($"- 例（{sample.Name}）：");
                foreach (var f in sample.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Where(f => f.GetCustomAttributes(false).Length > 0).Take(6))
                    sb.AppendLine($"    {string.Join(" ", f.GetCustomAttributesData().Select(Attr))} {f.FieldType.Name} {f.Name}");
                foreach (var a in sample.GetCustomAttributesData().Take(3)) sb.AppendLine($"    类：{Attr(a)}");
            }

            // 3) 打开 / 关闭界面的方法
            sb.AppendLine("\n## 打开 / 关闭界面（管理器候选）");
            var mgr = new List<string>();
            foreach (var t in types)
            {
                foreach (var m in t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    bool open = OpenNames.Contains(m.Name), close = CloseNames.Contains(m.Name);
                    if (!open && !close) continue;
                    var ps = m.GetParameters();
                    bool viewish = m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 1 || ps.Length > 0 && (ps[0].ParameterType == typeof(Type) || ps[0].ParameterType == typeof(string));
                    if (!viewish) continue;
                    var gen = m.IsGenericMethodDefinition ? $"<{string.Join(",", m.GetGenericArguments().Select(a => a.Name + (a.GetGenericParameterConstraints().Length > 0 ? ":" + string.Join("&", a.GetGenericParameterConstraints().Select(Nice)) : "")))}>" : "";
                    // 排序：泛型 + 约束是项目接口 / 基类的 ShowView 一类最像通用 UI 管理器；界面类自带的静态 Open 次之
                    int rank = m.IsGenericMethodDefinition && m.GetGenericArguments()[0].GetGenericParameterConstraints().Any(c => ProjectAsm(c.Assembly)) ? 0 : m.IsGenericMethodDefinition ? 1 : views.Contains(t) ? 3 : 2;
                    mgr.Add($"{rank}|{(open ? "开" : "关")} {(m.IsStatic ? "static " : "")}{t.FullName}.{m.Name}{gen}({string.Join(", ", ps.Select(p => p.ParameterType.Name + " " + p.Name))})");
                }
            }
            foreach (var l in mgr.OrderBy(x => x[0]).ThenBy(x => x.Contains("View") || x.Contains("Window") ? 0 : 1).Take(16)) sb.AppendLine("- " + l.Substring(2));
            if (mgr.Count == 0) sb.AppendLine("- 没找到常见命名的管理器方法：grep 现有界面是怎么被打开的");

            // 4) 多语言组件、事件总线、单例入口
            var locs = types.Where(t => typeof(Component).IsAssignableFrom(t) && Regex.IsMatch(t.Name, "Locali[sz]", RegexOptions.IgnoreCase)).Select(t => t.FullName).Take(6);
            sb.AppendLine("\n## 多语言组件：" + string.Join("，", locs));
            var bus = types.Where(t => Regex.IsMatch(t.Name, "^(EventBus|EventManager|EventCenter|MessageCenter|Messenger|Signal(Bus)?|GameEvent)", RegexOptions.IgnoreCase)).Select(t => t.FullName).Take(6);
            sb.AppendLine("## 事件 / 消息：" + string.Join("，", bus));
            sb.AppendLine("\n下一步：用上面数量最多且最像的界面当参照，读它的脚本与 prefab，照同样的基类 / 绑定 / 注册方式写新界面；ReskinSurvey.Run(\"关键字\") 可缩小范围。");
            return sb.ToString();
        }

        static string Nice(Type t) => t == null ? "null" : t.IsGenericType ? Regex.Replace(t.FullName ?? t.Name, "`\\d+.*$", "") + "<>" : t.FullName;
        static string Attr(CustomAttributeData a) =>
            "[" + a.AttributeType.Name.Replace("Attribute", "") + (a.ConstructorArguments.Count > 0 ? "(" + string.Join(", ", a.ConstructorArguments.Select(x => x.Value is string s ? $"\"{s}\"" : x.Value?.ToString())) + ")" : "") + "]";

        static string ScriptPath(Type t)
        {
            foreach (var g in AssetDatabase.FindAssets($"{t.Name} t:MonoScript"))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (System.IO.Path.GetFileNameWithoutExtension(p) == t.Name) return p;
            }
            return "";
        }
    }
}
