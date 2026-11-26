// 入场动画快进：截图 / 测量前把界面动画直接推到最后一帧，省掉每轮等待，也避免截到动画中间态。
// 覆盖 DOTween（反射调用，不强依赖程序集）、Animator、Legacy Animation、PlayableDirector。
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Playables;

namespace UIReskin
{
    public static class ReskinAnim
    {
        public static string SkipAll(Transform root = null)
        {
            int tw = CompleteDOTween();
            int an = 0, la = 0, pd = 0;
            Component[] Get(Type t) => root != null
                ? root.GetComponentsInChildren(t, true)
                : UnityEngine.Object.FindObjectsByType(t, FindObjectsInactive.Exclude, FindObjectsSortMode.None).Cast<Component>().ToArray();

            foreach (Animator a in Get(typeof(Animator)))
            {
                if (!a.isActiveAndEnabled || a.runtimeAnimatorController == null) continue;
                // 非循环状态推进到末尾；循环状态推进一段时间不影响静态布局
                for (int i = 0; i < 20; i++) a.Update(0.25f);
                an++;
            }
            foreach (Animation a in Get(typeof(Animation)))
            {
                if (!a.isActiveAndEnabled || !a.isPlaying) continue;
                foreach (AnimationState s in a) if (a.IsPlaying(s.name) && s.wrapMode != WrapMode.Loop) s.normalizedTime = 1f;
                a.Sample(); la++;
            }
            foreach (PlayableDirector d in Get(typeof(PlayableDirector)))
            {
                if (!d.isActiveAndEnabled || d.state != PlayState.Playing || d.extrapolationMode == DirectorWrapMode.Loop) continue;
                d.time = d.duration; d.Evaluate(); pd++;
            }
            return $"DOTween={tw} Animator={an} Animation={la} Director={pd}";
        }

        static int CompleteDOTween()
        {
            var t = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("DG.Tweening.DOTween")).FirstOrDefault(x => x != null);
            if (t == null) return -1;
            // DOTween.CompleteAll(bool withCallbacks)：带回调完成，保证动画结束回调里的显隐逻辑也执行
            var m = t.GetMethod("CompleteAll", new[] { typeof(bool) });
            if (m == null) return -1;
            return (int)m.Invoke(null, new object[] { true });
        }
    }
}
