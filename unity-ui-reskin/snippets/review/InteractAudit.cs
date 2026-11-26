// 用途：可点 / 可裁剪审查（references/review.md §3 第 11 项）。不进 Play，把 prefab 放进一块临时画布（尺寸 = 项目事实 ui.refWidth × refHeight，没有事实时 768×1365）里静态判断：
//   BLOCKED   按钮中心点最上层接射线的图形不是按钮自己（或其子节点），且它的父链上没有点击处理 → 点了没反应（实例：灰色 LockButton 盖住 EnterButton）
//   MASKOFF   滚动列表内容里、或"列表项 prefab"（根节点非全拉伸）里 maskable=false 的图形 → 开遮罩后画出可视区（实例：列表项 5 张图画出可视区）
//   OVERSIZE  列表内容里 / 列表项 prefab 里接射线、却没有点击处理、尺寸超出所在项的图形 → 挡住相邻项的点击（实例：放大的选中框挡住相邻项）
// 每行末尾标 (原有) 表示 Assets/_TmpUIAuditOld/ 里的旧版同样有这条；"(已不存在)" 是旧版有、新版没有的（复核修复时用）。没有旧版快照时都不标。
// PREFABS 为空时审 _TmpUIAuditOld 里出现的那批 prefab 的新版。只读：临时画布与实例用完即销毁。
// 局限：按层级先序当绘制顺序（同一画布内成立；嵌套 Canvas 的 overrideSorting 不考虑）；代码运行时才改的显隐 / 遮罩开关看不到，结论要在 Play 里证实。
//   例：选缸窗 EnterButton / LockButton 在 prefab 里都激活、运行时由代码二选一，修复后仍会报 BLOCKED（被另一个可点对象接走）——这类要回代码确认谁来切换。
// 验证：在真实项目的美术原版选缸窗上跑过，BLOCKED / MASKOFF / OVERSIZE 与 Play 实测一致。
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class InteractAudit
{
    static readonly string[] PREFABS = { };   // 例："Assets/<模块>/Prefabs/UI/UIXxxWindow.prefab"
    const string OLD = "Assets/_TmpUIAuditOld/";
    const float OVERSIZE_RATIO = 1.15f;

    static string P(Transform t, Transform r) { var s = new List<string>(); while (t != null && t != r) { s.Add(t.name); t = t.parent; } s.Reverse(); return string.Join("/", s); }
    static bool HasClickHandler(Transform t, Transform stop)
    {
        for (; t != null; t = t.parent) { if (t.GetComponent<IPointerClickHandler>() != null || t.GetComponent<IPointerDownHandler>() != null) return true; if (t == stop) break; }
        return false;
    }
    static bool RaycastBlocked(Transform t)
    {
        for (; t != null; t = t.parent) { var g = t.GetComponent<CanvasGroup>(); if (g != null && !g.blocksRaycasts) return true; }
        return false;
    }
    static Rect WorldRect(RectTransform r) { var c = new Vector3[4]; r.GetWorldCorners(c); return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y); }
    static bool IsStretchRoot(RectTransform r) => r.anchorMin == Vector2.zero && r.anchorMax == Vector2.one;

    static HashSet<string> Audit(string path, string label, StringBuilder sb)
    {
        var keys = new HashSet<string>();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) { sb.AppendLine("  缺 prefab " + path); return keys; }
        var canvasGo = new GameObject("_InteractAuditCanvas", typeof(RectTransform), typeof(Canvas));
        try
        {
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            ((RectTransform)canvasGo.transform).sizeDelta = RefSize();
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvasGo.transform);
            var root = (RectTransform)go.transform;
            Canvas.ForceUpdateCanvases();
            foreach (var lg in go.GetComponentsInChildren<LayoutGroup>(true)) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)lg.transform);
            Canvas.ForceUpdateCanvases();

            var order = go.GetComponentsInChildren<Transform>(true).Select((t, i) => (t, i)).ToDictionary(x => x.t, x => x.i);
            var targets = go.GetComponentsInChildren<Graphic>(false).Where(g => g.raycastTarget && g.enabled && !RaycastBlocked(g.transform)).ToList();

            // BLOCKED
            foreach (var b in go.GetComponentsInChildren<Button>(false).Where(b => b.interactable && !RaycastBlocked(b.transform)))
            {
                var center = WorldRect((RectTransform)b.transform).center;
                var hits = targets.Where(g => WorldRect(g.rectTransform).Contains(center)).OrderByDescending(g => order[g.transform]).ToList();
                if (hits.Count == 0) { keys.Add($"NOHIT {P(b.transform, root)}"); continue; }
                var top = hits[0].transform;
                if (top == b.transform || top.IsChildOf(b.transform)) continue;
                var handler = HasClickHandler(top, root) ? "（被另一个可点对象接走）" : "（上层没有点击处理 → 点了没反应）";
                keys.Add($"BLOCKED {P(b.transform, root)} <- {P(top, root)} {handler}");
            }

            // 列表范围：ScrollRect 的 content；根节点非全拉伸的 prefab 视为列表项，整份都算
            var scopes = new List<(Transform scope, Transform itemRootOf)>();
            foreach (var sr in go.GetComponentsInChildren<ScrollRect>(true)) if (sr.content != null) scopes.Add((sr.content, sr.content));
            if (!IsStretchRoot(root)) scopes.Add((root, null));
            foreach (var (scope, contentRoot) in scopes)
                foreach (var g in scope.GetComponentsInChildren<Graphic>(true))
                {
                    var mg = g as MaskableGraphic;
                    if (mg != null && !mg.maskable) keys.Add($"MASKOFF {P(g.transform, root)}");
                    if (!g.raycastTarget || HasClickHandler(g.transform, root)) continue;
                    // 所在项：列表内容的直接子节点；列表项 prefab 则是根
                    var item = contentRoot == null ? root : g.transform;
                    if (contentRoot != null) { while (item.parent != null && item.parent != contentRoot) item = item.parent; if (item.parent != contentRoot) continue; }
                    if (g.transform == item) continue;
                    var gr = g.rectTransform.rect.size; var ir = ((RectTransform)item).rect.size;
                    if (ir.x > 1 && ir.y > 1 && (gr.x > ir.x * OVERSIZE_RATIO || gr.y > ir.y * OVERSIZE_RATIO))
                        keys.Add($"OVERSIZE {P(g.transform, root)} {gr.x:F0}x{gr.y:F0} > 项 {(item == root ? "(根)" : P(item, root))} {ir.x:F0}x{ir.y:F0}");
                }
        }
        finally { Object.DestroyImmediate(canvasGo); }
        return keys;
    }

    [System.Serializable] public class UiF { public float refWidth, refHeight; }
    // funplay 片段编译在内存程序集里，JsonUtility 只认顶层字段：先截出某一段再解析成扁平类
    static string Section(string j, string key)
    {
        if (j == null) return null;
        int k = j.IndexOf("\"" + key + "\"", System.StringComparison.Ordinal); if (k < 0) return null;
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
    static Vector2 RefSize()
    {
        var env = System.Environment.GetEnvironmentVariable("UIRESKIN_FACTS"); string json = null;
        if (!string.IsNullOrEmpty(env) && System.IO.File.Exists(env)) json = System.IO.File.ReadAllText(env);
        var d = new System.IO.DirectoryInfo(System.IO.Path.GetDirectoryName(Application.dataPath));
        for (int i = 0; json == null && d != null && i < 6; i++, d = d.Parent) { var p = System.IO.Path.Combine(d.FullName, ".uireskin", "facts.json"); if (System.IO.File.Exists(p)) json = System.IO.File.ReadAllText(p); }
        var ui = JsonUtility.FromJson<UiF>(Section(json, "ui") ?? "{}");
        return ui != null && ui.refWidth > 0 && ui.refHeight > 0 ? new Vector2(ui.refWidth, ui.refHeight) : new Vector2(768, 1365);
    }

    public static string Run()
    {
        var list = PREFABS.Length > 0 ? PREFABS.ToList()
            : AssetDatabase.FindAssets("t:Prefab", new[] { OLD.TrimEnd('/') }).Select(AssetDatabase.GUIDToAssetPath).Select(p => "Assets/" + p.Substring(OLD.Length)).OrderBy(x => x).ToList();
        if (list.Count == 0) return "没有要审的 prefab：填 PREFABS，或先导出 _TmpUIAuditOld";
        var sb = new StringBuilder();
        foreach (var path in list)
        {
            var tmp = new StringBuilder();
            var now = Audit(path, "new", tmp);
            var oldPath = OLD + path.Substring("Assets/".Length);
            var old = AssetDatabase.LoadAssetAtPath<GameObject>(oldPath) != null ? Audit(oldPath, "old", new StringBuilder()) : null;
            var gone = old == null ? new List<string>() : old.Except(now).OrderBy(x => x).ToList();
            if (now.Count == 0 && gone.Count == 0 && tmp.Length == 0) continue;
            sb.AppendLine("## " + path);
            sb.Append(tmp);
            foreach (var k in now.OrderBy(x => x)) sb.AppendLine("  " + k + (old != null && old.Contains(k) ? "  (原有)" : ""));
            foreach (var k in gone) sb.AppendLine("  (已不存在) " + k);
        }
        return sb.Length == 0 ? "无问题" : sb.ToString();
    }
}
