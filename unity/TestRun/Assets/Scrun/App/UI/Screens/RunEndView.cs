using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // 달리는 중 멈춤·끝(계획 0037, BL-14, Figma D-17·D-18·D-19·D-22). 맵 화면 안의 층(RunSetupView와 같은 방식).
    //   D-10 멈춤 버튼 → D-17 일시정지 ─ 계속 달리기 → 달리기(걸음이 다시 잡히면 시간 흐름)
    //                                 └ 종료하기 → D-18 ─ 계속 달리기 → 달리기
    //                                                  └ 종료하고 인증 → D-22
    //   목표 도달 → D-19(달리기는 그대로) ─ 계속 달리기 → 닫힘 / 마무리하기 → D-22(D-18 없이, Figma 노트)
    //   D-22 ─ 계기판 촬영하기 → 인증(지환 E-02) / 촬영 없이 마무리 → 미인증
    // 단계 판단은 맵 화면(MapScreen)이 하고, 이 층은 보여 주고 누른 것을 알리기만 한다
    public enum RunEndPanel { None, Pause, Confirm, Goal, Done, MeasureLost, Heat }

    public sealed class RunEndView : IDisposable
    {
        readonly VisualElement root;
        readonly VisualElement pause, confirm, goal, done, measureLost, heat;
        readonly Label pauseDistance, pauseTime, confirmBody, goalBanner, goalTime, doneBody, doneSaving;
        readonly Button doneVerify, doneSkip, heatLower;
        readonly Label heatBody;

        public event Action Resume;       // D-17·D-18 "계속 달리기"
        public event Action AskFinish;    // D-17 "종료하기" → D-18
        public event Action Finish;       // D-18 "종료하고 인증", D-19 "마무리하기"
        public event Action CloseGoal;    // D-19 "계속 달리기"
        public event Action Verify;       // D-22 "계기판 촬영하기"
        public event Action SkipVerify;   // D-22 "촬영 없이 마무리 (미인증)"
        public event Action LostPause;    // D-15 "일시정지"
        public event Action LostRetry;    // D-15 "다시 측정"(센서를 껐다 켬, 카드는 데이터가 들어오면 닫힘)
        public event Action HeatContinue; // I-12 "계속 달리기"
        public event Action HeatLower;    // I-12 "품질 낮추기"

        public RunEndPanel Shown { get; private set; } = RunEndPanel.None;

        public RunEndView(VisualElement host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            var tree = Resources.Load<VisualTreeAsset>("UI/RunEnd")
                       ?? throw new InvalidOperationException("UXML을 찾지 못함: Resources/UI/RunEnd.uxml");
            var container = tree.Instantiate();
            container.style.position = Position.Absolute;
            container.style.left = 0;
            container.style.top = 0;
            container.style.right = 0;
            container.style.bottom = 0;
            container.pickingMode = PickingMode.Ignore;
            root = container;

            pause = Q<VisualElement>("pause");
            confirm = Q<VisualElement>("confirm");
            goal = Q<VisualElement>("goal");
            done = Q<VisualElement>("done");
            measureLost = Q<VisualElement>("measure-lost");
            heat = Q<VisualElement>("heat");
            pauseDistance = Q<Label>("pause-distance");
            pauseTime = Q<Label>("pause-time");
            confirmBody = Q<Label>("confirm-body");
            goalBanner = Q<Label>("goal-banner");
            goalTime = Q<Label>("goal-time");
            doneBody = Q<Label>("done-body");
            doneSaving = Q<Label>("done-saving");
            doneVerify = Q<Button>("done-verify");
            doneSkip = Q<Button>("done-skip");

            Q<Button>("pause-resume").clicked += () => Resume?.Invoke();
            Q<Button>("pause-finish").clicked += () => AskFinish?.Invoke();
            Q<Button>("confirm-resume").clicked += () => Resume?.Invoke();
            Q<Button>("confirm-finish").clicked += () => Finish?.Invoke();
            Q<Button>("goal-continue").clicked += () => CloseGoal?.Invoke();
            Q<Button>("goal-finish").clicked += () => Finish?.Invoke();
            doneVerify.clicked += () => Verify?.Invoke();
            doneSkip.clicked += () => SkipVerify?.Invoke();
            Q<Button>("lost-pause").clicked += () => LostPause?.Invoke();
            Q<Button>("lost-retry").clicked += () => LostRetry?.Invoke();
            Q<Button>("heat-continue").clicked += () => HeatContinue?.Invoke();
            heatLower = Q<Button>("heat-lower");
            heatLower.clicked += () => HeatLower?.Invoke();
            heatBody = Q<Label>("heat-body");

            Show(RunEndPanel.None);
            host.Add(root); // 다 만든 뒤에 붙인다
        }

        public void ShowPause(float distanceM, double sec)
        {
            pauseDistance.text = RunEndText.Km(distanceM);
            pauseTime.text = RunSession.TimeText(sec);
            Show(RunEndPanel.Pause);
        }

        public void ShowConfirm(float distanceM, double sec)
        {
            confirmBody.text = RunEndText.ConfirmBody(distanceM, sec);
            Show(RunEndPanel.Confirm);
        }

        public void ShowGoal(RunGoal goal, double sec)
        {
            goalBanner.text = RunEndText.GoalBanner(goal);
            goalTime.text = RunSession.TimeText(sec);
            Show(RunEndPanel.Goal);
        }

        // D-22. saving: 서버에 보내는 중이면 버튼을 막고 한 줄 안내
        public void ShowDone(float distanceM)
        {
            doneBody.text = RunEndText.DoneBody(distanceM);
            SetSaving(RunSaveState.Saving);
            Show(RunEndPanel.Done);
        }

        public void SetSaving(RunSaveState state)
        {
            doneSaving.text = RunEndText.Saving(state);
            doneSaving.SetVisible(!string.IsNullOrEmpty(doneSaving.text));
            bool ready = state != RunSaveState.Saving;
            doneVerify.SetEnabled(ready && state == RunSaveState.Saved); // 인증은 서버 기록이 있어야
            doneSkip.SetEnabled(ready);
        }

        public void ShowMeasureLost() => Show(RunEndPanel.MeasureLost);
        // canLower: 그래픽 품질을 더 낮출 수 있음(이미 가장 낮으면 "품질 낮추기"를 숨기고 다른 안내)
        public void ShowHeat(bool canLower)
        {
            heatLower.SetVisible(canLower);
            heatBody.text = canLower ? RunEndText.HeatBody : RunEndText.HeatBodyLowest;
            Show(RunEndPanel.Heat);
        }

        public void Hide() => Show(RunEndPanel.None);

        // 패널이 다 닫히면 층 전체를 숨김(RunSetup·RunReady와 같게). 전엔 패널만 숨겨 화면 전체 크기의 빈 층이 남아
        // 맵 위 멈춤 버튼 터치를 가로챘음(첫 일시정지 → 계속 달리기 뒤 멈춤 버튼이 안 눌림)
        void Show(RunEndPanel p)
        {
            Shown = p;
            root.SetVisible(p != RunEndPanel.None);
            pause.SetVisible(p == RunEndPanel.Pause);
            confirm.SetVisible(p == RunEndPanel.Confirm);
            goal.SetVisible(p == RunEndPanel.Goal);
            done.SetVisible(p == RunEndPanel.Done);
            measureLost.SetVisible(p == RunEndPanel.MeasureLost);
            heat.SetVisible(p == RunEndPanel.Heat);
        }

        public void Dispose() => root.RemoveFromHierarchy();

        T Q<T>(string name) where T : VisualElement =>
            root.Q<T>(name) ?? throw new InvalidOperationException($"RunEnd.uxml에 '{name}'({typeof(T).Name})이 없음");
    }

    public enum RunSaveState
    {
        Saving,    // 서버에 보내는 중
        Saved,     // 서버에 기록됨(인증할 수 있음)
        KeptLocal, // 못 보내 기기에 남김(다음에 홈에서 보냄) → 인증은 못 함
        Failed,    // 서버가 받지 않음
    }

    // 글자(Figma 문구 그대로, 없는 상태 문구는 임시 → 상준 확인)
    public static class RunEndText
    {
        public static string Km(float m) => RunSession.DistanceText(m) + "km";

        // 계획 0039(임시 문구, Figma에 없음 → 상준 확인)
        public const string AirPodsDropped = "이어폰 연결이 끊겨 폰 센서로만 재요";
        public const string Unsteady = "측정값이 불안정해요 · 보정하고 있어요"; // I-03(Figma 문구)
        public const string HeatBody = "그래픽 품질을 낮추면 발열과 배터리 사용을 줄일 수 있어요."; // I-12(Figma 문구)
        public const string HeatBodyLowest = "그래픽 품질은 이미 가장 낮아요. 잠시 쉬거나 충전해 주세요."; // 임시(Figma에 없음)

        // D-18: "2.10km · 12:08\n기록은 계기판을 인증하면 저장돼요. 인증하지 않으면 미인증 기록으로 남아요."
        public static string ConfirmBody(float m, double sec) =>
            $"{Km(m)} · {RunSession.TimeText(sec)}\n기록은 계기판을 인증하면 저장돼요. 인증하지 않으면 미인증 기록으로 남아요.";

        // D-19 배너: "목표 달성! 3.0km 완주" / 시간 목표는 "목표 달성! 30분 완주"
        public static string GoalBanner(RunGoal g) => $"목표 달성! {g.ValueText}{g.UnitText} 완주";

        // D-22: "2.10km를 달렸어요. 폰을 거치대에서 내려 러닝머신 계기판을 촬영해 인증해요."
        public static string DoneBody(float m) => $"{Km(m)}를 달렸어요. 폰을 거치대에서 내려 러닝머신 계기판을 촬영해 인증해요.";

        public static string Saving(RunSaveState s) => s switch
        {
            RunSaveState.Saving => "기록을 저장하는 중이에요",
            RunSaveState.KeptLocal => "인터넷이 안 돼 기록을 폰에 저장했어요. 연결되면 올라가요(이번엔 인증할 수 없어요)",
            RunSaveState.Failed => "기록을 저장하지 못했어요",
            _ => "",
        };
    }
}
