// Play 模式下在「游戏主循环」里打开界面（挂到 DontDestroyOnLoad 里现成的 MonoBehaviour 跑协程）。
// 为什么：execute_code 跑在编辑器线程，读到的 Screen 是编辑器窗口尺寸，UIManager 会误判 Pad / 手机。
// 关闭战令等全屏界面会触发大厅任务队列（评分弹窗、帮助页等），所以先发两次 EVENT_UI_CLOSE_ALL_POPUP 清场。
// 协程结束时打 [RESKIN] 日志：用 get_console_logs(filter_text="RESKIN") 确认就绪后再截图。
using UnityEngine; using System.Collections; using Framework.UI; using Framework.Utils;
using UI.Activity.BattlePass; // TODO 换成目标界面的命名空间
public class C {
  static IEnumerator Co(){
    EventBus.Send(new EVENT_UI_CLOSE_ALL_POPUP());
    UIManager.Instance.Close<UIBattlePass>();           // TODO 目标界面
    yield return new WaitForSeconds(0.8f);
    EventBus.Send(new EVENT_UI_CLOSE_ALL_POPUP());
    yield return new WaitForSeconds(0.4f);
    if (UIManager.Instance.GetView<UIBattlePass>() == null) UIBattlePass.Open();   // TODO
    yield return new WaitForSeconds(1.5f);               // 等入场动画结束
    Debug.Log("[RESKIN] ready screen=" + Screen.width + "x" + Screen.height + " pad=" + Util.IsLE_16_10());
  }
  public static string Run(){
    MonoBehaviour host = null;
    foreach (var m in Object.FindObjectsOfType<MonoBehaviour>())
      if (m.isActiveAndEnabled && m.gameObject.scene.name == "DontDestroyOnLoad"){ host = m; break; }
    if (host == null) return "no host";
    host.StartCoroutine(Co()); return "started on " + host.name;
  }
}
