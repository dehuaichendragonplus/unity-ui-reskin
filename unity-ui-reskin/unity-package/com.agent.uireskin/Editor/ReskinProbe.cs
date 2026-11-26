// 运行时导出：把界面实例里每个节点的「精确屏幕矩形 + 图片/文字参数 + 布局参数」写成 runtime.json，
// 供 diff.py 与效果图测量结果做差。运行时这一侧不用 CV，数值是精确的。
// 用法（Play 中，界面已打开、动画已快进）：
//   ReskinProbe.Dump("UIPopupBattlePassActivate", "/abs/out/runtime.json", "Assets/Res/.../UIPopupBattlePassActivate.prefab");
//   ReskinProbe.Capture("/abs/out/runtime.png");   // 截同一帧的 Game 视图（异步，下一帧写盘）
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace UIReskin
{
    public static class ReskinProbe
    {
        public static string Dump(string viewName, string outPath, string prefabPath = null)
        {
            outPath = ReskinCore.Abs(outPath);
            var view = ReskinCore.FindViewRoot(viewName);
            if (view == null) return "找不到界面实例 " + viewName;
            var d = new RuntimeDump
            {
                view = viewName, prefab = prefabPath, screenW = Screen.width, screenH = Screen.height,
                pad = view.name.Contains(ReskinConfig.Data.padSuffix), time = DateTime.Now.ToString("s")
            };
            foreach (var rt in view.GetComponentsInChildren<RectTransform>(false))
            {
                var r = ReskinCore.ScreenRect(rt);
                var n = new RNode
                {
                    path = ReskinCore.PathOf(rt, view),
                    rect = new[] { r.x, r.y, r.width, r.height },
                    visible = Visible(rt, r),
                    anchorMin = V(rt.anchorMin), anchorMax = V(rt.anchorMax), pivot = V(rt.pivot),
                    pos = V(rt.anchoredPosition), size = V(rt.sizeDelta), scale = new[] { rt.localScale.x, rt.localScale.y },
                    ppu = ReskinCore.PixelsPerParentUnit(rt),
                    hasLayoutParent = rt.parent != null && rt.parent.GetComponent<LayoutGroup>() != null && rt.GetComponent<LayoutElement>()?.ignoreLayout != true,
                };
                var img = rt.GetComponent<Image>();
                if (img != null && img.enabled)
                {
                    n.sprite = img.sprite != null ? img.sprite.name : null;
                    n.imageType = img.type.ToString(); n.alpha = img.color.a;
                }
                var raw = rt.GetComponent<RawImage>();
                if (raw != null && raw.enabled && raw.texture != null) { n.sprite = raw.texture.name; n.imageType = "Raw"; n.alpha = raw.color.a; }
                var tmp = rt.GetComponent<TMPro.TMP_Text>();
                if (tmp != null && tmp.enabled)
                {
                    n.text = tmp.GetParsedText();
                    var b = tmp.textBounds;
                    if (b.size.x > 0.01f && b.size.y > 0.01f)
                    {
                        var c4 = new[] { rt.TransformPoint(new Vector3(b.min.x, b.min.y)), rt.TransformPoint(new Vector3(b.min.x, b.max.y)), rt.TransformPoint(new Vector3(b.max.x, b.max.y)), rt.TransformPoint(new Vector3(b.max.x, b.min.y)) };
                        var tr = ReskinCore.ScreenRect(c4, ReskinCore.CanvasCamera(rt));
                        n.textRect = new[] { tr.x, tr.y, tr.width, tr.height };
                    } n.fontSize = tmp.fontSize; n.autoSize = tmp.enableAutoSizing; n.fontSizeMax = tmp.fontSizeMax;
                    n.color = C(tmp.color);
                    var m = tmp.fontSharedMaterial;
                    if (m != null)
                    {
                        n.material = m.name;
                        if (m.HasProperty("_UnderlayColor")) n.underlayColor = C(m.GetColor("_UnderlayColor"));
                        if (m.HasProperty("_UnderlayDilate")) n.underlayDilate = m.GetFloat("_UnderlayDilate");
                        if (m.HasProperty("_UnderlayOffsetY")) n.underlayOffsetY = m.GetFloat("_UnderlayOffsetY");
                    }
                    var loc = ReskinConfig.LocText(rt);
                    if (loc != null && !string.IsNullOrEmpty(ReskinConfig.Data.locTermField))
                    {
                        var f = loc.GetType().GetField(ReskinConfig.Data.locTermField, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        n.term = f?.GetValue(loc)?.ToString();
                    }
                }
                d.nodes.Add(n);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllText(outPath, JsonUtility.ToJson(d, true));
            return $"runtime.json 节点 {d.nodes.Count}（可见 {d.nodes.Count(x => x.visible)}） screen={Screen.width}x{Screen.height} pad={d.pad} → {outPath}";
        }

        static bool Visible(RectTransform rt, Rect r)
        {
            if (r.width < 2 || r.height < 2) return false;
            if (r.xMax < 0 || r.yMax < 0 || r.x > Screen.width || r.y > Screen.height) return false;
            // 只查到根画布为止：ScreenSpaceCamera 画布根节点自身缩放本来就很小（≈0.003）
            var canvasRoot = rt.GetComponentInParent<Canvas>()?.rootCanvas?.transform;
            for (var t = rt.transform; t != null && t != canvasRoot; t = t.parent)
            {
                var cg = t.GetComponent<CanvasGroup>();
                if (cg != null && cg.alpha < 0.02f) return false;
                if (Mathf.Abs(t.localScale.x) < 0.01f || Mathf.Abs(t.localScale.y) < 0.01f) return false;
            }
            return true;
        }

        static float[] V(Vector2 v) => new[] { v.x, v.y };
        static float[] C(Color c) => new[] { c.r, c.g, c.b, c.a };

        /// 在帧末截取 Game 视图（包含 Overlay 画布）。需要一个 DontDestroyOnLoad 下的 MonoBehaviour 跑协程。
        public static string Capture(string outPath, Action<string> done = null)
        {
            var host = Host();
            if (host == null) return "no host";
            host.StartCoroutine(CaptureCo(outPath, done));
            return "capturing → " + outPath;
        }

        public static IEnumerator CaptureCo(string outPath, Action<string> done = null)
        {
            outPath = ReskinCore.Abs(outPath);
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            Directory.CreateDirectory(Path.GetDirectoryName(outPath));
            File.WriteAllBytes(outPath, tex.EncodeToPNG());
            var msg = $"[RESKIN] captured {tex.width}x{tex.height} → {outPath}";
            UnityEngine.Object.Destroy(tex);
            Debug.Log(msg);
            done?.Invoke(outPath);
        }

        public static MonoBehaviour Host()
        {
            foreach (var m in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (m.isActiveAndEnabled && m.gameObject.scene.name == "DontDestroyOnLoad") return m;
            return null;
        }
    }
}
