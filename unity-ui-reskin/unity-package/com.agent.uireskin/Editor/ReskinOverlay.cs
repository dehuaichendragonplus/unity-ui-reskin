// 编辑器内实时叠图 + 辅助线（Play 模式）。
// - 效果图层：半透明叠加（Alpha）或只叠效果图的「边缘线」（Edges，对齐时最清楚）。
// - 辅助线层：读 diff.py 生成的 guides.json，按组画「目标框（品红）」和「运行时实际框（绿）」并标出偏差像素；
//   目标框按节点锚点 + 目标 anchoredPosition/sizeDelta 计算，换分辨率后会跟着锚点移动 → 没有效果图的比例也能检查。
// - 安全区：黄色框。
// 不依赖自定义 MonoBehaviour：用内置 Canvas/RawImage/Image/Text 搭，刷新挂在 EditorApplication.update 上。
// 用法（execute_code 或菜单 Tools/UIReskin）：
//   ReskinOverlay.Show("/abs/效果图.png");  ReskinOverlay.SetMode(OverlayMode.Edges); ReskinOverlay.SetAlpha(0.5f);
//   ReskinOverlay.LoadGuides("/abs/guides.json"); ReskinOverlay.SetGroup("Root/MiddleGroup", false); ReskinOverlay.Hide();
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace UIReskin
{
    public enum OverlayMode { Off, Alpha, Edges }

    public static class ReskinOverlay
    {
        const string RootName = "~ReskinOverlay";
        static GameObject _root;
        static RawImage _mock;
        static Texture2D _mockTex, _edgeTex;
        static OverlayMode _mode = OverlayMode.Alpha;
        static float _alpha = 0.5f;
        static RectTransform _guideLayer;
        static GuideList _guides;
        static readonly Dictionary<string, bool> _groupOn = new Dictionary<string, bool>();
        static bool _showSafeArea = true, _showGuides = true;
        static double _nextRefresh;
        static Font _font;

        public static Color TargetColor = new Color(1f, 0.1f, 0.85f, 0.95f);
        public static Color ActualColor = new Color(0.1f, 1f, 0.3f, 0.95f);
        public static float DeltaThreshold = 1.5f;

        // ---------- 公开 API ----------
        public static string Show(string mockupPath, OverlayMode mode = OverlayMode.Alpha, float alpha = 0.5f)
        {
            if (!Application.isPlaying) return "需要 Play 模式";
            mockupPath = ReskinCore.Abs(mockupPath);
            if (!File.Exists(mockupPath)) return "找不到效果图 " + mockupPath;
            EnsureRoot();
            _mockTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            _mockTex.LoadImage(File.ReadAllBytes(mockupPath));
            _edgeTex = BuildEdges(_mockTex);
            _mode = mode; _alpha = alpha;
            Apply();
            return $"overlay {Path.GetFileName(mockupPath)} {_mockTex.width}x{_mockTex.height} screen={Screen.width}x{Screen.height}" +
                   (Mathf.Abs((float)_mockTex.height / _mockTex.width - (float)Screen.height / Screen.width) > 0.01f ? " ⚠效果图与屏幕长宽比不同，效果图层只按宽度贴顶对齐" : "");
        }

        /// 临时隐藏整个叠图层（测量用截图不能带叠图）
        public static bool Active { get => _root != null && _root.activeSelf; set { if (_root != null) _root.SetActive(value); } }
        public static void SetMode(OverlayMode m) { _mode = m; Apply(); }
        public static void SetAlpha(float a) { _alpha = Mathf.Clamp01(a); Apply(); }
        public static void ShowSafeArea(bool on) { _showSafeArea = on; Refresh(); }
        public static void ShowGuides(bool on) { _showGuides = on; Refresh(); }

        public static string LoadGuides(string json)
        {
            if (!json.TrimStart().StartsWith("{")) json = File.ReadAllText(ReskinCore.Abs(json));
            _guides = JsonUtility.FromJson<GuideList>(json);
            EnsureRoot();
            foreach (var g in _guides.guides) if (g.group != null && !_groupOn.ContainsKey(g.group)) _groupOn[g.group] = true;
            Refresh();
            return $"guides={_guides.guides.Count} groups={string.Join(",", _groupOn.Keys)}";
        }

        public static void SetGroup(string group, bool on) { _groupOn[group] = on; Refresh(); }
        public static void SoloGroup(string group) { foreach (var k in new List<string>(_groupOn.Keys)) _groupOn[k] = k == group; Refresh(); }

        public static void Hide()
        {
            EditorApplication.update -= Tick;
            if (_root) Object.Destroy(_root);
            _root = null;
        }

        // ---------- 菜单 ----------
        [MenuItem("Tools/UIReskin/叠图 模式切换 (Alpha→Edges→Off) %#&o")]
        static void MenuCycle() { if (_root == null) return; _mode = (OverlayMode)(((int)_mode + 2) % 3); Apply(); }
        [MenuItem("Tools/UIReskin/叠图 透明度 +0.25 %#&p")]
        static void MenuAlpha() { if (_root == null) return; _alpha = _alpha >= 1f ? 0.25f : _alpha + 0.25f; Apply(); }
        [MenuItem("Tools/UIReskin/辅助线 显示切换 %#&g")]
        static void MenuGuides() { _showGuides = !_showGuides; Refresh(); }
        [MenuItem("Tools/UIReskin/关闭叠图")]
        static void MenuHide() => Hide();

        // ---------- 实现 ----------
        static void EnsureRoot()
        {
            if (_root != null) return;
            _root = new GameObject(RootName);
            Object.DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32767;
            var mgo = new GameObject("Mockup", typeof(RectTransform), typeof(RawImage));
            mgo.transform.SetParent(_root.transform, false);
            _mock = mgo.GetComponent<RawImage>(); _mock.raycastTarget = false;
            var lgo = new GameObject("Guides", typeof(RectTransform));
            lgo.transform.SetParent(_root.transform, false);
            _guideLayer = (RectTransform)lgo.transform;
            Stretch(_guideLayer);
            try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            if (_font == null) try { _font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }   // 2022.1 及更早
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

        static void Apply()
        {
            if (_root == null || _mock == null) return;
            _mock.enabled = _mode != OverlayMode.Off && _mockTex != null;
            if (_mockTex != null)
            {
                _mock.texture = _mode == OverlayMode.Edges ? _edgeTex : _mockTex;
                _mock.color = new Color(1, 1, 1, _mode == OverlayMode.Edges ? Mathf.Max(_alpha, 0.85f) : _alpha);
            }
            Layout();
            Refresh();
        }

        static void Layout()
        {
            if (_mockTex == null) return;
            var r = _mock.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
            r.anchoredPosition = Vector2.zero;
            r.sizeDelta = new Vector2(Screen.width, Screen.width * (float)_mockTex.height / _mockTex.width);
        }

        static void Tick()
        {
            if (!Application.isPlaying || _root == null) { EditorApplication.update -= Tick; _root = null; return; }
            if (EditorApplication.timeSinceStartup < _nextRefresh) return;
            _nextRefresh = EditorApplication.timeSinceStartup + 0.25;
            Layout();
            Refresh();
        }

        /// 立即重画辅助线（截图前调用一次）
        public static void Refresh()
        {
            if (_guideLayer == null) return;
            for (int i = _guideLayer.childCount - 1; i >= 0; i--) Object.Destroy(_guideLayer.GetChild(i).gameObject);
            if (_showSafeArea)
            {
                var sa = Screen.safeArea;
                if (sa.width < Screen.width || sa.height < Screen.height)
                    Box(new Rect(sa.x, Screen.height - sa.yMax, sa.width, sa.height), Color.yellow, 2);
            }
            if (!_showGuides || _guides == null) return;
            var view = ReskinCore.FindViewRoot(_guides.view);
            float k = (float)Screen.width / Mathf.Max(1, _guides.refW);
            foreach (var g in _guides.guides)
            {
                if (g.group != null && _groupOn.TryGetValue(g.group, out var on) && !on) continue;
                RectTransform node = null;
                if (view != null) node = (string.IsNullOrEmpty(g.path) ? view : view.Find(g.path)) as RectTransform;
                Rect target;
                if (node != null && g.pos != null && g.pos.Length == 2 && g.size != null && g.size.Length == 2)
                    target = ReskinCore.ExpectedScreenRect(node, new Vector2(g.pos[0], g.pos[1]), new Vector2(g.size[0], g.size[1]));
                else if (g.target != null && g.target.Length == 4)
                    target = new Rect(g.target[0] * k, g.target[1] * k, g.target[2] * k, g.target[3] * k);
                else continue;
                Box(target, TargetColor, 1);
                if (node == null || !node.gameObject.activeInHierarchy) continue;
                var cur = ReskinCore.ScreenRect(node);
                float dx = cur.center.x - target.center.x, dy = cur.center.y - target.center.y;
                float dw = cur.width - target.width, dh = cur.height - target.height;
                bool off = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy), Mathf.Abs(dw), Mathf.Abs(dh)) > DeltaThreshold;
                if (off)
                {
                    Box(cur, ActualColor, 1);
                    Label(new Vector2(cur.x, cur.y - 14), $"{g.label ?? node.name} Δ{dx:+0;-0;0},{dy:+0;-0;0}" + (Mathf.Abs(dw) + Mathf.Abs(dh) > DeltaThreshold ? $" 尺寸{dw:+0;-0;0}x{dh:+0;-0;0}" : ""), ActualColor);
                }
            }
        }

        static void Box(Rect r, Color c, float t)
        {
            Line(new Rect(r.x, r.y, r.width, t), c);
            Line(new Rect(r.x, r.yMax - t, r.width, t), c);
            Line(new Rect(r.x, r.y, t, r.height), c);
            Line(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        static void Line(Rect r, Color c)
        {
            var go = new GameObject("l", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_guideLayer, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(r.x, -r.y); rt.sizeDelta = new Vector2(Mathf.Max(1, r.width), Mathf.Max(1, r.height));
            var img = go.GetComponent<Image>(); img.color = c; img.raycastTarget = false;
        }

        static void Label(Vector2 p, string s, Color c)
        {
            var go = new GameObject("t", typeof(RectTransform), typeof(Text), typeof(Shadow));
            go.transform.SetParent(_guideLayer, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(p.x, -Mathf.Max(0, p.y)); rt.sizeDelta = new Vector2(400, 16);
            var t = go.GetComponent<Text>(); t.font = _font; t.fontSize = 12; t.color = c; t.text = s; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            go.GetComponent<Shadow>().effectColor = Color.black;
        }

        /// 效果图边缘图：Sobel 梯度 > 阈值的像素画成品红，其余透明
        static Texture2D BuildEdges(Texture2D src)
        {
            int w = src.width, h = src.height;
            var px = src.GetPixels32();
            var lum = new float[w * h];
            for (int i = 0; i < px.Length; i++) lum[i] = (px[i].r * 0.299f + px[i].g * 0.587f + px[i].b * 0.114f) * (px[i].a / 255f);
            var outPx = new Color32[w * h];
            var col = new Color32(255, 30, 220, 255);
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                {
                    int i = y * w + x;
                    float gx = lum[i + 1 - w] + 2 * lum[i + 1] + lum[i + 1 + w] - lum[i - 1 - w] - 2 * lum[i - 1] - lum[i - 1 + w];
                    float gy = lum[i - 1 + w] + 2 * lum[i + w] + lum[i + 1 + w] - lum[i - 1 - w] - 2 * lum[i - w] - lum[i + 1 - w];
                    if (gx * gx + gy * gy > 120f * 120f) outPx[i] = col;
                }
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.SetPixels32(outPx); t.Apply();
            return t;
        }
    }
}
