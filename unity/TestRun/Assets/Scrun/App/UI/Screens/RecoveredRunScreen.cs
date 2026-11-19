using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // E-01 종료 요약·인증 전(계획 0052). 앱이 꺼졌다 켜져 I-04에서 "마무리하기"를 누른 러닝(서버에 이미 끝냄).
    // 서버가 인증 대기(PENDING)로 끝냈으면 D-22처럼 "계기판 촬영하기"·"촬영 없이 마무리", 아니면(멈춘 뒤 10분이 지나 서버가 바로 미인증,
    // 정할 것 2 가) 왜 못 찍는지 알리고 "결과 보기". 판정은 서버 한 곳(앱이 따로 재면 서버와 어긋남, 리뷰).
    // 인증·결과 흐름은 코스 상세(D-22 다음)와 같음: 인증 완료 → E-11, 아니면 E-12·E-14. 글자는 Figma E-01(607:1054, 계획 0057)
    public sealed class RecoveredRunScreen : AppScreen
    {
        readonly RecoveredRun run;
        readonly string runId;
        readonly bool pending;   // 서버가 인증 대기로 끝냄 = 아직 찍을 수 있음
        readonly bool canVerify;

        public RecoveredRunScreen(RecoveredRun run, RunSummary saved)
        {
            this.run = run ?? throw new ArgumentNullException(nameof(run));
            runId = saved?.RunId ?? throw new ArgumentNullException(nameof(saved));
            pending = saved.Verification == VerifyStatus.Pending;
            canVerify = pending;
        }

        protected override string Uxml => "UI/RunRecovered";
        public override bool HidesTabBar => true;

        // 테스트
        internal bool CanVerify => canVerify;

        protected override void OnCreate()
        {
            var body = run.FinishBody.Sensor;
            Q<Label>("nav-title").text = RecoveryText.NavTitle;
            Q<Label>("title").text = RecoveryText.EndTitle;
            Q<Label>("lead").text = canVerify ? RecoveryText.EndLead : RecoveryText.LateLead;
            Q<Label>("card-title").text = RecoveryText.CardTitle;
            Q<Label>("km").text = RunResultText.BigKm(body.DistanceM);
            Q<Label>("time").text = FriendText.Duration(body.DurationS);
            Q<Label>("pace").text = RunResultText.Pace(RunResultText.AvgPace(body.DistanceM, body.DurationS));
            Q<Label>("goal").text = run.Start != null ? RunResultText.Goal(run.Start.Goal) : "-"; // 서버에만 있던 러닝은 목표를 모름(없음과 다름)
            var warning = Q<Label>("warning");
            warning.text = RecoveryText.SkipWarning;
            warning.SetVisible(canVerify);
            var verify = Q<Button>("verify");
            verify.text = RecoveryText.Verify;
            verify.SetVisible(canVerify);
            verify.clicked += Verify;
            var skip = Q<Button>("skip");
            skip.text = canVerify ? RecoveryText.Skip : RecoveryText.SeeResult;
            skip.EnableInClassList("btn-primary", !canVerify);
            skip.EnableInClassList("btn-ghost", canVerify);
            skip.clicked += Skip;
        }

        // 버튼으로만(D-22와 같음): 뒤로 가면 인증 대기로 남아 마감 뒤 미인증이 되는데, 그걸 고른 적이 없게 됨
        protected internal override bool OnBack() => true;

        void Verify()
        {
            if (!canVerify || Nav?.Current != this) return;
            string id = runId;
            Nav.Push(new VerifyScreen(id, run.FinishBody.Sensor, verified => AfterVerify(id, verified)));
        }

        // 코스 상세 AfterVerify와 같음: 인증 화면을 결과로 바꿈(이 화면은 결과 아래에 남고, 결과를 닫으면 홈)
        void AfterVerify(string id, bool verified)
        {
            if (!(Nav?.Current is VerifyScreen)) return;
            Nav.Pop();
            Nav.Push(verified ? (AppScreen)new RunRewardsScreen(id) : new RunResultScreen(id));
        }

        // "촬영 없이 마무리"·"결과 보기": 아직 인증 대기면 미인증으로 알리고(결과 화면이 그걸 기다린 뒤 불러옴) E-14
        void Skip()
        {
            if (Nav?.Current != this) return;
            Task skip = pending ? SkipAsync(runId) : null;
            Nav.Push(new RunResultScreen(runId, skip));
        }

        static async Task SkipAsync(string runId)
        {
            try { await Api.Runs.SkipVerificationAsync(runId); }
            catch (ApiException e) { Debug.LogWarning("[복구] 미인증 마무리를 서버에 못 알림(마감 뒤 서버가 닫음): " + e); }
        }
    }
}
