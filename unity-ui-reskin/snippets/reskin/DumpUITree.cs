// 导出预制体（编辑态）或运行时实例的 UI 节点树：锚点、位置、尺寸、缩放、Sprite、Image 类型、
// 文字字号/材质/本地化 Term 与 m_Matrial。改之前先导一份，改完再导一份对比。
// 运行时模式还会输出屏幕像素矩形，可直接和效果图坐标比。
using UnityEngine; using UnityEngine.UI; using UnityEditor; using System.Text; using System.Linq;
public class C {
  static string Path(Transform t, Transform r){ var s = t.name; while (t.parent && t.parent != r){ t = t.parent; s = t.name + "/" + s; } return s; }
  static void Dump(Transform root, Transform sub, StringBuilder sb, Camera cam, float screenH){
    foreach (var t in sub.GetComponentsInChildren<RectTransform>(true)){
      var img = t.GetComponent<Image>(); var tmp = t.GetComponent<TMPro.TMP_Text>(); var loc = t.GetComponent("LocalizeTextMeshProUGUI");
      string extra = "";
      if (img) extra += $" IMG[{(img.sprite ? img.sprite.name : "NULL")} {img.type} a={img.color.a:F2}]";
      if (tmp) extra += $" TMP[fs={tmp.fontSize:F0}{(tmp.enableAutoSizing ? "(auto " + tmp.fontSizeMax + ")" : "")} mat={tmp.fontSharedMaterial?.name}]";
      if (loc){ var so = new SerializedObject(loc); extra += $" LOC[{so.FindProperty("Term")?.stringValue}|{so.FindProperty("m_Matrial")?.stringValue}]"; }
      if (cam){ var c = new Vector3[4]; t.GetWorldCorners(c); var p0 = RectTransformUtility.WorldToScreenPoint(cam, c[0]); var p2 = RectTransformUtility.WorldToScreenPoint(cam, c[2]);
        extra += $" screen=({p0.x:F0},{screenH - p2.y:F0})-({p2.x:F0},{screenH - p0.y:F0})"; }
      sb.AppendLine($"{Path(t, root)} act={t.gameObject.activeSelf} amin={t.anchorMin} amax={t.anchorMax} piv={t.pivot} pos={t.anchoredPosition} size={t.sizeDelta} scale={t.localScale}{extra}");
    }
  }
  public static string Run(){
    var sb = new StringBuilder();
    // 模式一：编辑态预制体
    var p = "Assets/<模块>/Prefabs/UI/UIXXX.prefab"; // TODO
    var root = PrefabUtility.LoadPrefabContents(p);
    try { Dump(root.transform, root.transform, sb, null, 0); } finally { PrefabUtility.UnloadPrefabContents(root); }
    // 模式二：运行时实例（Play 中），取消注释使用
    // var go = Object.FindObjectsOfType<RectTransform>().FirstOrDefault(x => x.name == "UIXXX(Clone)");
    // var cam = go.GetComponentInParent<Canvas>().worldCamera; Dump(go.transform, go.transform, sb, cam, Screen.height);
    return sb.ToString();
  }
}
