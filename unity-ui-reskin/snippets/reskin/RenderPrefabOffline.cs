// 编辑器离屏渲染预制体到 PNG（不进 Play、不开 Prefab Stage）。用于换图后快速预览、与效果图并排。
// 通过 Unity MCP execute_code 运行（safety_checks=false，因为要写 Library 下的 PNG）。
// 注意：编辑器里不跑本地化/业务代码，文字显示的是预制体里存的静态文本；最终结论以 Play 实测为准。
using UnityEngine; using UnityEngine.UI; using UnityEditor; using UnityEditor.SceneManagement; using System.IO;
public class C {
  static void Mv(GameObject g, UnityEngine.SceneManagement.Scene s){ UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(g,s); }
  static void Shot(string prefab, string outPng, int w, int h, bool transparent){
    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefab);
    var scene = EditorSceneManager.NewPreviewScene();
    try {
      var camGo = new GameObject("cam"); Mv(camGo, scene);
      var cam = camGo.AddComponent<Camera>(); cam.orthographic = true; cam.clearFlags = CameraClearFlags.SolidColor;
      cam.backgroundColor = transparent ? new Color(0,0,0,0) : new Color(0.15f,0.15f,0.15f,1); cam.scene = scene;
      cam.transform.position = new Vector3(0,0,-100); cam.farClipPlane = 2000;
      var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32); cam.targetTexture = rt;
      var cgo = new GameObject("canvas"); Mv(cgo, scene);
      var canvas = cgo.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cam; canvas.planeDistance = 50;
      // 参考分辨率与适配方式照项目根画布的 CanvasScaler 改（FoodFlow1 是 768x1996、按宽适配）；有自管参考框的界面不适合离屏渲染
      var sc = cgo.AddComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(768,1996); sc.matchWidthOrHeight = 0;
      var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene); inst.transform.SetParent(cgo.transform, false);
      var irt = inst.GetComponent<RectTransform>();
      if (irt && irt.sizeDelta == Vector2.zero){ irt.anchorMin = Vector2.zero; irt.anchorMax = Vector2.one; irt.offsetMin = Vector2.zero; irt.offsetMax = Vector2.zero; }
      Canvas.ForceUpdateCanvases(); cam.Render(); Canvas.ForceUpdateCanvases(); cam.Render();
      RenderTexture.active = rt; var tex = new Texture2D(w, h, TextureFormat.RGBA32, false); tex.ReadPixels(new Rect(0,0,w,h),0,0); tex.Apply(); RenderTexture.active = null;
      File.WriteAllBytes(outPng, tex.EncodeToPNG()); Object.DestroyImmediate(tex); rt.Release();
    } finally { EditorSceneManager.ClosePreviewScene(scene); }
  }
  public static string Run(){
    Directory.CreateDirectory("Library/ReskinShots");
    // TODO 按需修改：预制体路径、输出名、尺寸（与效果图一致）
    Shot("Assets/<模块>/Prefabs/UI/UIXXX.prefab", "Library/ReskinShots/UIXXX.png", 768, 1920, false);
    return "ok";
  }
}
