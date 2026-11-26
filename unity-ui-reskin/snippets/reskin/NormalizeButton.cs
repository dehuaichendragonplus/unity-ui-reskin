// 统一整理 CommonButtonGreen / CommonButtonPrice 一类按钮模板的底图与文字框。
// 现象：嵌套到各界面后常见文字框为负数尺寸（自动字号被压到最小 / 文字竖排）、
//       底图锚点异常（anchorMin=0.5, anchorMax=0）再缩放 0.4~0.7 → 按钮扁小、偏移。
// 做法：按钮设成效果图尺寸；ImgBg 撑满按钮、缩放 1；TextName 撑满按钮并留边距、开自动字号。
// 编辑器预览看不出这个问题，必须 Play 实测才会暴露。
using UnityEngine; using UnityEditor; using System.Text;
public class C {
  static void Norm(RectTransform btn, Vector2? size, float maxFs){
    if (size.HasValue) btn.sizeDelta = size.Value;
    var bg = btn.Find("ImgBg") as RectTransform;
    if (bg){ bg.anchorMin = Vector2.zero; bg.anchorMax = Vector2.one; bg.pivot = new Vector2(.5f,.5f); bg.offsetMin = Vector2.zero; bg.offsetMax = Vector2.zero; bg.localScale = Vector3.one; }
    var tn = btn.Find("TextName") as RectTransform;
    if (tn){ tn.anchorMin = Vector2.zero; tn.anchorMax = Vector2.one; tn.pivot = new Vector2(.5f,.5f); tn.offsetMin = new Vector2(12,10); tn.offsetMax = new Vector2(-12,-6);
      var t = tn.GetComponent<TMPro.TMP_Text>(); t.enableAutoSizing = true; t.fontSizeMin = 18; t.fontSizeMax = maxFs; t.fontSize = maxFs; }
  }
  public static string Run(){
    var p = "Assets/<模块>/Prefabs/UI/UIXXX.prefab"; // TODO
    var root = PrefabUtility.LoadPrefabContents(p); var sb = new StringBuilder();
    try {
      // TODO 列出需要整理的按钮路径与效果图尺寸（null 表示保持原尺寸）
      foreach (var (path, size, fs) in new (string, Vector2?, float)[]{
        ("Root/BottomGroup/ButtonGroup/ButtonActivate", new Vector2(290,104), 46f),
      }){
        var b = root.transform.Find(path) as RectTransform; if (!b){ sb.AppendLine("MISSING " + path); continue; }
        Norm(b, size, fs); sb.AppendLine("ok " + path);
      }
      PrefabUtility.SaveAsPrefabAsset(root, p);
    } finally { PrefabUtility.UnloadPrefabContents(root); }
    return sb.ToString();
  }
}
