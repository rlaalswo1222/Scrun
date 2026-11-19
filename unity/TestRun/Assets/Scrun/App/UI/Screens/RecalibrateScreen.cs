using System.Globalization;
using System.Linq;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // H-09 속도 재보정(계획 0049, Figma 610:2595). 마지막 보정 날짜·보정한 속도(GET /users/me/calibration) →
    // "다시 보정하기"는 보정 흐름 그대로(CalibrationIntroScreen, 계획 0023). 끝나면 보정 화면들이 스스로 닫히고 여기로 돌아와 다시 받음
    public sealed class RecalibrateScreen : AppScreen
    {
        Label last, speeds;
        bool loading, loaded;
        StateView state;

        protected override string Uxml => "UI/Recalibrate";
        public override bool HidesTabBar => true;

        protected override void OnCreate()
        {
            Q<Button>("back").clicked += () => { if (Nav.Current == this) Nav.Pop(); };
            var content = Q<VisualElement>("content");
            var head = new VisualElement();
            head.AddToClassList("head");
            head.Add(UiExtensions.Text("속도를 다시 맞춰요", "onb-title"));                                       // Figma H-09
            head.Add(UiExtensions.Text(MyText.RecalibrateBody, "onb-lead"));
            content.Add(head);

            var stateHost = new VisualElement { name = "state" };
            content.Add(stateHost);
            var group = MyParts.Group(content);
            var lastRow = MyParts.Row("마지막 보정", null, "", null, chevron: false);
            last = lastRow.Q<Label>(className: "my-row-value");
            last.name = "last";
            group.Add(lastRow);
            var speedRow = MyParts.Row("보정 속도", "", null, null, chevron: false); // 단계가 많아(최대 7) 오른쪽 대신 아래 줄에
            speeds = speedRow.Q<Label>(className: "my-row-sub");
            speeds.name = "speeds";
            group.Add(speedRow);
            MyParts.MarkLast(group);
            // 공통 상태(계획 0056): 처음 받는 동안 값 칸 대신 스켈레톤, 실패하면 그 자리에. "다시 보정하기"는 그대로 씀
            state = new StateView(stateHost, SkeletonKind.Rows, "보정 기록을 불러오지 못했어요", group);

            var start = new Button(() => { if (Nav.Current == this) Nav.Push(new CalibrationIntroScreen()); }) { text = "다시 보정하기", name = "start" };
            start.AddToClassList("btn");
            start.AddToClassList("btn-primary");
            start.style.marginTop = 24;
            content.Add(start);
            Show(null);
        }

        protected internal override void OnShow() => Load();

        async void Load()
        {
            if (loading) return;
            loading = true;
            CalibrationRecord got = null;
            // 처음만 스켈레톤(실패하면 그 자리에), 보정을 마치고 돌아오면 지금 값을 둔 채 조용히
            bool ok = await Feedback.Load(state, loaded, async () => got = await Api.Users.GetCalibrationAsync(), Load);
            loading = false;
            if (!ok || Root.panel == null) return;
            loaded = true;
            Show(got);
        }

        void Show(CalibrationRecord c)
        {
            last.text = c?.CreatedAt is System.DateTime at ? MyText.DateWithDay(at) : "아직 보정하지 않았어요";
            speeds.text = c?.Points == null || c.Points.Count == 0 ? "-"
                : string.Join(" / ", c.Points.Select(p => p.SpeedKmh.ToString("0.0", CultureInfo.InvariantCulture))) + " km/h";
        }
    }
}
