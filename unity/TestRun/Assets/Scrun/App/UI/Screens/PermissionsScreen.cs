using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // A-07 권한 안내(계획 0025). 가입 뒤·앱 시작 때 흐름의 첫 화면(Onboarding.Begin).
    // "권한 설정하기" → 시스템 팝업 4개를 차례로(A-08, 이미 정한 것은 팝업 없이 지나감) → 모션을 못 받았으면 I-05, 아니면 이어폰 화면.
    // "나중에 하기" → 이어폰 화면(모션 없이 맵에 들어가려 하면 그때 다시 막는다).
    public sealed class PermissionsScreen : AppScreen
    {
        readonly Dictionary<PermissionKind, (Label chip, bool required)> chips = new Dictionary<PermissionKind, (Label, bool)>();
        Button allow, later;
        bool asking, subscribed;

        protected override string Uxml => "UI/Permissions";

        protected override void OnCreate()
        {
            // 흐름의 맨 아래(StartFlow)라 돌아갈 곳이 없음 → 자리만 두고 숨김
            Q<Button>("back").style.visibility = Visibility.Hidden;
            var rows = Q<VisualElement>("rows");
            foreach (var kind in Onboarding.AskOrder) rows.Add(Row(kind));
            allow = Q<Button>("allow");
            later = Q<Button>("later");
            allow.clicked += Ask;
            later.clicked += () => { if (!asking && Nav.Current == this) Nav.Push(new EarphoneScreen()); };
        }

        protected internal override void OnShow()
        {
            Subscribe(true);
            foreach (var kind in Onboarding.AskOrder) Refresh(kind, Native.Permissions.GetStatus(kind));
        }

        protected internal override void OnHide() => Subscribe(false);
        protected internal override void OnDestroy() => Subscribe(false);

        async void Ask()
        {
            if (asking || Nav.Current != this) return;
            asking = true;
            allow.SetEnabled(false);
            later.SetEnabled(false);
            try
            {
                foreach (var kind in Onboarding.AskOrder)
                {
                    await Onboarding.Ask(kind); // 답이 안 오면 시간 제한 뒤 넘어감(버튼이 잠긴 채 남지 않게)
                    if (Root.panel == null) return; // 기다리는 사이 화면을 떠남
                }
            }
            finally
            {
                asking = false;
                allow.SetEnabled(true);
                later.SetEnabled(true);
            }
            if (Nav.Current != this) return;
            if (Onboarding.Next(Onboarding.MotionStatus()) == AfterAsking.MotionDenied)
                Nav.Push(new MotionDeniedScreen(ReplaceWithEarphone, ReplaceWithEarphone, onBack: () => Nav.Pop()));
            else
                Nav.Push(new EarphoneScreen());
        }

        // I-05에서 넘어갈 때: I-05를 빼고 이어폰 화면으로(뒤로 가기가 I-05로 돌아오지 않게)
        void ReplaceWithEarphone()
        {
            if (!(Nav.Current is MotionDeniedScreen)) return;
            Nav.Pop();
            Nav.Push(new EarphoneScreen());
        }

        VisualElement Row(PermissionKind kind)
        {
            var info = OnboardingText.Permission(kind);
            var row = new VisualElement();
            row.AddToClassList("info-row");
            var box = new VisualElement();
            box.AddToClassList("info-icon-box");
            var icon = new VisualElement();
            icon.AddToClassList("info-icon");
            icon.AddToClassList(info.IconClass);
            box.Add(icon);
            row.Add(box);
            var text = new VisualElement();
            text.AddToClassList("info-text");
            text.Add(UiExtensions.Text(info.Title, "info-name"));
            text.Add(UiExtensions.Text(info.Body, "info-desc"));
            row.Add(text);
            var chip = UiExtensions.Text("", "tag");
            chips[kind] = (chip, info.Required);
            row.Add(chip);
            return row;
        }

        // 칩: 허용 "허용됨"(초록), 거부 "꺼짐"(회색), 아직이면 필수(파랑)·선택(회색)
        void Refresh(PermissionKind kind, PermissionStatus status)
        {
            if (!chips.TryGetValue(kind, out var c)) return;
            c.chip.text = OnboardingText.Chip(c.required, status);
            c.chip.EnableInClassList("tag--success", status == PermissionStatus.Granted);
            c.chip.EnableInClassList("tag--gray", status != PermissionStatus.Granted && (!c.required || status != PermissionStatus.NotDetermined));
        }

        void Subscribe(bool on)
        {
            if (on == subscribed) return;
            if (on) Native.Permissions.Changed += Refresh;
            else Native.Permissions.Changed -= Refresh;
            subscribed = on;
        }
    }
}
