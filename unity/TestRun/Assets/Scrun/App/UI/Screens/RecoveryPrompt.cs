using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // I-04 "끝나지 않은 러닝이 있어요"(계획 0052, BL-37). 앱을 켠 뒤 홈에 처음 왔을 때 한 번 묻는다(계정마다).
    // 폰 진행 기록(ActiveRunStore)과 서버 GET /runs/active 중 RunRecovery.Pick이 고른 러닝을 홈 위 시트로 보여 주고
    // "마무리하기" → 남은 시계열 + 종료 → E-01(RecoveredRunScreen) / "버리기" → 확인 → discard.
    // 홈 화면(지환 BL-31)과 떨어져 있게 따로 둠: 홈은 OnShow에서 CheckOnShow(this)만 부른다
    public static class RecoveryPrompt
    {
        static string checkedFor;      // 이번 실행에서 물어본 계정(못 물었으면 null로 되돌려 다음에 다시)
        static RecoveredRun waiting;   // 확인이 끝났을 때 홈이 안 보였으면 다음에 홈에 올 때 띄움
        static string waitingFor;      // waiting을 찾은 계정(다른 계정으로 바뀌면 버림, 리뷰)
        static bool working;
        static FriendParts.Sheet sheet;
        static VisualElement host; // 시트를 담는 틀(스타일)

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            checkedFor = null;
            waiting = null;
            waitingFor = null;
            working = false;
            sheet = null;
            host = null;
        }

        // 홈 OnShow. 물어볼 게 없으면 지금처럼 남긴 기록 보내기만
        public static void CheckOnShow(AppScreen home, Action flushPending)
        {
            string uid = RunRecordUploader.TokenSubject(Api.Session.AccessToken);
            if (waiting != null)
            {
                var r = waiting;
                bool mine = uid != null && uid == waitingFor;
                waiting = null;
                waitingFor = null;
                if (mine && StillOpen(r))
                {
                    Show(home, r, uid);
                    return;
                }
            }
            if (uid == null || uid == checkedFor)
            {
                flushPending?.Invoke();
                return;
            }
            checkedFor = uid;
            Check(home, uid, flushPending);
        }

        // 기다리는 사이 그 러닝이 정상으로 끝났으면(폰 기록이 지워짐·새 달리기가 덮어씀) 묻지 않음
        static bool StillOpen(RecoveredRun r) =>
            !r.FromPhone || ActiveRunStore.Default.Load()?.Start?.ClientRunId == r.ClientRunId;

        static async void Check(AppScreen home, string uid, Action flushPending)
        {
            // 폰 기록은 서버를 기다리기 전에 읽음: 기다리는 사이 새로 달리기 시작하면 그 진행 기록을 복구 대상으로 착각함(리뷰)
            var local = ActiveRunStore.Default.Load();
            ActiveRun server = null;
            bool asked = false;
            try
            {
                server = await Api.Runs.GetActiveAsync();
                asked = true;
            }
            catch (ApiException e)
            {
                if (e.Failure == ApiFailure.NotLoggedIn)
                {
                    checkedFor = null;
                    Feedback.Handle(e);
                    return;
                }
                Debug.LogWarning("[복구] 끝나지 않은 러닝을 못 물음(폰 기록만 봄): " + e);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            var c = RunRecovery.Pick(local, asked ? server : null, uid, DateTime.UtcNow);
            if (c.ClearPhone && local != null) ActiveRunStore.Default.Clear(local.Start.ClientRunId); // 그새 덮어쓴 새 기록은 그대로
            if (!asked && c.Run == null) checkedFor = null; // 연결이 안 돼 서버에 못 물음 → 다음에 홈에 올 때 다시
            flushPending?.Invoke(); // 마무리할 러닝과 남긴 기록은 clientRunId가 달라 같이 보내도 됨
            if (c.Run == null) return;
            if (!StillOpen(c.Run)) return;
            if (home.Root.panel == null || home.Nav?.Current != home || uid != RunRecordUploader.TokenSubject(Api.Session.AccessToken))
            {
                waiting = c.Run; // 기다리는 사이 다른 화면으로 감 → 홈에 다시 올 때(같은 계정·그 러닝이 아직 안 끝났을 때만)
                waitingFor = uid;
                return;
            }
            Show(home, c.Run, uid);
        }

        static void Show(AppScreen home, RecoveredRun run, string uid)
        {
            if (sheet?.Open == true) return;
            OpenSheet(home);
            sheet.Body.Add(UiExtensions.Text(RecoveryText.Title, "fr-sheet-title"));
            sheet.Body.Add(UiExtensions.Text(RecoveryText.Info(run), "fr-sub"));
            var stats = UiExtensions.Text(RecoveryText.Stats(run.DistanceM, run.DurationS), "fr-sheet-title");
            stats.name = "recover-stats";
            sheet.Body.Add(stats);
            sheet.Info(RecoveryText.Body);
            sheet.Button(RecoveryText.Finish, "btn-primary", () => Finish(home, run, uid), "recover-finish");
            sheet.Button(RecoveryText.Discard, "btn-ghost", () => ConfirmDiscard(home, run, uid), "recover-discard");
        }

        // 홈 UXML엔 친구 스타일이 없음(시트 모양은 Friends.uss). 홈을 바꾸지 않게 스타일을 담은 빈 틀(RecoverySheet.uxml)을
        // 홈 위에 꽉 차게 붙이고 그 안에 시트를 만든다(코드로 USS를 불러 붙이면 빈 스타일이 와서 안 됨, 11/19 에디터 확인)
        static void OpenSheet(AppScreen home)
        {
            sheet?.Close();
            host?.RemoveFromHierarchy();
            host = Resources.Load<VisualTreeAsset>("UI/RecoverySheet").Instantiate();
            host.pickingMode = PickingMode.Ignore;
            host.style.position = Position.Absolute;
            host.style.left = host.style.top = host.style.right = host.style.bottom = 0;
            home.Root.Add(host);
            sheet = new FriendParts.Sheet(host);
        }

        static void ConfirmDiscard(AppScreen home, RecoveredRun run, string uid)
        {
            if (working) return;
            OpenSheet(home);
            sheet.Body.Add(UiExtensions.Text(RecoveryText.DiscardTitle, "fr-sheet-title"));
            sheet.Info(RecoveryText.DiscardBody);
            sheet.Button(RecoveryText.Discard, "btn-danger", () => Discard(home, run), "recover-discard-confirm");
            sheet.Button(RecoveryText.Cancel, "btn-ghost", () => Show(home, run, uid), "recover-discard-cancel");
        }

        // "마무리하기": 저장 중엔 시트를 잠금(바깥을 눌러 닫히지 않게). 실패 안내의 "다시 시도"는 같은 시트에서
        static async void Finish(AppScreen home, RecoveredRun run, string uid)
        {
            if (working || sheet == null) return;
            working = true;
            var mine = sheet;
            mine.Locked = true;
            RunRecovery.FinishResult r = default;
            bool ok;
            try
            {
                ok = await Feedback.Run(async () => r = await RunRecovery.FinishAsync(run, uid), () => Finish(home, run, uid));
            }
            finally
            {
                working = false;
                mine.Locked = false;
            }
            if (!ok) return;
            mine.Close();
            var nav = home.Nav;
            if (nav == null || home.Root.panel == null) return;
            switch (r.Outcome)
            {
                case RunRecovery.FinishOutcome.Saved:
                    nav.Push(new RecoveredRunScreen(run, r.Summary));
                    break;
                case RunRecovery.FinishOutcome.KeptLocal:
                    // 연결이 안 돼 폰에 남김(다음에 홈에서 보냄, 인증은 못 함) → 이 폰의 값으로 간이 결과(계획 0051 정할 것 1 가)
                    nav.Push(RunResultScreen.Offline(run.StartedAtUtc, run.FinishBody.Sensor.DistanceM, run.FinishBody.Sensor.DurationS,
                                                     run.Start?.Goal));
                    break;
                case RunRecovery.FinishOutcome.AlreadyClosed when r.RunId != null:
                    nav.Push(new RunResultScreen(r.RunId)); // 이미 끝난 기록(다른 값으로) → 그 결과
                    break;
                default:
                    Feedback.ShowError(RecoveryText.Title, RecoveryText.Rejected, r.Error?.RequestId);
                    break;
            }
        }

        // "버리기" 확인: discard(연결이 안 되면 다시 시도, 진행 기록은 그대로). runId를 모르면 같은 clientRunId로 시작을 다시 보내 ID를 받고
        // 버림(서버가 받았는데 응답만 못 받은 러닝이 서버에 남아 다음에 또 묻지 않게, 리뷰). 서버가 시작을 받지 않으면(4xx) 폰 기록만
        static async void Discard(AppScreen home, RecoveredRun run)
        {
            if (working || sheet == null) return;
            working = true;
            var mine = sheet;
            mine.Locked = true;
            bool finishedAlready = false;
            bool ok;
            try
            {
                ok = await Feedback.Run(async () =>
                {
                    string id = run.RunId;
                    if (id == null && run.Start != null)
                    {
                        try { id = run.RunId = (await Api.Runs.StartAsync(run.Start)).RunId; }
                        catch (ApiException e) when (e.Failure != ApiFailure.NotLoggedIn && !RunRecordUploader.ShouldKeep(e)) { id = null; }
                    }
                    if (id != null) await Api.Runs.DiscardAsync(id);
                }, () => Discard(home, run), e =>
                {
                    if (e.Code != "RUN_ALREADY_FINISHED") return false;
                    finishedAlready = true; // 그새 끝난 기록(다른 기기 등): 버릴 수 없음 → 진행 기록만 지우고 알림
                    return true;
                });
            }
            finally
            {
                working = false;
                mine.Locked = false;
            }
            if (!ok && !finishedAlready) return;
            if (run.ClientRunId != null) ActiveRunStore.Default.Clear(run.ClientRunId);
            mine.Close();
            if (finishedAlready) Feedback.ShowError(RecoveryText.DiscardFailedTitle, "이미 끝난 기록이라 버릴 수 없어요. 기록 탭에서 볼 수 있어요.");
        }
    }
}
