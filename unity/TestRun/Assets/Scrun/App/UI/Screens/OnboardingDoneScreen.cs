using UnityEngine.UIElements;

namespace Scrun.App
{
    // A-17 온보딩 완료(Figma 599:434, 계획 0031). 가입 직후 주간 목표(A-16) 다음. "홈으로" → 홈(이번 주 목표가 바로 보임).
    // 배경은 스플래시와 같은 그라데이션. 보정은 온보딩이 아니라 첫 맵 입장 때(계획 0023) — 여기선 안내 한 줄만.
    // "[모션] 아바타 환호 + 컨페티"는 cheer 에셋이 생기면.
    public sealed class OnboardingDoneScreen : AppScreen
    {
        protected override string Uxml => "UI/OnboardingDone";
        public override bool HidesTabBar => true;
        public override string RootClass => "app-root--splash";

        protected override void OnCreate() =>
            Q<Button>("home").clicked += () => { if (Nav.Current == this) Onboarding.FinishGoal(Nav); };
    }
}
