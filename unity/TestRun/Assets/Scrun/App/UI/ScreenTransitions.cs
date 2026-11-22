using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

namespace Scrun.App
{
    // 화면이 바뀔 때의 움직임(계획 0068, 상준 미리보기 ui.css pushIn·backIn·fadeIn, fx.css tabInR·tabInL).
    //  - Push: 새 화면이 오른쪽에서 들어오고(100% → 0), 옛 화면은 왼쪽으로 30% 밀리며 흐려짐(불투명 .4)
    //  - Back: 반대로(옛 화면이 왼쪽 30%에서 돌아오고, 닫히는 화면은 오른쪽으로 나감)
    //  - Fade: 겹쳐 흐려짐(흐름 시작·로그인 → 홈)
    //  - TabRight·TabLeft: 탭 바는 그대로, 새 탭이 그 방향에서 28 밀려 들어오며 나타남
    // 시간 250ms(탭 200), ease-out cubic-bezier(0.2, 0, 0, 1) = 토큰 duration/base·ease/out.
    // Navigator는 화면 로직(OnShow·OnHide·OnDestroy)을 바로 부르고, 여기서는 보이는 것만 늦게 정리한다.
    public enum NavMotion { None, Push, Back, Fade, TabRight, TabLeft }

    public sealed class ScreenTransitions
    {
        const int BaseMs = 250;
        const int TabMs = 200;
        const float TabShift = 28f;

        sealed class Run
        {
            public ValueAnimation<float> Anim;
            public Action Done;
            public bool RemoveAtEnd;
        }

        readonly Dictionary<VisualElement, Run> running = new Dictionary<VisualElement, Run>();

        // 새로 보이는 화면
        public void Enter(VisualElement el, NavMotion motion)
        {
            Cancel(el);
            if (motion == NavMotion.None) { Reset(el); return; }
            el.pickingMode = PickingMode.Position;
            Play(el, motion, entering: true, null);
        }

        // 가려지는 화면: 움직임이 끝나면 done(보통 display: none). 움직임이 없으면 바로 done
        public void Leave(VisualElement el, NavMotion motion, Action done)
        {
            Cancel(el);
            if (motion == NavMotion.None) { Reset(el); done?.Invoke(); return; }
            el.pickingMode = PickingMode.Ignore; // 나가는 동안 눌리지 않게
            Play(el, motion, entering: false, done);
        }

        // 나가는 중이면 끝난 뒤 화면 틀에서 떼고 true. 아니면 false(부른 쪽이 바로 뗌)
        public bool RemoveWhenDone(VisualElement el)
        {
            if (!running.TryGetValue(el, out var run)) return false;
            run.RemoveAtEnd = true;
            return true;
        }

        void Play(VisualElement el, NavMotion motion, bool entering, Action done)
        {
            int ms = motion == NavMotion.TabLeft || motion == NavMotion.TabRight ? TabMs : BaseMs;
            var run = new Run { Done = done };
            running[el] = run;
            Apply(el, motion, entering, 0f);
            run.Anim = el.experimental.animation
                .Start(0f, 1f, ms, (e, t) => Apply(e, motion, entering, EaseOut(t)))
                .Ease(Easing.Linear);
            run.Anim.OnCompleted(() => Finish(el, run));
        }

        void Finish(VisualElement el, Run run)
        {
            if (!running.TryGetValue(el, out var cur) || cur != run) return; // 그사이 다른 움직임이 시작됨
            running.Remove(el);
            Reset(el);
            run.Done?.Invoke();
            if (run.RemoveAtEnd) el.RemoveFromHierarchy();
        }

        // 진행 중인 움직임을 멈추고(끝 처리 없이) 원래 모습으로
        void Cancel(VisualElement el)
        {
            if (!running.TryGetValue(el, out var run)) return;
            running.Remove(el);
            run.Anim?.Stop(); // 자동 재활용(KeepAlive 안 함)
            Reset(el);
        }

        static void Reset(VisualElement el)
        {
            el.style.translate = StyleKeyword.Null;
            el.style.opacity = StyleKeyword.Null;
            el.pickingMode = PickingMode.Position;
        }

        // t: 0 → 1(이미 ease 적용)
        static void Apply(VisualElement el, NavMotion motion, bool entering, float t)
        {
            float x = 0f, opacity = 1f;
            bool percent = true;
            switch (motion)
            {
                case NavMotion.Push:
                    if (entering) x = Mathf.Lerp(100f, 0f, t);
                    else { x = Mathf.Lerp(0f, -30f, t); opacity = Mathf.Lerp(1f, 0.4f, t); }
                    break;
                case NavMotion.Back:
                    if (entering) { x = Mathf.Lerp(-30f, 0f, t); opacity = Mathf.Lerp(0.4f, 1f, t); }
                    else x = Mathf.Lerp(0f, 100f, t);
                    break;
                case NavMotion.Fade:
                    opacity = entering ? t : 1f - t;
                    break;
                case NavMotion.TabRight:
                case NavMotion.TabLeft:
                    percent = false;
                    opacity = entering ? t : 1f - t;
                    if (entering) x = Mathf.Lerp(motion == NavMotion.TabRight ? TabShift : -TabShift, 0f, t);
                    break;
            }
            el.style.translate = new Translate(percent ? Length.Percent(x) : new Length(x), 0);
            el.style.opacity = opacity;
        }

        // cubic-bezier(0.2, 0, 0, 1): x(s) = 0.6 s(1−s)² + 3 s²(1−s)·0 + s³ … 를 뉴턴법으로 풀어 y를 구함
        internal static float EaseOut(float t) => CubicBezier(0.2f, 0f, 0f, 1f, Mathf.Clamp01(t));

        internal static float CubicBezier(float x1, float y1, float x2, float y2, float x)
        {
            if (x <= 0f) return 0f;
            if (x >= 1f) return 1f;
            float s = x;
            for (int i = 0; i < 8; i++)
            {
                float cx = Bez(x1, x2, s) - x;
                if (Mathf.Abs(cx) < 1e-5f) break;
                float dx = BezD(x1, x2, s);
                if (Mathf.Abs(dx) < 1e-6f) break;
                s = Mathf.Clamp01(s - cx / dx);
            }
            return Bez(y1, y2, s);
        }

        static float Bez(float p1, float p2, float s)
        {
            float u = 1f - s;
            return 3f * u * u * s * p1 + 3f * u * s * s * p2 + s * s * s;
        }

        static float BezD(float p1, float p2, float s)
        {
            float u = 1f - s;
            return 3f * u * u * p1 + 6f * u * s * (p2 - p1) + 3f * s * s * (1f - p2);
        }
    }
}
