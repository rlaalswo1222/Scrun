using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // E-11 인증 후 반영(계획 0051, Figma 607:1602). 인증 완료 직후에만: GET rewards(이번 운동의 경험치·레벨·주간 목표) + GET progress(인증 마크·다음 임팩트).
    // 경험치 바가 앞 값에서 뒤 값으로 차오르고(노트 "또로로"), 레벨업이면 다 찬 뒤 3초에 E-13 덮개(정할 것 2 가: 러닝복 그림 대신 메인 캐릭터, 반짝임만).
    // "운동 결과 보기"·E-13 "계속하기" → 이 화면을 E-12로 바꿈(뒤로 가도 E-11로 안 돌아옴)
    public sealed class RunRewardsScreen : AppScreen
    {
        const float FillSeconds = 0.9f;
        const long LevelUpDelayMs = 3000;

        readonly string runId;
        RunRewards rewards;
        Progress progress;
        StateView state;
        VisualElement content, weekly, weeklyDots, outfitBefore, outfitArrow, fill, levelUp;
        Label weeklyText, levelLine, outfitBeforeName, outfitAfterName, xpGain, xpNext, marks, impactUnlocked, impactNext;
        IVisualElementScheduledItem anim, levelUpTimer;
        bool loading, levelUpShown;

        public RunRewardsScreen(string runId) => this.runId = runId ?? throw new ArgumentNullException(nameof(runId));

        protected override string Uxml => "UI/RunRewards";
        public override bool HidesTabBar => true;
        public override string RootClass => "app-root--celebrate"; // 축하 바탕(미리보기 E-11·E-13 bg celebrate)

        protected override void OnCreate()
        {
            content = Q<VisualElement>("content");
            weekly = Q<VisualElement>("weekly");
            weeklyText = Q<Label>("weekly-text");
            weeklyDots = Q<VisualElement>("weekly-dots");
            levelLine = Q<Label>("level-line");
            outfitBefore = Q<VisualElement>("outfit-before");
            outfitArrow = Q<Label>("outfit-arrow");
            outfitBeforeName = Q<Label>("outfit-before-name");
            outfitAfterName = Q<Label>("outfit-after-name");
            xpGain = Q<Label>("xp-gain");
            xpNext = Q<Label>("xp-next");
            fill = Q<VisualElement>("xp-fill");
            marks = Q<Label>("marks");
            impactUnlocked = Q<Label>("impact-unlocked");
            impactNext = Q<Label>("impact-next");
            levelUp = Q<VisualElement>("levelup");

            Q<Button>("next").clicked += ShowResult;
            state = new StateView(Q<VisualElement>("state"), SkeletonKind.Cards, "반영 결과를 불러오지 못했어요").Padded()
                .WithAlt("운동 결과 보기", ShowResult);
            Q<Button>("levelup-continue").clicked += ShowResult;

            content.SetVisible(false);
            levelUp.SetVisible(false);
        }

        // 다시 보이면(로그인 화면 등에서 돌아옴) 멈춘 바·레벨업 타이머를 처음부터
        protected internal override void OnShow()
        {
            if (rewards == null) Load();
            else if (!levelUpShown) Animate();
        }

        protected internal override void OnHide()
        {
            anim?.Pause();
            levelUpTimer?.Pause();
        }

        protected internal override bool OnBack()
        {
            ShowResult();
            return true;
        }

        // E-12로: 이 화면을 내리고 결과를 올림(같은 탭 쌓기 안에서 바꾸기)
        void ShowResult()
        {
            var nav = Nav;
            if (nav == null || nav.Current != this) return;
            nav.Pop();
            nav.Push(new RunResultScreen(runId));
        }

        async void Load()
        {
            if (loading) return;
            loading = true;
            RunRewards r = null;
            Progress p = null;
            // 레벨(progress)은 인증 마크 수·다음 임팩트·바 경계만이라 못 받아도 반영 결과는 보여 줌(조용히, 경계는 기본 표).
            // 둘 다 받을 때까지 스켈레톤(반영 결과만 먼저 오면 빈 화면이 되지 않게, 리뷰)
            bool ok = await Feedback.Load(state, rewards != null, async () =>
            {
                var progressTask = Api.Users.ProgressAsync();
                r = await Api.Runs.RewardsAsync(runId);
                try { p = await progressTask; }
                catch (ApiException e) when (e.Failure != ApiFailure.NotLoggedIn)
                {
                    UnityEngine.Debug.LogWarning("[반영] 레벨을 못 받음(기본 경계로): " + e);
                }
            }, Load);
            loading = false;
            if (Root.panel == null || !ok) return;
            Show(r, p);
            Animate();
        }

        internal void Show(RunRewards r, Progress p)
        {
            rewards = r;
            progress = p;
            Render();
        }

        void Render()
        {
            var r = rewards;
            content.SetVisible(true);
            state.Clear();

            string w = RunResultText.Weekly(r.WeeklyGoal);
            weekly.SetVisible(w != null);
            if (w != null)
            {
                weeklyText.text = w;
                weeklyDots.Clear();
                int target = r.WeeklyGoal.TargetCount ?? 0;
                for (int i = 0; i < target; i++)
                {
                    var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                    dot.AddToClassList("rr-dot");
                    bool done = i < r.WeeklyGoal.DoneCount;
                    dot.EnableInClassList("rr-dot--done", done);
                    if (done)
                    {
                        var check = new VisualElement { pickingMode = PickingMode.Ignore };
                        check.AddToClassList("rr-dot-check");
                        dot.Add(check);
                    }
                    weeklyDots.Add(dot);
                }
            }

            levelLine.text = RunResultText.LevelLine(r);
            int stageBefore = Math.Max(1, Math.Min(MyText.LevelStartKm.Length, r.LevelBefore));
            outfitBefore.SetVisible(r.LeveledUp);
            outfitArrow.SetVisible(r.LeveledUp);
            outfitBeforeName.text = MyText.Level(r.LevelBefore) + " " + MyText.Outfit(stageBefore);
            outfitAfterName.text = MyText.Level(r.LevelAfter) + " " + MyText.Outfit(r.OutfitStageAfter);
            xpGain.text = RunResultText.XpGain(r);
            xpNext.text = RunResultText.NextLevel(r);
            fill.style.width = Length.Percent(RunResultText.BarRange(r, progress?.LevelStartsM).from * 100f);

            marks.text = RunResultText.Marks(progress?.VerifiedCount ?? 1);
            var unlocked = r.ImpactsUnlocked != null && r.ImpactsUnlocked.Count > 0 ? r.ImpactsUnlocked[0] : null;
            impactUnlocked.SetVisible(unlocked != null);
            impactUnlocked.text = unlocked != null ? RunResultText.ImpactUnlocked(unlocked) : "";
            string next = RunResultText.NextImpact(progress);
            impactNext.SetVisible(next != null);
            impactNext.text = next ?? "";

            Q<Label>("levelup-before").text = MyText.Level(r.LevelBefore);
            Q<Label>("levelup-after").text = MyText.Level(r.LevelAfter);
            Q<Label>("levelup-outfit").text = MyText.Outfit(r.OutfitStageAfter);
            Q<Label>("levelup-xp").text = RunResultText.XpGain(r) + " · " + RunResultText.NextLevel(r);
            var li = Q<Label>("levelup-impact");
            li.SetVisible(unlocked != null);
            li.text = unlocked != null ? RunResultText.ImpactUnlocked(unlocked) : "";
        }

        // 바 차오름 → 레벨업이면 3초 뒤 E-13
        void Animate()
        {
            var (from, to) = RunResultText.BarRange(rewards, progress?.LevelStartsM);
            float t0 = Time.realtimeSinceStartup;
            anim?.Pause();
            anim = Root.schedule.Execute(() =>
            {
                float k = Mathf.Clamp01((Time.realtimeSinceStartup - t0) / FillSeconds);
                float eased = 1f - (1f - k) * (1f - k);
                fill.style.width = Length.Percent(Mathf.Lerp(from, to, eased) * 100f);
                if (k >= 1f) anim.Pause();
            }).Every(16);
            levelUpTimer?.Pause();
            if (rewards.LeveledUp && !levelUpShown)
                levelUpTimer = Root.schedule.Execute(ShowLevelUp).StartingIn((long)(FillSeconds * 1000) + LevelUpDelayMs);
        }

        internal void ShowLevelUp()
        {
            if (levelUpShown || Root.panel == null || Nav?.Current != this) return;
            levelUpShown = true;
            // 덮개 뒤 카드·내비를 숨김(바탕은 앱 바탕의 축하 그림이 그대로 보임, 미리보기 levelUp)
            content.SetVisible(false);
            Root.Q("rewards-nav")?.SetVisible(false);
            levelUp.SetVisible(true);
            levelUp.schedule.Execute(() => levelUp.AddToClassList("rr-levelup--in")).StartingIn(16); // 첫 배치 뒤에 페이드인
            UiMotion.Pop(levelUp.Q("levelup-title"));            // 미리보기 .pop
            UiMotion.Rise(levelUp.Q("levelup-levels"));          // .rise
            UiMotion.StatusIn(levelUp.Q("levelup-stage"), false); // 캐릭터 튀어 들어온 뒤 둥실
        }
    }
}
