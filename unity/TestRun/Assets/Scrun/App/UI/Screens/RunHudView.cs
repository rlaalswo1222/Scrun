using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // 달리는 중 기본 HUD(계획 0029): 거리·시간·목표 진행(BL-12 ②), 앞뒤 가장 가까운 러너 간격(④), 들어옴·나감 토스트(BL-21 ⑤),
    // 연결 상태. S6(계획 0041): 구간 알림(⑦ D-24·D-25), 끊김 배너·흐림(D-16). 맵 화면 안의 층이다(RunSetupView와 같은 방식). 섬 값은 RunLink에서 읽는다.
    public sealed class RunHudView : IDisposable
    {
        const long RefreshMs = 200;
        const double ToastSec = 3; // BL-12 ⑦과 같은 3초, 한 번에 하나

        readonly VisualElement root;
        readonly RunSession session;
        readonly IRunTogether together;
        readonly Label distance, time, goal, aheadName, aheadGap, behindName, behindGap, toast, net, dev, unsteady, ghostBanner;
        readonly VisualElement goalFill, gaps, ahead, behind;
        readonly IVisualElementScheduledItem tick, frameTick;
        readonly LoopMinimap minimap;  // 왼쪽 위 루프 미니맵(BL-12 ③, 계획 0041)
        readonly Label mapProgress;
        readonly RunnerTagsView tagsView; // 이름표·러너 카드(BL-22)
        readonly List<(float, bool)> dots = new List<(float, bool)>();
        readonly Queue<string> toasts = new Queue<string>();
        readonly RunMoments moments; // 구간 알림(D-24·D-25, 계획 0041)
        string moment;               // 다음에 띄울 구간 알림(들어옴·나감보다 먼저)
        readonly List<(string, float)> others = new List<(string, float)>();
        double toastUntil;

        public RunHudView(VisualElement host, RunSession session, IRunTogether together)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            this.session = session ?? throw new ArgumentNullException(nameof(session));
            this.together = together;

            var tree = Resources.Load<VisualTreeAsset>("UI/RunHud")
                       ?? throw new InvalidOperationException("UXML을 찾지 못함: Resources/UI/RunHud.uxml");
            var container = tree.Instantiate();
            container.pickingMode = PickingMode.Ignore;
            container.style.position = Position.Absolute;
            container.style.left = 0;
            container.style.top = 0;
            container.style.right = 0;
            container.style.bottom = 0;
            root = container;

            distance = Q<Label>("hud-distance");
            time = Q<Label>("hud-time");
            goal = Q<Label>("hud-goal");
            goalFill = Q<VisualElement>("hud-goal-fill");
            gaps = Q<VisualElement>("hud-gaps");
            ahead = Q<VisualElement>("hud-ahead");
            behind = Q<VisualElement>("hud-behind");
            aheadName = Q<Label>("hud-ahead-name");
            aheadGap = Q<Label>("hud-ahead-gap");
            behindName = Q<Label>("hud-behind-name");
            behindGap = Q<Label>("hud-behind-gap");
            toast = Q<Label>("hud-toast");
            net = Q<Label>("hud-net");
            dev = Q<Label>("hud-dev");
            unsteady = Q<Label>("hud-unsteady");
            unsteady.text = RunEndText.Unsteady;
            unsteady.SetVisible(false);
            ghostBanner = Q<Label>("hud-ghost");
            ghostBanner.SetVisible(false);

            goal.text = GoalText(session.Goal);
            moments = new RunMoments(session.Goal);
            minimap = new LoopMinimap();
            minimap.style.flexGrow = 1;
            Q<VisualElement>("hud-map").Add(minimap);
            mapProgress = Q<Label>("hud-map-progress");
            tagsView = new RunnerTagsView(root, together);
            dev.SetVisible(Debug.isDebugBuild);
            toast.SetVisible(false);
            net.SetVisible(false);

            if (together != null)
            {
                together.Joined += OnJoined;
                together.Left += OnLeft;
            }

            host.Add(root); // 다 만든 뒤에 붙인다
            tick = root.schedule.Execute(Refresh).Every(RefreshMs);
            frameTick = root.schedule.Execute(() => tagsView.Tick(together != null && OthersStale(together.Status))).Every(0); // 이름표는 매 프레임
            Refresh();
        }

        public void Dispose()
        {
            tick?.Pause();
            frameTick?.Pause();
            RunLink.MyRunM = null;
            if (together != null)
            {
                together.Joined -= OnJoined;
                together.Left -= OnLeft;
            }
            root.RemoveFromHierarchy();
        }

        public static string GoalText(RunGoal g) => $"목표 {g.ValueText}{(g.Kind == GoalKind.Distance ? " " : "")}{g.UnitText}";

        // D-16(BL-26, 계획 0041): 끊긴 동안 위 가운데 배너, 다른 러너 정보는 흐리게. 내 달리기·기록은 그대로(폰에 쌓이고
        // 다시 붙으면 1분 묶음이 이어서 올라감, 0037). 오래 끊겨도 막지 않고 계속 다시 붙음(정할 것 1)
        public const string Reconnecting = "연결이 불안정해요 · 재연결 중…";

        // 다른 러너 위치를 믿을 수 없는 동안(끊겨서 마지막 값으로 내다보는 중) → 간격 카드·이름표를 흐리게
        public static bool OthersStale(RunTogetherStatus status) =>
            status == RunTogetherStatus.Reconnecting || status == RunTogetherStatus.Stopped;

        public static string NetText(RunTogetherStatus status, RunTogetherStop stop)
        {
            switch (status)
            {
                case RunTogetherStatus.Reconnecting: return Reconnecting; // D-16
                case RunTogetherStatus.Stopped:
                    switch (stop)
                    {
                        case RunTogetherStop.ReplacedElsewhere: return "다른 기기에서 같은 계정으로 들어와 연결을 끊었어요";
                        case RunTogetherStop.NeedsReentry: return "같이 뛰기 연결이 끝났어요. 맵에 다시 들어와 주세요";
                        case RunTogetherStop.ProtocolError: return "같이 뛰기 연결에 문제가 생겼어요";
                        default: return null;
                    }
                default: return null;
            }
        }

        // 이번 달리기의 고스트(계획 0050). 맵 화면이 시계열을 받으면 넣음(받기 전·고르지 않았으면 null)
        public GhostRun Ghost { get; set; }

        // 위 가운데 3초 알림(한 번에 하나). 에어팟 끊김 등(계획 0039)
        public void Notice(string text) => toasts.Enqueue(text);

        // I-03 측정값 이상 띠(계획 0039)
        public void SetUnsteady(bool on) => unsteady.SetVisible(on);

        // 봇은 사람이 들고 날 때 빈자리를 채우며 따라 들고 나므로(계획 0040) 알리지 않음
        void OnJoined(RemoteRunner r)
        {
            if (!r.IsBot) toasts.Enqueue($"{r.Nickname}님이 들어왔어요");
        }

        void OnLeft(RemoteRunner r)
        {
            if (!r.IsBot) toasts.Enqueue($"{r.Nickname}님이 나갔어요");
        }

        void Refresh()
        {
            float me = RunLink.PlayerDistanceM;
            distance.text = RunSession.DistanceText(session.DistanceM(me));
            time.text = RunSession.TimeText(session.ElapsedSec);
            goalFill.style.width = Length.Percent(session.Progress(me) * 100f);

            RunLink.MyRunM = session.Started ? session.DistanceM(me) : (float?)null; // 다른 사람의 러너 카드 "누적 거리"
            RefreshGhost(me);
            RefreshGaps(me);
            RefreshMinimap(me);
            if (session.Phase == RunPhase.Running)
                moment = moments.Check(session.DistanceM(me), session.ElapsedSec, NearestPerson()) ?? moment;
            RefreshToast();

            string netText = together == null ? null : NetText(together.Status, together.StopReason);
            net.SetVisible(netText != null);
            if (netText != null) net.text = netText;
            bool stale = together != null && OthersStale(together.Status);
            gaps.EnableInClassList("hud-dimmed", stale);

            if (Debug.isDebugBuild)
            {
                double? rtt = together?.RoundTripMs;
                dev.text = $"속도 {RunLink.PlayerSpeedKmh:0.0} km/h · 왕복 {(rtt.HasValue ? rtt.Value.ToString("0") + "ms" : "-")}" +
                           $" · {together?.Status.ToString() ?? "혼자"}" + MethodsDev();
            }
        }

        // 개발 표시: 세 방식 값과 에어팟 공중 시간(계획 0060). 아이폰 센서가 덧붙인 칸을 줄 때만
        internal static string MethodsDev() => MethodsDev((Native.Speed as ISpeedSensorDiagnostics)?.Methods ?? default);

        internal static string MethodsDev(SpeedMethodReading m)
        {
            if (!m.Has) return "";
            static string K(float v) => float.IsNaN(v) ? "-" : v.ToString("0.0");
            return $"\n기본 {K(m.BaseKmh)} · 섞기 {K(m.BlendKmh)} · 고치기 {K(m.FixKmh)} · 공중 " +
                   (float.IsNaN(m.FlightMs) ? "-" : m.FlightMs.ToString("0") + "ms") + $" · 쓰는 중 {SpeedMethodPrefs.Name(m.InUse)}";
        }

        // D-13 배너: 같은 출발선·같은 시계라 거리 차이 = 내 이번 거리 − 고스트 거리
        void RefreshGhost(float me)
        {
            var g = Ghost;
            bool on = g != null && session.Started;
            ghostBanner.SetVisible(on);
            if (!on) return;
            double t = session.ElapsedSec;
            ghostBanner.text = GhostText.Banner(session.DistanceM(me), g.Track.DistanceAt(t), g.Track.FinishedAt(t));
        }

        void RefreshGaps(float me)
        {
            float lap = RunLink.LapLengthM;
            others.Clear();
            if (together != null && lap > 0f)
            {
                double now = RealtimeClock.Now();
                foreach (var r in together.Runners) others.Add((r.DisplayName, r.PositionAt(now)));
            }
            var (a, b) = LoopMath.Nearest(RunLink.PlayerLapPositionM, others, lap);
            if (Ghost != null && session.Started) // 앞뒤 간격 줄에 "어제의 나"(D-13). 바퀴로 접지 않은 차이(배너와 같은 값)
                (a, b) = LoopMath.WithExact((a, b), GhostText.Name, Ghost.Track.DistanceAt(session.ElapsedSec) - session.DistanceM(me));
            gaps.SetVisible(a != null || b != null);
            ahead.SetVisible(a != null);
            behind.SetVisible(b != null);
            if (a != null)
            {
                aheadName.text = a.Value.Name;
                aheadGap.text = LoopMath.GapText(a.Value.GapM, ahead: true);
            }
            if (b != null)
            {
                behindName.text = b.Value.Name;
                behindGap.text = LoopMath.GapText(b.Value.GapM, ahead: false);
            }
        }

        void RefreshMinimap(float me)
        {
            float lap = RunLink.LapLengthM;
            dots.Clear();
            if (together != null && lap > 0f)
            {
                double now = RealtimeClock.Now();
                foreach (var r in together.Runners) dots.Add((r.PositionAt(now), r.IsBot));
            }
            minimap.Set(RunLink.RouteShape, lap, RunLink.PlayerLapPositionM, dots);
            minimap.EnableInClassList("hud-dimmed", together != null && OthersStale(together.Status));
            mapProgress.text = MapProgressText(session.Goal, session.DistanceM(me), session.ElapsedSec);
        }

        // 미니맵 아래 "2.1 / 3.0km"(거리 목표), "12 / 30분"(시간 목표)
        public static string MapProgressText(RunGoal g, float distanceM, double elapsedSec)
        {
            if (g.Kind == GoalKind.Distance)
                return (Math.Floor(distanceM / 100.0) / 10.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                       " / " + g.ValueText + g.UnitText;
            return ((int)(elapsedSec / 60)).ToString(System.Globalization.CultureInfo.InvariantCulture) + " / " + g.ValueText + g.UnitText;
        }

        // 가장 가까운 사람(봇 빼고, 끊긴 동안은 위치를 못 믿어 없음): 이름, 간격(앞 +, 뒤 −)
        (string, float)? NearestPerson()
        {
            float lap = RunLink.LapLengthM;
            if (together == null || lap <= 0f || OthersStale(together.Status)) return null;
            double now = RealtimeClock.Now();
            float me = RunLink.PlayerLapPositionM;
            (string, float)? best = null;
            foreach (var r in together.Runners)
            {
                if (r.IsBot) continue;
                float gap = LoopMath.Delta(me, r.PositionAt(now), lap);
                if (best == null || System.Math.Abs(gap) < System.Math.Abs(best.Value.Item2)) best = (r.Nickname, gap);
            }
            return best;
        }

        void RefreshToast()
        {
            double now = RealtimeClock.Now();
            if (now < toastUntil) return;
            if (moment == null && toasts.Count == 0)
            {
                toast.SetVisible(false);
                return;
            }
            if (moment != null)
            {
                toast.text = moment;
                moment = null;
            }
            else toast.text = toasts.Dequeue();
            toast.SetVisible(true);
            toastUntil = now + ToastSec;
        }

        T Q<T>(string name) where T : VisualElement =>
            root.Q<T>(name) ?? throw new InvalidOperationException($"RunHud.uxml에 '{name}'({typeof(T).Name})이 없음");
    }
}
