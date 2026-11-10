using System;
using System.Linq;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // A-11 속도 보정 안내(세로). "보정 시작" → 가로 보정 화면(A-12–A-15).
    // 부르는 곳(계획 0023): 코스 입장이 409 CALIBRATION_REQUIRED일 때, 내 정보의 재보정(H-09).
    //   Nav.Push(new CalibrationIntroScreen(onDone: () => EnterAgain()));
    // 끝나면(완료·그만두기 모두) 보정 화면과 이 화면을 닫고 onDone 또는 onCancel을 부른다.
    public sealed class CalibrationIntroScreen : AppScreen
    {
        readonly Action onDone, onCancel;

        public CalibrationIntroScreen(Action onDone = null, Action onCancel = null)
        {
            this.onDone = onDone;
            this.onCancel = onCancel;
        }

        protected override string Uxml => "UI/CalibrationIntro";
        public override bool HidesTabBar => true;

        protected override void OnCreate()
        {
            var ladder = CalibrationLadder.SpeedsKmh;
            Q<Label>("title").text = CalibrationCopy.IntroTitle;
            Q<Label>("lead").text = CalibrationCopy.IntroLead(ladder.Length, ladder.First(), ladder.Last(), CalibrationLadder.StageSec);
            Q<Label>("note").text = CalibrationCopy.IntroNote;
            var steps = Q<VisualElement>("steps");
            for (int i = 0; i < CalibrationCopy.IntroSteps.Length; i++)
            {
                var row = new VisualElement();
                row.AddToClassList("calib-intro-step");
                var num = new Label((i + 1).ToString());
                num.AddToClassList("calib-intro-num");
                var text = new Label(CalibrationCopy.IntroSteps[i]);
                text.AddToClassList("calib-intro-text");
                row.Add(num);
                row.Add(text);
                steps.Add(row);
            }

            Q<Button>("back").clicked += () =>
            {
                Nav.Pop();
                onCancel?.Invoke();
            };
            var start = Q<Button>("start");
            start.text = CalibrationCopy.IntroStart;
            start.clicked += () => Nav.Push(new CalibrationScreen(Finish));
        }

        void Finish(bool done)
        {
            Nav.Pop(); // 보정 화면
            Nav.Pop(); // 이 화면
            (done ? onDone : onCancel)?.Invoke();
        }
    }
}
