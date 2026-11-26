// 用途（Play 中）：在屏幕上临时摆一块对照板，左 = 美术字体 + 美术材质，右 = TMP_Font_Normal + 当前映射的材质，放大看描边 / 阴影差异（references/review.md §6.3）。
// 模板：EN / NM / PAIRS 是 BubbleWord3 的路径，用前换成本项目的「美术材质 | 规范字体材质」对。
// 只存在于本次 Play；再运行会替换旧板，退出 Play 自动消失。PAIRS 里填"美术材质路径 | Normal 材质路径"。运行：skip_refresh=true，然后 capture_game_view。
using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;

public class FxBoard
{
    const string EN = "Assets/Res/FishPool/_Resources/Export/Fonts/En/";
    const string NM = "Assets/Res/Launcher/Code/Resources/Fonts/Normal/";
    static readonly string[] PAIRS =
    {
        EN + "LocaleFont_En SDF Shadow.mat|" + NM + "Material/TMP_Font_Normal OutLine Black.mat",
        EN + "LocaleFont_En SDF ButtonGreen.mat|" + NM + "Material/TMP_Font_Normal 3ButtonGreen.mat",
        EN + "LocaleFont_En SDF ButtonBlue.mat|" + NM + "Material/TMP_Font_Normal 3ButtonBlue.mat",
        EN + "LocaleFont_En SDF Shadow Orange.mat|" + NM + "Material/TMP_Font_Normal 3Shadow Orange.mat",
    };
    public static string Run()
    {
        var old = GameObject.Find("__FxBoard"); if (old) UnityEngine.Object.Destroy(old);
        var en = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(EN + "LocaleFont_En SDF.asset");
        var nm = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(NM + "TMP_Font_Normal.asset");
        var go = new GameObject("__FxBoard", typeof(Canvas), typeof(CanvasScaler));
        var cv = go.GetComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 32000;
        var cs = go.GetComponent<CanvasScaler>(); cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; cs.referenceResolution = new Vector2(768, 1536); cs.matchWidthOrHeight = 0;
        var bg = new GameObject("bg", typeof(RectTransform), typeof(Image)); bg.transform.SetParent(go.transform, false);
        var brt = (RectTransform)bg.transform; brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.sizeDelta = Vector2.zero;
        bg.GetComponent<Image>().color = new Color(0.55f, 0.75f, 0.95f, 1f);
        for (int i = 0; i < PAIRS.Length; i++)
        {
            var pr = PAIRS[i].Split('|');
            for (int side = 0; side < 2; side++)
            {
                var t = new GameObject("fx" + i + side, typeof(RectTransform), typeof(TextMeshProUGUI)); t.transform.SetParent(go.transform, false);
                var rt = (RectTransform)t.transform; rt.anchorMin = rt.anchorMax = new Vector2(side == 0 ? 0.27f : 0.73f, 1f);
                rt.sizeDelta = new Vector2(370, 200); rt.anchoredPosition = new Vector2(0, -150 - i * 230);
                var tmp = t.GetComponent<TextMeshProUGUI>();
                tmp.font = side == 0 ? en : nm;
                tmp.fontSharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(pr[side]);
                tmp.fontSize = 64; tmp.alignment = TextAlignmentOptions.Center; tmp.enableWordWrapping = false;
                tmp.text = side == 0 ? "Play 123" : "Play 123 设置";
            }
        }
        return "对照板已显示：左 = 美术 En，右 = 当前 Normal";
    }
}
