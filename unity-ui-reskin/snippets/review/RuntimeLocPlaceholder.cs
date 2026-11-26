// 用途（Play 中）：列出当前激活的 LocalizationText 及其多语言 id、强制文本、TMP 实际文字（references/review.md §4 第 4 项）。
// id=0 且显示值等于 prefab 占位（如 1234、"Large Bundle"）即写回问题。用 FILTER 只看某个界面（按节点路径包含的字符串）。
// 运行：execute_code，skip_refresh=true（Play 中别触发刷新）。
using System;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;

public class RuntimeLocPlaceholder
{
    const string FILTER = "";   // 例如 "UIShop"；空 = 全部
    static string P(Transform t) { var s = new List<string>(); while (t != null) { s.Add(t.name); t = t.parent; } s.Reverse(); return string.Join("/", s); }
    public static string Run()
    {
        Type lt = null;
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { lt = a.GetType("DragonCD3.Localization.LocalizationText"); if (lt != null) break; }
        if (lt == null) foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) { try { lt = a.GetTypes().FirstOrDefault(x => x.Name == "LocalizationText" && typeof(Component).IsAssignableFrom(x)); } catch { } if (lt != null) break; }
        if (lt == null) return "工程里没有 LocalizationText 组件，本项不适用";
        var fId = lt.GetField("m_LocalizationID", BindingFlags.Instance | BindingFlags.NonPublic);
        var fForce = lt.GetField("m_ForceText", BindingFlags.Instance | BindingFlags.NonPublic);
        var sb = new StringBuilder();
        foreach (var c in UnityEngine.Object.FindObjectsByType(lt, FindObjectsSortMode.None).Cast<MonoBehaviour>())
        {
            if (!c.isActiveAndEnabled) continue;
            var p = P(c.transform);
            if (FILTER.Length > 0 && !p.Contains(FILTER)) continue;
            var tmp = c.GetComponent<TMPro.TMP_Text>();
            sb.AppendLine($"{p} id={fId.GetValue(c)} force='{fForce.GetValue(c)}' tmp='{tmp?.text}' font={(tmp && tmp.font ? tmp.font.name : "null")}");
        }
        return sb.Length == 0 ? "没有匹配的激活 LocalizationText" : sb.ToString();
    }
}
