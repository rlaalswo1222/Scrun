using System;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // I-05 모션 및 피트니스 권한 거부(계획 0025). 권한 안내 뒤, 그리고 맵 입장 전(코스 상세)에 쓴다.
    // "설정 열기" → 설정 앱. 거기서 켜고 돌아오면(Permissions.Changed) 저절로 onGranted. "나중에" → onLater, 뒤로 → onBack(없으면 onLater)
    public sealed class MotionDeniedScreen : AppScreen
    {
        readonly Action onGranted, onLater, onBack;
        bool subscribed, done;

        public MotionDeniedScreen(Action onGranted, Action onLater, Action onBack = null)
        {
            this.onGranted = onGranted;
            this.onLater = onLater;
            this.onBack = onBack ?? onLater; // 코스 상세에서는 뒤로도 "나중에"와 같게(상세를 다시 불러오지 않음)
        }

        protected override string Uxml => "UI/MotionDenied";
        public override bool HidesTabBar => true;

        protected override void OnCreate()
        {
            Q<Button>("back").clicked += () => Leave(onBack);
            Q<Button>("later").clicked += Later;
            Q<Button>("settings").clicked += () => Native.Permissions.OpenSettings();
        }

        protected internal override void OnShow()
        {
            Subscribe(true);
            // 설정 앱에서 켜고 돌아오는 사이 이벤트를 놓쳤을 수도 있으니 보일 때도 확인
            if (Onboarding.MotionStatus() == PermissionStatus.Granted) Leave(onGranted);
        }

        protected internal override void OnHide() => Subscribe(false);
        protected internal override void OnDestroy() => Subscribe(false);

        void OnChanged(PermissionKind kind, PermissionStatus status)
        {
            if (kind == PermissionKind.Motion && status == PermissionStatus.Granted) Leave(onGranted);
        }

        void Later() => Leave(onLater);

        // 한 번만(두 번 눌러도 이 화면 아래까지 빠지지 않게), 이 화면이 맨 위일 때만
        void Leave(Action next)
        {
            if (done || Nav.Current != this) return;
            done = true;
            next?.Invoke();
        }

        void Subscribe(bool on)
        {
            if (on == subscribed) return;
            if (on) Native.Permissions.Changed += OnChanged;
            else Native.Permissions.Changed -= OnChanged;
            subscribed = on;
        }
    }
}
