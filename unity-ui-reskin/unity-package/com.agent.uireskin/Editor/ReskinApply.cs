// 把 diff.py 生成的 fixes.json 写回预制体（只改数值，不改结构）。
// - move：anchoredPosition += (dx, dy)（已按父节点缩放换算成本地单位，保持原锚点体系 → 跨分辨率不坏）
// - resize：sizeDelta += (dx, dy)
// - fontSize：自动字号改 fontSizeMax，否则改 fontSize
// 父节点有 LayoutGroup 的 move 会跳过（位置由布局决定，应改间距/父节点）。
// 默认 dryRun=true 只出报告；确认后 dryRun=false 写入。Pad 预制体单独跑（runtime.json 在 Pad 分辨率下导出）。
// 用法：ReskinApply.Apply("/abs/out/fixes.json", dryRun: true)
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UIReskin
{
    public static class ReskinApply
    {
        public static string Apply(string fixesPath, bool dryRun = true, string prefabOverride = null)
        {
            if (Application.isPlaying && !dryRun) return "请先退出 Play 再写预制体（Play 中改的资产在退出时不会回滚，但运行时实例不会刷新，容易误判）";
            var list = JsonUtility.FromJson<FixList>(File.ReadAllText(ReskinCore.Abs(fixesPath)));
            var prefab = prefabOverride ?? list.prefab;
            if (string.IsNullOrEmpty(prefab) || AssetDatabase.LoadAssetAtPath<GameObject>(prefab) == null) return "预制体路径无效: " + prefab;
            var sb = new StringBuilder($"[RESKIN] {(dryRun ? "DRY-RUN" : "APPLY")} {prefab} fixes={list.fixes.Count}\n");
            var root = PrefabUtility.LoadPrefabContents(prefab);
            int changed = 0;
            try
            {
                foreach (var f in list.fixes)
                {
                    var found = ReskinCore.FindInPrefab(root.transform, f.path);
                    if (found.Count == 0) { sb.AppendLine($"  MISSING {f.path}"); continue; }
                    if (found.Count > 1 || ReskinCore.PathOf(found[0], root.transform) != f.path) sb.AppendLine($"  （列表实例 → 模板 {string.Join(", ", found.Select(x => ReskinCore.PathOf(x, root.transform)))}）");
                    foreach (var t in found) {
                    var rt = t as RectTransform;
                    switch (f.op)
                    {
                        case "move":
                            if (t.parent != null && t.parent.GetComponent<UnityEngine.UI.LayoutGroup>() != null)
                            { sb.AppendLine($"  SKIP(布局组) {f.path} move {f.dx:F1},{f.dy:F1} → 改父节点间距/padding"); break; }
                            var p0 = rt.anchoredPosition; var p1 = p0 + new Vector2(f.dx, f.dy);
                            sb.AppendLine($"  move   {f.path}: {p0} → {p1}  ({f.reason})");
                            if (!dryRun) rt.anchoredPosition = p1;
                            changed++; break;
                        case "resize":
                            var s0 = rt.sizeDelta; var s1 = s0 + new Vector2(f.dx, f.dy);
                            sb.AppendLine($"  resize {f.path}: {s0} → {s1}  ({f.reason})");
                            if (!dryRun) rt.sizeDelta = s1;
                            changed++; break;
                        case "fontSize":
                            var tmp = t.GetComponent<TMPro.TMP_Text>();
                            if (tmp == null) { sb.AppendLine($"  NO-TMP {f.path}"); break; }
                            if (tmp.enableAutoSizing)
                            { sb.AppendLine($"  font   {f.path}: max {tmp.fontSizeMax} → {f.value:F0}  ({f.reason})"); if (!dryRun) tmp.fontSizeMax = Mathf.Round(f.value); }
                            else
                            { sb.AppendLine($"  font   {f.path}: {tmp.fontSize} → {f.value:F0}  ({f.reason})"); if (!dryRun) tmp.fontSize = Mathf.Round(f.value); }
                            changed++; break;
                        case "spacing":
                            // 布局组内子节点不能直接挪：改 LayoutGroup 间距（value 为增量）
                            var lg = t.GetComponent<UnityEngine.UI.HorizontalOrVerticalLayoutGroup>();
                            if (lg == null) { sb.AppendLine($"  NO-LAYOUT {f.path}"); break; }
                            sb.AppendLine($"  spacing {f.path}: {lg.spacing} → {lg.spacing + f.value}  ({f.reason})");
                            if (!dryRun) lg.spacing += f.value;
                            changed++; break;
                        default: sb.AppendLine($"  UNKNOWN op {f.op} {f.path}"); break;
                    }
                    }
                }
                if (!dryRun && changed > 0) PrefabUtility.SaveAsPrefabAsset(root, prefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            sb.AppendLine(dryRun ? $"共 {changed} 项（未写入）" : $"已写入 {changed} 项");
            return sb.ToString();
        }
    }
}
