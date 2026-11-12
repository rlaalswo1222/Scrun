using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // 달리는 중 기본 HUD(계획 0029): 거리·시간·목표 진행(BL-12 ②), 앞뒤 가장 가까운 러너 간격(④), 들어옴·나감 토스트(BL-21 ⑤),
    // 연결 상태. 맵 화면 안의 층이다(RunSetupView와 같은 방식). 섬 값은 RunLink에서 읽는다.
    public sealed class RunHudView : IDisposable
    {
        const long RefreshMs = 200;
        const double ToastSec = 3; // BL-12 ⑦과 같은 3초, 한 번에 하나

        readonly VisualElement root;
        readonly RunSession session;
        readonly IRunTogether together;
        readonly Label distance, time, goal, aheadName, aheadGap, behindName, behindGap, toast, net, dev;
        readonly VisualElement goalFill, gaps, ahead, behind;
        readonly IVisualElementScheduledItem tick;
        readonly Queue<string> toasts = new Queue<string>();
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

            goal.text = GoalText(session.Goal);
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
            Refresh();
        }

        public void Dispose()
        {
            tick?.Pause();
            if (together != null)
            {
                together.Joined -= OnJoined;
                together.Left -= OnLeft;
            }
            root.RemoveFromHierarchy();
        }

        public static string GoalText(RunGoal g) => $"목표 {g.ValueText}{(g.Kind == GoalKind.Distance ? " " : "")}{g.UnitText}";

        public static string NetText(RunTogetherStatus status, RunTogetherStop stop)
        {
            switch (status)
            {
                case RunTogetherStatus.Reconnecting: return "연결이 끊겨 다시 잇는 중이에요";
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

        void OnJoined(RemoteRunner r) => toasts.Enqueue($"{r.Nickname}님이 들어왔어요");
        void OnLeft(RemoteRunner r) => toasts.Enqueue($"{r.Nickname}님이 나갔어요");

        void Refresh()
        {
            float me = RunLink.PlayerDistanceM;
            distance.text = RunSession.DistanceText(session.DistanceM(me));
            time.text = RunSession.TimeText(session.ElapsedSec);
            goalFill.style.width = Length.Percent(session.Progress(me) * 100f);

            RefreshGaps();
            RefreshToast();

            string netText = together == null ? null : NetText(together.Status, together.StopReason);
            net.SetVisible(netText != null);
            if (netText != null) net.text = netText;

            if (Debug.isDebugBuild)
            {
                double? rtt = together?.RoundTripMs;
                dev.text = $"속도 {RunLink.PlayerSpeedKmh:0.0} km/h · 왕복 {(rtt.HasValue ? rtt.Value.ToString("0") + "ms" : "-")}" +
                           $" · {together?.Status.ToString() ?? "혼자"}";
            }
        }

        void RefreshGaps()
        {
            float lap = RunLink.LapLengthM;
            others.Clear();
            if (together != null && lap > 0f)
            {
                double now = RealtimeClock.Now();
                foreach (var r in together.Runners) others.Add((r.Nickname, r.PositionAt(now)));
            }
            var (a, b) = LoopMath.Nearest(RunLink.PlayerLapPositionM, others, lap);
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

        void RefreshToast()
        {
            double now = RealtimeClock.Now();
            if (now < toastUntil) return;
            if (toasts.Count == 0)
            {
                toast.SetVisible(false);
                return;
            }
            toast.text = toasts.Dequeue();
            toast.SetVisible(true);
            toastUntil = now + ToastSec;
        }

        T Q<T>(string name) where T : VisualElement =>
            root.Q<T>(name) ?? throw new InvalidOperationException($"RunHud.uxml에 '{name}'({typeof(T).Name})이 없음");
    }
}
