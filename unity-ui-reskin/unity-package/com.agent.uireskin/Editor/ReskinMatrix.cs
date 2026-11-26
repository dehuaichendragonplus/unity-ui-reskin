// 一键「打开界面 → 快进动画 → 导出 runtime.json → 截图」，以及多分辨率批量截图矩阵（移植自 FoodFlow1）。
// 效果图画面尺寸因项目而异，首次使用时确认并记进项目记忆（references/reskin.md 第 3 节）。
// 打开界面的方式因界面而异，由调用方传一个协程（在游戏主循环里执行，Screen 才是 Game 视图尺寸）。
// 用法（execute_code，Play 中）：
//   ReskinMatrix.Snap("UIPopupBattlePassActivate", OpenAct, "/abs/out/phone", prefab);           // 单分辨率
//   ReskinMatrix.Run("UIPopupBattlePassActivate", OpenAct, "/abs/out/matrix",
//                    new[]{"768x1664","1080x2400","1170x2532","1536x2048","1600x2560"}, prefab, prefabPad);
//   完成后日志 [RESKIN] matrix done；再跑 python matrix.py 拼总览图。
// 注意：换分辨率后必须重新打开界面（UIManager 打开时才决定加载手机还是 _Pad 预制体）。
using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace UIReskin
{
    public static class ReskinMatrix
    {
        public static float SettleSeconds = 0.6f;

        /// size 例如 "768x1664"；为空则用当前 Game 视图尺寸
        public static string Snap(string viewName, Func<IEnumerator> open, string outDir, string prefab = null, string size = null)
        {
            var host = ReskinProbe.Host();
            if (host == null) return "no host（需要 Play 模式）";
            if (!string.IsNullOrEmpty(size)) { host.StartCoroutine(RunCo(viewName, open, outDir, new[] { size }, prefab, prefab, false)); return "snap started " + size; }
            host.StartCoroutine(SnapCo(viewName, open, outDir, prefab, null));
            return "snap started → " + outDir;
        }

        public static string Run(string viewName, Func<IEnumerator> open, string outDir, string[] sizes, string prefab = null, string prefabPad = null)
        {
            var host = ReskinProbe.Host();
            if (host == null) return "no host（需要 Play 模式）";
            host.StartCoroutine(RunCo(viewName, open, outDir, sizes, prefab, prefabPad, true));
            return $"matrix started {sizes.Length} sizes → {outDir}";
        }

        static IEnumerator RunCo(string viewName, Func<IEnumerator> open, string outDir, string[] sizes, string prefab, string prefabPad, bool subDirs)
        {
            outDir = ReskinCore.Abs(outDir);
            foreach (var s in sizes)
            {
                var wh = s.Split('x');
                int w = int.Parse(wh[0]), h = int.Parse(wh[1]);
                ReskinCore.SetGameViewSize(w, h);
                for (int i = 0; i < 30 && (Screen.width != w || Screen.height != h); i++) yield return null;
                if (Screen.width != w || Screen.height != h) Debug.LogWarning($"[RESKIN] Game 视图未切到 {s}（当前 {Screen.width}x{Screen.height}），可能是窗口放不下；截图按实际尺寸");
                bool pad = (float)h / w <= ReskinConfig.Data.padAspectMax;
                yield return SnapCo(viewName, open, subDirs ? Path.Combine(outDir, s) : outDir, pad && prefabPad != null ? prefabPad : prefab, null);
            }
            if (subDirs) Debug.Log("[RESKIN] matrix done → " + outDir);
        }

        public static IEnumerator SnapCo(string viewName, Func<IEnumerator> open, string outDir, string prefab, Action done)
        {
            outDir = ReskinCore.Abs(outDir);
            if (open != null) yield return open();
            yield return new WaitForSecondsRealtime(SettleSeconds);
            ReskinAnim.SkipAll();
            yield return null; yield return null;
            Directory.CreateDirectory(outDir);
            var viewInst = ReskinCore.FindViewRoot(viewName) ?? ReskinCore.FindViewRoot(viewName + ReskinConfig.Data.padSuffix);
            var actualName = viewInst != null ? viewInst.name.Replace("(Clone)", "") : viewName;
            Debug.Log("[RESKIN] " + ReskinProbe.Dump(actualName, Path.Combine(outDir, "runtime.json"), prefab));
            // 干净截图（测量用）+ 带辅助线截图（人看用）
            bool hadOverlay = ReskinOverlay.Active;
            ReskinOverlay.Active = false;
            yield return ReskinProbe.CaptureCo(Path.Combine(outDir, "runtime.png"));
            if (hadOverlay)
            {
                ReskinOverlay.Active = true; ReskinOverlay.Refresh();
                yield return null;
                yield return ReskinProbe.CaptureCo(Path.Combine(outDir, "runtime_guides.png"));
            }
            Debug.Log($"[RESKIN] snap ready {Screen.width}x{Screen.height} → {outDir}");
            done?.Invoke();
        }
    }
}
