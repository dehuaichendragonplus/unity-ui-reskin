// Play 模式下在「游戏主循环」里打开界面并一键测量：打开 → 等动画 → 导出 runtime.json + 截图（UIReskin.ReskinMatrix.Snap）。
// 为什么在主循环里开：execute_code 跑在编辑器线程，读到的 Screen 是编辑器窗口尺寸，UI 管理器可能误判 Pad / 手机。
// 用法：改 TODO 三处后整段 execute_code；完成后 get_console_logs(filter_text="RESKIN") 看到 "snap ready WxH" 再读产物。
// 打开方式不知道时先跑 snippets/review/OpenViews.cs（自动找 UI 管理器的打开方法），把它返回的调用写进 Open()。
// 例（FoodFlow1 框架写法）：examples/FoodFlow1/OpenViewInPlayLoop.cs。
using System.Collections;
using UnityEngine;

public class C
{
    const string VIEW = "UIXXX";                                  // TODO 运行时界面根节点名（预制体名，不含 (Clone)）
    const string PREFAB = "Assets/<模块>/Prefabs/UI/UIXXX.prefab"; // TODO 预制体路径（ReskinApply 写回用）
    const string SIZE = "768x1664";                               // TODO 效果图手机画面尺寸

    static IEnumerator Open()
    {
        // TODO 打开界面，例如：UIManager.Instance.Open<UIXXX>();  或 GameManager.UISystem.ShowView<UIXXX>();
        //      需要先清场（关闭其他弹窗）时写在这里，并多等一会儿。
        yield return new WaitForSecondsRealtime(0.5f);
    }

    public static string Run()
    {
        if (!Application.isPlaying) return "需要 Play 模式";
        return UIReskin.ReskinMatrix.Snap(VIEW, Open, "Library/ReskinShots/" + VIEW, PREFAB, SIZE);
    }
}
