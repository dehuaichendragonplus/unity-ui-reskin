// UIReskin 工具链公共部分：屏幕像素矩形、节点路径、Game 视图尺寸、JSON 数据结构。
// 坐标约定：所有「屏幕矩形」都是 Game 视图像素、左上角为原点、y 向下（和效果图 PNG 一致）。
// 配套规范与 Python 脚本见 ~/.claude/skills/unity-ui-reskin（references/reskin.md）；项目适配由 ReskinConfig 自动探测
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UIReskin
{
    public static class ReskinCore
    {
        // ---------- 坐标 ----------
        public static Camera CanvasCamera(Transform t)
        {
            var c = t.GetComponentInParent<Canvas>();
            if (c == null) return null;
            c = c.rootCanvas;
            return c.renderMode == RenderMode.ScreenSpaceOverlay ? null : c.worldCamera;
        }

        /// 世界坐标四角 → 屏幕像素矩形（左上原点）
        public static Rect ScreenRect(Vector3[] worldCorners, Camera cam)
        {
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var w in worldCorners)
            {
                var p = RectTransformUtility.WorldToScreenPoint(cam, w);
                x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); y0 = Mathf.Min(y0, p.y); y1 = Mathf.Max(y1, p.y);
            }
            return new Rect(x0, Screen.height - y1, x1 - x0, y1 - y0);
        }

        public static Rect ScreenRect(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return ScreenRect(c, CanvasCamera(rt));
        }

        /// 父节点本地单位 → 屏幕像素 的比例（用于把像素偏差换算成 anchoredPosition）
        public static float PixelsPerParentUnit(RectTransform rt)
        {
            var canvas = rt.GetComponentInParent<Canvas>()?.rootCanvas;
            if (canvas == null) return 1f;
            var parent = rt.parent != null ? rt.parent : rt;
            float rel = parent.lossyScale.x / Mathf.Max(1e-6f, canvas.transform.lossyScale.x);
            return canvas.scaleFactor * rel;
        }

        /// 按「锚点 + 目标 anchoredPosition/sizeDelta」计算节点应在的屏幕矩形（辅助线跟随锚点用）
        public static Rect ExpectedScreenRect(RectTransform rt, Vector2 pos, Vector2 size)
        {
            var parent = rt.parent as RectTransform;
            if (parent == null) return ScreenRect(rt);
            var pr = parent.rect;
            Vector2 aMin = pr.min + Vector2.Scale(rt.anchorMin, pr.size);
            Vector2 aMax = pr.min + Vector2.Scale(rt.anchorMax, pr.size);
            Vector2 sz = (aMax - aMin) + size;
            Vector2 pivotPos = aMin + Vector2.Scale(aMax - aMin, rt.pivot) + pos;
            Vector2 min = pivotPos - Vector2.Scale(sz, rt.pivot);
            Vector2 max = min + sz;
            var s = rt.localScale;
            Vector3 P(Vector2 v) => parent.TransformPoint(new Vector3(pivotPos.x + (v.x - pivotPos.x) * s.x, pivotPos.y + (v.y - pivotPos.y) * s.y, 0));
            var corners = new[] { P(min), P(new Vector2(min.x, max.y)), P(max), P(new Vector2(max.x, min.y)) };
            return ScreenRect(corners, CanvasCamera(rt));
        }

        /// 在预制体里按路径找节点；列表实例路径（.../Content/2/Root/...）找不到时，映射到 Content 下所有同结构的模板节点
        public static List<Transform> FindInPrefab(Transform root, string path)
        {
            var res = new List<Transform>();
            var t = string.IsNullOrEmpty(path) ? root : root.Find(path);
            if (t != null) { res.Add(t); return res; }
            var i = path.IndexOf("/Content/");
            if (i < 0) return res;
            var contentPath = path.Substring(0, i + 8);
            var rest = path.Substring(i + 9);
            var j = rest.IndexOf('/');
            if (j < 0) return res;
            var suffix = rest.Substring(j + 1);
            var content = root.Find(contentPath);
            if (content == null) return res;
            foreach (Transform item in content)
            {
                var hit = item.Find(suffix);
                if (hit != null) res.Add(hit);
            }
            return res;
        }

        public static string PathOf(Transform t, Transform root)
        {
            if (t == root) return "";
            var s = t.name;
            while (t.parent != null && t.parent != root) { t = t.parent; s = t.name + "/" + s; }
            return s;
        }

        public static Transform FindViewRoot(string viewName)
        {
            // 运行时界面实例名一般是「预制体名(Clone)」；也接受不带 (Clone)
            foreach (var rt in UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (rt.name == viewName + "(Clone)" || rt.name == viewName) return rt;
            return null;
        }

        // ---------- Game 视图 ----------
        static Type GameViewType => typeof(Editor).Assembly.GetType("UnityEditor.GameView");

        public static int EnsureGameViewSize(int w, int h)
        {
            var asm = typeof(Editor).Assembly;
            var sizesType = asm.GetType("UnityEditor.GameViewSizes");
            var inst = typeof(ScriptableSingleton<>).MakeGenericType(sizesType).GetProperty("instance").GetValue(null);
            // 取 Game 视图当前用的尺寸分组（构建目标为 iOS / Android 时不是 Standalone，写错分组索引会对不上）
            var curGroup = (int)sizesType.GetProperty("currentGroupType").GetValue(inst);
            var group = sizesType.GetMethod("GetGroup").Invoke(inst, new object[] { curGroup });
            var gvs = asm.GetType("UnityEditor.GameViewSize");
            var gvst = asm.GetType("UnityEditor.GameViewSizeType");
            int count = (int)group.GetType().GetMethod("GetTotalCount").Invoke(group, null);
            for (int i = 0; i < count; i++)
            {
                var s = group.GetType().GetMethod("GetGameViewSize").Invoke(group, new object[] { i });
                if ((int)gvs.GetProperty("width").GetValue(s) == w && (int)gvs.GetProperty("height").GetValue(s) == h
                    && gvs.GetProperty("sizeType").GetValue(s).ToString() == "FixedResolution") return i;
            }
            var ns = gvs.GetConstructor(new[] { gvst, typeof(int), typeof(int), typeof(string) })
                .Invoke(new object[] { Enum.Parse(gvst, "FixedResolution"), w, h, $"Reskin {w}x{h}" });
            group.GetType().GetMethod("AddCustomSize").Invoke(group, new[] { ns });
            return count;
        }

        /// 设置 Game 视图为固定分辨率。Screen 尺寸在下一帧重绘后生效。
        public static void SetGameViewSize(int w, int h)
        {
            int idx = EnsureGameViewSize(w, h);
            foreach (var o in Resources.FindObjectsOfTypeAll(GameViewType))
            {
                GameViewType.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(o, idx);
                ((EditorWindow)o).Repaint();
            }
        }

        /// 相对路径按 Unity 项目根目录解析（MCP 的 execute_code 禁止写绝对路径，统一用相对路径如 "Library/ReskinShots/xxx"）
        public static string Abs(string p) => string.IsNullOrEmpty(p) || System.IO.Path.IsPathRooted(p) ? p : System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectRoot, p));

        public static string ProjectRoot => System.IO.Path.GetDirectoryName(Application.dataPath);
        public static string ShotsDir(string sub)
        {
            var d = System.IO.Path.Combine(Abs(ReskinConfig.Data.shotsRoot), sub);
            System.IO.Directory.CreateDirectory(d);
            return d;
        }
    }

    // ---------- JSON 结构（与 Python 侧 diff.py 约定一致） ----------
    [Serializable] public class RNode
    {
        public string path; public bool visible;
        public float[] rect;          // 屏幕像素 x,y,w,h（左上原点）
        public string sprite; public string imageType; public float alpha;
        public string text; public string term; public float[] textRect; /* 文字实际字形包围盒（屏幕像素） */ public float fontSize; public bool autoSize; public float fontSizeMax;
        public string material; public float[] color; public float[] underlayColor; public float underlayDilate; public float underlayOffsetY;
        public float[] anchorMin, anchorMax, pivot, pos, size, scale;
        public float ppu;             // 父节点本地单位 → 屏幕像素
        public bool hasLayoutParent;  // 父节点有 LayoutGroup：位置由布局决定，不能直接改 anchoredPosition
    }

    [Serializable] public class RuntimeDump
    {
        public string view; public string prefab; public int screenW, screenH; public bool pad; public string time;
        public List<RNode> nodes = new List<RNode>();
    }

    [Serializable] public class Fix
    {
        public string path; public string op;   // move / resize / fontSize
        public float dx, dy;                      // move/resize：本地单位（已换算）
        public float value;                       // fontSize
        public string reason;
    }

    [Serializable] public class FixList { public string prefab; public List<Fix> fixes = new List<Fix>(); }

    [Serializable] public class Guide
    {
        public string path; public string group; public string label;
        public float[] target;     // 参考分辨率下效果图矩形 x,y,w,h
        public float[] pos, size;  // 目标 anchoredPosition / sizeDelta（跟随锚点画线用；为空则用 target 按宽度缩放）
    }

    [Serializable] public class GuideList { public string view; public int refW, refH; public List<Guide> guides = new List<Guide>(); }
}
