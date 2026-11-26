// 覆盖贴图后修复 Multiple 模式（spriteMode: 2）的切片矩形。
// 现象：新图尺寸与旧图不同 → 旧切片越界 → Sprite 加载为 NULL → 界面白底 / 丢图，且没有任何报错。
// 做法：单切片的 Multiple 图把 rect 改成整张新图尺寸，spriteID/internalID 不变（预制体引用不会断）。
using UnityEngine; using UnityEditor; using UnityEditor.U2D.Sprites; using System.Text;
public class C {
  public static string Run(){
    var sb = new StringBuilder();
    var dir = "Assets/<模块>/Textures/UI"; // TODO 换成本次替换的贴图目录
    foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[]{ dir })){
      var path = AssetDatabase.GUIDToAssetPath(guid);
      var imp = AssetImporter.GetAtPath(path) as TextureImporter;
      if (imp == null || imp.spriteImportMode != SpriteImportMode.Multiple) continue;
      var fac = new SpriteDataProviderFactories(); fac.Init();
      var dp = fac.GetSpriteEditorDataProviderFromObject(imp); dp.InitSpriteEditorDataProvider();
      int w, h; dp.GetDataProvider<ITextureDataProvider>().GetTextureActualWidthAndHeight(out w, out h);
      var rects = dp.GetSpriteRects();
      if (rects.Length != 1){ sb.AppendLine($"SKIP(多切片，需人工处理) {path} count={rects.Length}"); continue; }
      var r = rects[0];
      if (r.rect.width == w && r.rect.height == h && r.rect.x == 0 && r.rect.y == 0) continue;
      sb.AppendLine($"FIX {path} {r.rect} -> (0,0,{w},{h}) id={r.spriteID}");
      r.rect = new Rect(0, 0, w, h); dp.SetSpriteRects(rects); dp.Apply(); imp.SaveAndReimport();
    }
    return sb.Length == 0 ? "无需修复" : sb.ToString();
  }
}
