// 文字描边/阴影对齐效果图：建「界面专用」字体材质并正确挂到本地化文字上（手工版；优先用 ReskinTextStyle / ReskinMaterialStyle）。
// 说明：下面的命名 LocaleFont_<语言> SDF <后缀> 与 m_Matrial 是 FoodFlow1 体系，其他体系照 ReskinConfig.Dump() 的探测结果改。
//
// 规则（踩过的坑）：
// 1. 字体材质按名字从 Resources/Fonts/<语言>/LocaleFont_<语言> SDF <后缀>.mat 加载，
//    新材质必须每种语言各建一份，否则切语言丢样式。
// 2. 不要改共享材质（Button OutLine Orange 等），会影响全游戏；复制一份改名为「<模块前缀> <样式>」。
// 3. LocalizeTextMeshProUGUI.Awake 会用「当前挂的材质名」反推 m_Matrial：
//    只改 m_Matrial 不换材质 → 运行时被覆盖回旧值。必须把 fontSharedMaterial 换成「该文字当前字体语言」的同名新材质。
// 4. 描边是用 Underlay 实现的（_UnderlayColor / _UnderlayDilate / _UnderlayOffsetY），不是 Outline。
using UnityEngine; using UnityEditor; using System.Text;
public class C {
  static string FontRoot => UIReskin.ReskinConfig.Data.fontRoot;   // 自动探测（ReskinConfig.Dump() 查看）
  // 语言 = FontRoot 下的子目录（自动识别，不再写死 10 种）
  static string[] Langs => System.Array.ConvertAll(AssetDatabase.GetSubFolders(FontRoot.TrimEnd('/')), d => d.Substring(d.LastIndexOf('/') + 1));

  // 第一步：建材质（已存在则只更新参数）
  static void CreateVariant(string name, string src, Color col, float dilate, float offY, StringBuilder sb){
    foreach (var l in Langs){
      var s = $"{FontRoot}{l}/LocaleFont_{l} SDF {src}.mat"; var d = $"{FontRoot}{l}/LocaleFont_{l} SDF {name}.mat";
      if (AssetDatabase.LoadAssetAtPath<Material>(s) == null){ sb.AppendLine("MISSING " + s); continue; }
      if (AssetDatabase.LoadAssetAtPath<Material>(d) == null) AssetDatabase.CopyAsset(s, d);
      var m = AssetDatabase.LoadAssetAtPath<Material>(d);
      m.SetColor("_UnderlayColor", col); m.SetFloat("_UnderlayDilate", dilate); m.SetFloat("_UnderlayOffsetY", offY); m.SetFloat("_UnderlaySoftness", 0);
      EditorUtility.SetDirty(m);
    }
  }

  // 第二步：把文字挂上新材质（按文字当前字体语言取材质）+ 同步 m_Matrial
  static void Apply(TMPro.TMP_Text t, string name, StringBuilder sb){
    var lang = t.font.name.Replace("LocaleFont_", "").Replace(" SDF", "").Trim();
    var m = AssetDatabase.LoadAssetAtPath<Material>($"{FontRoot}{lang}/LocaleFont_{lang} SDF {name}.mat");
    if (m == null){ sb.AppendLine($"no mat {lang} {name}"); return; }
    t.fontSharedMaterial = m;
    var loc = t.GetComponent("LocalizeTextMeshProUGUI");
    if (loc){ var so = new SerializedObject(loc); so.FindProperty("m_Matrial").stringValue = name; so.ApplyModifiedPropertiesWithoutUndo(); }
  }

  public static string Run(){
    var sb = new StringBuilder();
    // TODO 参考值（战令用过）：深棕/深绿/深藏青，dilate 0.65、offY -0.35；标题 0.75 / -0.45
    CreateVariant("BP Orange", "Button OutLine Orange", new Color(0.40f,0.15f,0.03f,1), 0.65f, -0.35f, sb);
    AssetDatabase.SaveAssets();

    var p = "Assets/<模块>/Prefabs/UI/UIXXX.prefab"; // TODO
    var root = PrefabUtility.LoadPrefabContents(p);
    try {
      var t = root.transform.Find("Root/BottomGroup/ButtonGroup/ButtonActivate/TextName")?.GetComponent<TMPro.TMP_Text>(); // TODO
      if (t) Apply(t, "BP Orange", sb);
      PrefabUtility.SaveAsPrefabAsset(root, p);
    } finally { PrefabUtility.UnloadPrefabContents(root); }
    return sb.Length == 0 ? "ok" : sb.ToString();
  }
}
