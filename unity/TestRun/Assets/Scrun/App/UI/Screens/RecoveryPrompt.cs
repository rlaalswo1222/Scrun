using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // I-04 운동 중 앱 종료 → 세션 복구(계획 0052·0057, BL-37, Figma 611:2252). 앱을 켠 뒤 홈에 처음 왔을 때 한 번 묻는다(계정마다).
    // 폰 진행 기록(ActiveRunStore)과 서버 GET /runs/active 중 RunRecovery.Pick이 고른 러닝을 홈을 어둡게 덮은 가운데 안내창으로 보여 주고
    // "복구하고 인증하기" → 남은 시계열 + 종료 → E-01(RecoveredRunScreen) / "기록 버리기" → 한 번 더 확인(Figma엔 없음, 정할 것 3 가) → discard.
    // 안내창은 바깥을 눌러도 닫히지 않음(둘 중 하나를 골라야 함). 모양은 Home.uss(.hm-dim·.hm-dialog). 홈은 OnShow에서 CheckOnShow(this)만 부른다
    public static class RecoveryPrompt
    {
        static string checkedFor;      // 이번 실행에서 물어본 계정(못 물었으면 null로 되돌려 다음에 다시)
        static RecoveredRun waiting;   // 확인이 끝났을 때 홈이 안 보였으면 다음에 홈에 올 때 띄움
        static string waitingFor;      // waiting을 찾은 계정(다른 계정으로 바뀌면 버림, 리뷰)
        static bool working;
        static VisualElement dim; // 안내창 덮개(홈 Root에 붙음)

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            checkedFor = null;
            waiting = null;
            waitingFor = null;
            working = false;
            dim = null;
            confirmFor = null;
            confirmHome = null;
            confirmUid = null;
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

        static bool Open => dim?.panel != null;

        // 홈 뒤로 가기: 안내창이 떠 있으면 막음(둘 중 하나를 골라야 함), 버리기 확인이면 첫 안내로
        public static bool OnBack()
        {
            if (!Open) return false;
            if (!working && confirmFor != null) Show(confirmHome, confirmFor, confirmUid);
            return true;
        }

        static RecoveredRun confirmFor;   // 버리기 확인 중인 러닝(뒤로 가면 첫 안내로)
        static AppScreen confirmHome;
        static string confirmUid;

        static void Show(AppScreen home, RecoveredRun run, string uid)
        {
            if (working) return;
            confirmFor = null;
            var box = OpenDialog(home);
            box.Add(Text(RecoveryText.Title, "hm-dialog-title"));
            box.Add(Text(RecoveryText.Body(run.DistanceM, run.DurationS), "hm-dialog-body", "recover-stats"));
            Add(box, RecoveryText.Finish, "btn-primary hm-dialog-first", () => Finish(home, run, uid), "recover-finish");
            Add(box, RecoveryText.Discard, "btn-ghost", () => ConfirmDiscard(home, run, uid), "recover-discard");
        }

        static void ConfirmDiscard(AppScreen home, RecoveredRun run, string uid)
        {
            if (working) return;
            var box = OpenDialog(home);
            confirmFor = run;
            confirmHome = home;
            confirmUid = uid;
            box.Add(Text(RecoveryText.DiscardTitle, "hm-dialog-title"));
            box.Add(Text(RecoveryText.DiscardBody, "hm-dialog-body"));
            Add(box, RecoveryText.DiscardConfirm, "btn-danger hm-dialog-first", () => Discard(home, run), "recover-discard-confirm");
            Add(box, RecoveryText.Cancel, "btn-ghost", () => Show(home, run, uid), "recover-discard-cancel");
        }

        // 홈 Root 위에 어두운 덮개 + 가운데 흰 창. 바깥을 눌러도 안 닫힘
        static VisualElement OpenDialog(AppScreen home)
        {
            Close();
            dim = new VisualElement { name = "recover-dim" };
            dim.AddToClassList("hm");     // 화면 Root에 바로 붙어 .hm 아래가 아니므로 글자 규칙(.hm .x)이 닿게
            dim.AddToClassList("hm-dim");
            var box = new VisualElement { name = "recover-dialog" };
            box.AddToClassList("hm-dialog");
            dim.Add(box);
            home.Root.Add(dim);
            return box;
        }

        static void Close()
        {
            dim?.RemoveFromHierarchy();
            dim = null;
            confirmFor = null;
            confirmHome = null;
            confirmUid = null;
        }

        static Label Text(string text, string cls, string name = null)
        {
            var l = UiExtensions.Text(text, cls);
            if (name != null) l.name = name;
            return l;
        }

        static void Add(VisualElement box, string text, string classes, Action onClick, string name)
        {
            var b = new Button(() => onClick?.Invoke()) { text = text, name = name };
            b.AddToClassList("btn");
            foreach (var c in classes.Split(' ')) b.AddToClassList(c);
            box.Add(b);
        }

        // "복구하고 인증하기": 실패 안내의 "다시 시도"는 같은 안내창에서(안내창은 그대로 떠 있음)
        static async void Finish(AppScreen home, RecoveredRun run, string uid)
        {
            if (working || !Open) return;
            working = true;
            var mine = dim;
            RunRecovery.FinishResult r = default;
            bool ok;
            try
            {
                ok = await Feedback.Run(async () => r = await RunRecovery.FinishAsync(run, uid), () => Finish(home, run, uid));
            }
            finally
            {
                working = false;
            }
            if (!ok) return;
            if (dim == mine) Close();
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
            if (working || !Open) return;
            working = true;
            var mine = dim;
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
            }
            if (!ok && !finishedAlready) return;
            if (run.ClientRunId != null) ActiveRunStore.Default.Clear(run.ClientRunId);
            if (dim == mine) Close();
            if (finishedAlready) Feedback.ShowError(RecoveryText.DiscardFailedTitle, "이미 끝난 기록이라 버릴 수 없어요. 기록 탭에서 볼 수 있어요.");
        }
    }
}
