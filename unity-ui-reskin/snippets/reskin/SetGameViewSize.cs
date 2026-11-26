// 把 Game 视图设成固定分辨率（没有就新建一个自定义尺寸）。
// 用途：让运行时截图与效果图同尺寸，才能逐像素叠图；或切手机 / Pad 比例测试。
// 尺寸 = 效果图手机画面尺寸（crop_effect.py 的输出尺寸）；常见：768x1664 / 768x1536 / 768x1920，手机 1080x2340，iPad 1536x2048。也可直接用 UIReskin.ReskinCore.SetGameViewSize(w, h)。
// 注意：Util.IsLE_16_10()（长宽比 <= 1.605）为 true 时 UIManager 会加载 *_Pad.prefab。
using UnityEngine; using UnityEditor; using System; using System.Reflection;
public class C {
  static int Ensure(int w, int h, string label){
    var asm = typeof(Editor).Assembly; var sizesType = asm.GetType("UnityEditor.GameViewSizes");
    var inst = typeof(ScriptableSingleton<>).MakeGenericType(sizesType).GetProperty("instance").GetValue(null);
    var group = sizesType.GetMethod("GetGroup").Invoke(inst, new object[]{ (int)GameViewSizeGroupType.Standalone });
    var gvs = asm.GetType("UnityEditor.GameViewSize"); var gvst = asm.GetType("UnityEditor.GameViewSizeType");
    int count = (int)group.GetType().GetMethod("GetTotalCount").Invoke(group, null);
    for (int i = 0; i < count; i++){
      var s = group.GetType().GetMethod("GetGameViewSize").Invoke(group, new object[]{ i });
      if ((int)gvs.GetProperty("width").GetValue(s) == w && (int)gvs.GetProperty("height").GetValue(s) == h) return i;
    }
    var ns = gvs.GetConstructor(new[]{ gvst, typeof(int), typeof(int), typeof(string) }).Invoke(new object[]{ Enum.Parse(gvst, "FixedResolution"), w, h, label });
    group.GetType().GetMethod("AddCustomSize").Invoke(group, new[]{ ns }); return count;
  }
  public static string Run(){
    int idx = Ensure(768, 1536, "Effect 768x1536"); // TODO 按效果图画面尺寸修改
    var gvType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
    foreach (var o in Resources.FindObjectsOfTypeAll(gvType)){
      gvType.GetProperty("selectedSizeIndex", BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).SetValue(o, idx);
      ((EditorWindow)o).Repaint();
    }
    return "idx=" + idx + "（Screen 尺寸要到下一帧/重绘后才生效，先截一次图再开界面）";
  }
}
