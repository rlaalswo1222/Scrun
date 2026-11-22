using System;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

namespace Scrun.App
{
    // 화면 안 작은 움직임(계획 0068, 상준 미리보기 ui.css·fx.css의 keyframes를 코드로). USS transition으로 못 하는 것만:
    //  - Pop: 탭 아이콘이 고를 때 통 튐(.7 → 1.16 → 1, 400ms 스프링, fx.css tbPop)
    //  - Flick: 탭 LED가 두 번 깜빡이며 켜짐(400ms, ledFlick)
    //  - StatusIn: 큰 상태 그림이 들어올 때 튀고(.6 → 1, 520ms), 경고·오류면 한 번 흔들리고, 그 뒤 4px 둥실(ui.css st3d)
    //  - Rise: 아래 12에서 올라오며 나타남(500ms, 늦게 시작, ui.css rise)
    //  - CountUp: 숫자가 0에서 올라감(1초)
    // 모두 그 요소가 패널에서 떨어지면 멈춘다(화면이 없어지면 따라 없어짐)
    public static class UiMotion
    {
        // cubic-bezier(.34, 1.36, .64, 1): 살짝 넘쳤다 돌아오는 스프링(토큰 ease/spring)
        static float Spring(float t) => ScreenTransitions.CubicBezier(0.34f, 1.36f, 0.64f, 1f, t);
        static float EaseOut(float t) => ScreenTransitions.EaseOut(t);

        public static void Pop(VisualElement el)
        {
            if (el == null) return;
            el.experimental.animation.Start(0f, 1f, 400, (e, t) =>
            {
                // 0 → .55: .7 → 1.16, .55 → 1: 1.16 → 1
                float s = t < 0.55f ? Mathf.Lerp(0.7f, 1.16f, Spring(t / 0.55f)) : Mathf.Lerp(1.16f, 1f, Spring((t - 0.55f) / 0.45f));
                e.style.scale = new Scale(new Vector3(s, s, 1f));
            }).Ease(Easing.Linear).OnCompleted(() => el.style.scale = StyleKeyword.Null);
        }

        public static void Flick(VisualElement el)
        {
            if (el == null) return;
            el.experimental.animation.Start(0f, 1f, 400, (e, t) =>
            {
                // steps(1): 0 .15 / 25% 1 / 50% .25 / 75%– 1
                e.style.opacity = t < 0.25f ? 0.15f : t < 0.5f ? 1f : t < 0.75f ? 0.25f : 1f;
            }).Ease(Easing.Linear).OnCompleted(() => el.style.opacity = StyleKeyword.Null);
        }

        public static void StatusIn(VisualElement el, bool shake)
        {
            if (el == null) return;
            el.style.opacity = 0f;
            el.experimental.animation.Start(0f, 1f, 520, (e, t) =>
            {
                float s = Mathf.LerpUnclamped(0.6f, 1f, Spring(t));
                e.style.scale = new Scale(new Vector3(s, s, 1f));
                e.style.opacity = Mathf.Clamp01(t * 2f);
            }).Ease(Easing.Linear).OnCompleted(() =>
            {
                el.style.scale = StyleKeyword.Null;
                el.style.opacity = StyleKeyword.Null;
                if (shake) Shake(el, () => Float(el));
                else Float(el);
            });
        }

        // 한 번 흔들림(480ms: 0 → −7° → 5° → −2° → 0)
        static void Shake(VisualElement el, Action then)
        {
            el.experimental.animation.Start(0f, 1f, 480, (e, t) =>
            {
                float a = t < 0.25f ? Mathf.Lerp(0f, -7f, t / 0.25f)
                        : t < 0.55f ? Mathf.Lerp(-7f, 5f, (t - 0.25f) / 0.3f)
                        : t < 0.8f ? Mathf.Lerp(5f, -2f, (t - 0.55f) / 0.25f)
                        : Mathf.Lerp(-2f, 0f, (t - 0.8f) / 0.2f);
                e.style.rotate = new Rotate(new Angle(a, AngleUnit.Degree));
            }).Ease(Easing.InOutSine).OnCompleted(() =>
            {
                el.style.rotate = StyleKeyword.Null;
                then?.Invoke();
            });
        }

        // 위아래 4px, 4.5초 주기로 계속(요소가 패널에서 떨어지면 멈춤)
        public static void Float(VisualElement el, float amplitude = 4f, float period = 4.5f)
        {
            if (el == null) return;
            float start = Time.realtimeSinceStartup;
            el.schedule.Execute(() =>
            {
                float p = (Time.realtimeSinceStartup - start) / period;
                float y = -amplitude * 0.5f * (1f - Mathf.Cos(p * Mathf.PI * 2f));
                el.style.translate = new Translate(0, y);
            }).Every(33).Until(() => el.panel == null);
        }

        // 아래 12에서 올라오며 나타남. delayMs 뒤 시작
        public static void Rise(VisualElement el, long delayMs = 300)
        {
            if (el == null) return;
            el.style.opacity = 0f;
            el.style.translate = new Translate(0, 12);
            el.schedule.Execute(() =>
                el.experimental.animation.Start(0f, 1f, 500, (e, t) =>
                {
                    e.style.opacity = t;
                    e.style.translate = new Translate(0, Mathf.Lerp(12f, 0f, t));
                }).Ease(EaseOut).OnCompleted(() =>
                {
                    el.style.opacity = StyleKeyword.Null;
                    el.style.translate = StyleKeyword.Null;
                })).ExecuteLater(delayMs);
        }

        // 숫자가 from에서 to로 올라감(format이 글자로). 1초, ease-out
        public static void CountUp(Label label, float from, float to, Func<float, string> format, int ms = 1000)
        {
            if (label == null || format == null) return;
            label.text = format(from);
            label.experimental.animation.Start(0f, 1f, ms, (e, t) => label.text = format(Mathf.Lerp(from, to, t)))
                .Ease(EaseOut).OnCompleted(() => label.text = format(to));
        }
    }
}
