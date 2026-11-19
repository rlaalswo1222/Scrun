using UnityEngine.UIElements;

namespace Scrun.App
{
    // A-17 온보딩 완료(계획 0031, 모양은 우주 시안 886:229 계획 0061). 가입 직후 주간 목표(A-16) 다음. "홈으로" → 홈(이번 주 목표가 바로 보임).
    // 배경은 우주 바탕. 보정은 온보딩이 아니라 첫 맵 입장 때(계획 0023) — 여기선 안내 한 줄만.
    // "[모션] 아바타 환호 + 컨페티"는 cheer 에셋이 생기면.
    public sealed class OnboardingDoneScreen : AppScreen
    {
        protected override string Uxml => "UI/OnboardingDone";
        public override bool HidesTabBar => true;
        public override string RootClass => SpaceArt.RootClass;

        protected override void OnCreate()
        {
            SpaceArt.Fit(Q<VisualElement>("fit"));
            Q<Button>("home").clicked += () => { if (Nav.Current == this) Onboarding.FinishGoal(Nav); };
        }
    }
}
