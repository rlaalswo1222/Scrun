using UnityEngine.UIElements;

namespace Scrun.App
{
    // A-09 이어폰 미연결·A-10 연결됨(계획 0025). 끼고 빼면(Headphones.Changed) 저절로 바뀐다.
    // 움직임 센서가 있는 이어폰이 연결됐는데 헤드폰 모션 권한을 아직 안 물어봤으면 "다음"을 누를 때 묻는다(이어폰 없이 A-07에서 팝업이 안 떴을 때 대비).
    // 그리기(Render)는 묻지 않는다 — 끼고 빼거나 권한이 바뀔 때마다 불려서, 누르지도 않았는데 팝업이 뜨지 않게.
    // 미연결: "연결 확인하기"(다시 읽기)·"이어폰 없이 계속"·"건너뛰기" / 연결됨: "다음" → 홈(Onboarding.Finish)
    public sealed class EarphoneScreen : AppScreen
    {
        Label title, lead, device, detail, chip;
        VisualElement iconBox, icon;
        Button skip, check, without, next;
        bool subscribed, asking;

        protected override string Uxml => "UI/Earphone";

        protected override void OnCreate()
        {
            Q<Button>("back").clicked += () => { if (Nav.Current == this) Nav.Pop(); };
            title = Q<Label>("title");
            lead = Q<Label>("lead");
            device = Q<Label>("device");
            detail = Q<Label>("detail");
            chip = Q<Label>("chip");
            iconBox = Q<VisualElement>("icon-box");
            icon = Q<VisualElement>("icon");
            skip = Q<Button>("skip");
            check = Q<Button>("check");
            without = Q<Button>("without");
            next = Q<Button>("next");
            check.clicked += Render;
            skip.clicked += Finish;
            without.clicked += Finish;
            next.clicked += Next;
        }

        protected internal override void OnShow()
        {
            Subscribe(true);
            Render();
        }

        protected internal override void OnHide() => Subscribe(false);
        protected internal override void OnDestroy() => Subscribe(false);

        void Finish()
        {
            if (!asking && Nav.Current == this) Onboarding.Finish(Nav);
        }

        // A-10 "다음": 헤드폰 모션을 아직 안 물어봤으면 이때 묻고 끝냄
        async void Next()
        {
            if (asking || Nav.Current != this) return;
            if (Native.Headphones.Status == HeadphoneStatus.Connected &&
                Native.Permissions.GetStatus(PermissionKind.HeadphoneMotion) == PermissionStatus.NotDetermined)
            {
                asking = true;
                next.SetEnabled(false);
                try { await Onboarding.Ask(PermissionKind.HeadphoneMotion); }
                finally
                {
                    asking = false;
                    next.SetEnabled(true);
                }
                if (Root.panel == null) return;
            }
            Finish();
        }

        void OnHeadphonesChanged(HeadphoneStatus _) => Render();

        void OnPermissionChanged(PermissionKind kind, PermissionStatus _)
        {
            if (kind == PermissionKind.HeadphoneMotion) Render();
        }

        void Render()
        {
            var status = Native.Headphones.Status;
            var motion = Native.Permissions.GetStatus(PermissionKind.HeadphoneMotion);
            var t = OnboardingText.Earphone(status, Native.Headphones.DeviceName, motion);
            title.text = t.title;
            lead.text = t.lead;
            device.text = t.name;
            detail.text = t.detail;
            chip.text = t.chip;

            bool connected = status != HeadphoneStatus.NotConnected;
            iconBox.EnableInClassList("info-icon-box--off", !connected);
            icon.EnableInClassList("icon-earphone-off", !connected);
            icon.EnableInClassList("icon-earphone-on", connected);
            chip.EnableInClassList("tag--success", connected);
            chip.EnableInClassList("tag--gray", !connected);
            skip.SetVisible(!connected);
            check.SetVisible(!connected);
            without.SetVisible(!connected);
            next.SetVisible(connected);
        }

        void Subscribe(bool on)
        {
            if (on == subscribed) return;
            if (on)
            {
                Native.Headphones.Changed += OnHeadphonesChanged;
                Native.Permissions.Changed += OnPermissionChanged;
            }
            else
            {
                Native.Headphones.Changed -= OnHeadphonesChanged;
                Native.Permissions.Changed -= OnPermissionChanged;
            }
            subscribed = on;
        }
    }
}
