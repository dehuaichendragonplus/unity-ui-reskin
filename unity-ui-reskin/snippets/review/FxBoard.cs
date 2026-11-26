// 用途（Play 中）：在屏幕上临时摆一块对照板，左右各一组「字体 + 材质」，放大比较描边 / 阴影差异（references/review.md §6.3）。
// PAIRS 每行 4 段，用 | 分隔：左字体 .asset | 左材质 .mat | 右字体 .asset | 右材质 .mat（例：美术版字体 + 美术材质 对 修复后字体 + 映射后的预设材质）。
// 路径是本项目的，按项目事实或现场调研填；只存在于本次 Play，再运行会替换旧板，退出 Play 自动消失。运行：skip_refresh=true，然后 capture_game_view。
using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;

public class FxBoard
{
    static readonly string[] PAIRS =
    {
        // "Assets/.../ArtFont SDF.asset|Assets/.../ArtFont SDF Shadow.mat|Assets/.../Normal.asset|Assets/.../Normal OutLine.mat",
    };
    const string SAMPLE_LEFT = "Play 123", SAMPLE_RIGHT = "Play 123 设置";
    static readonly Vector2 REF = new Vector2(768, 1536);   // 项目 UI 参考分辨率（项目事实 ui.refWidth / refHeight）

    public static string Run()
    {
        if (!Application.isPlaying) return "需要 Play 模式";
        if (PAIRS.Length == 0) return "先在 PAIRS 里填要对比的「字体 | 材质 | 字体 | 材质」";
        var old = GameObject.Find("__FxBoard"); if (old) UnityEngine.Object.Destroy(old);
        var go = new GameObject("__FxBoard", typeof(Canvas), typeof(CanvasScaler));
        var cv = go.GetComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 32000;
        var cs = go.GetComponent<CanvasScaler>(); cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; cs.referenceResolution = REF; cs.matchWidthOrHeight = 0;
        var bg = new GameObject("bg", typeof(RectTransform), typeof(Image)); bg.transform.SetParent(go.transform, false);
        var brt = (RectTransform)bg.transform; brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.sizeDelta = Vector2.zero;
        bg.GetComponent<Image>().color = new Color(0.55f, 0.75f, 0.95f, 1f);
        var miss = "";
        for (int i = 0; i < PAIRS.Length; i++)
        {
            var pr = PAIRS[i].Split('|');
            if (pr.Length < 4) { miss += $" 第 {i + 1} 行不是 4 段;"; continue; }
            for (int side = 0; side < 2; side++)
            {
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(pr[side * 2].Trim());
                var mat = AssetDatabase.LoadAssetAtPath<Material>(pr[side * 2 + 1].Trim());
                if (font == null || mat == null) { miss += $" 第 {i + 1} 行{(side == 0 ? "左" : "右")}缺资源;"; continue; }
                var t = new GameObject("fx" + i + side, typeof(RectTransform), typeof(TextMeshProUGUI)); t.transform.SetParent(go.transform, false);
                var rt = (RectTransform)t.transform; rt.anchorMin = rt.anchorMax = new Vector2(side == 0 ? 0.27f : 0.73f, 1f);
                rt.sizeDelta = new Vector2(370, 200); rt.anchoredPosition = new Vector2(0, -150 - i * 230);
                var tmp = t.GetComponent<TextMeshProUGUI>();
                tmp.font = font; tmp.fontSharedMaterial = mat; tmp.fontSize = 64; tmp.alignment = TextAlignmentOptions.Center;
                tmp.overflowMode = TextOverflowModes.Overflow;
                tmp.text = side == 0 ? SAMPLE_LEFT : SAMPLE_RIGHT;
            }
        }
        return "对照板已显示（左 / 右）" + (miss.Length > 0 ? "；" + miss : "");
    }
}
