namespace Scrun.App
{
    // 달리는 중 측정 예외(계획 0039, BL-15, Figma D-15·I-03·I-12)를 언제 보일지 정한다. 화면 없이 → 테스트(RunMeasureWatchTests).
    //   D-15 측정 중단: 폰 센서가 끊김(Lost) → 카드. 한 번 뜨면 폰 데이터가 다시 들어올(Ok) 때까지 그대로(다시 측정으로 센서를
    //        껐다 켜는 동안 "켜지는 중"이어도 깜박이지 않게), 들어오면 저절로 닫힘(Figma 노트 "복구되면 자동으로 이어서")
    //   에어팟만 끊김: 막지 않고 한 번 알림(정한 것 3). 다시 들어오면 다음 끊김에 또 알림
    //   I-03 측정값 이상: 아바타 속도 계산(AvatarSpeed.Unsteady)을 그대로 보여 줌
    //   I-12 배터리·발열: 발열 serious 이상 또는 충전 중이 아닌데 배터리 15% 이하(정한 것 5, 가설) → 이번 달리기에 한 번만
    // 시간은 막지 않는다: 측정이 끊겨도 러닝머신은 돌고 있어 계기판 시간과 맞아야 인증 대조가 됨(정한 것 1)
    public struct MeasureInputs
    {
        public bool Running;      // 달리는 중(일시정지·끝·패널이 떠 있는 동안은 false로 넘김)
        public SensorFlow Phone;
        public SensorFlow AirPods;
        public bool Unsteady;
        public ThermalLevel Thermal;
        public float Battery;     // 0에서 1, 모르면 -1
        public bool Charging;
    }

    public struct MeasureChange
    {
        public bool ShowLost;       // D-15를 띄움
        public bool HideLost;       // D-15를 닫음(폰 데이터가 다시 들어옴)
        public bool AirPodsDropped; // "이어폰 연결이 끊겨 폰 센서로만 재요" 한 번
        public bool AskHeat;        // I-12를 띄움(이번 달리기에 한 번)
    }

    public sealed class RunMeasureWatch
    {
        public const ThermalLevel HeatFrom = ThermalLevel.Serious;
        public const float LowBattery = 0.15f;

        public bool LostShown { get; private set; }
        public bool Unsteady { get; private set; }
        bool airPodsWasOk, heatAsked;

        public static bool TooHot(in MeasureInputs i) =>
            i.Thermal >= HeatFrom || (i.Battery >= 0f && i.Battery <= LowBattery && !i.Charging);

        public MeasureChange Update(in MeasureInputs i)
        {
            var c = new MeasureChange();
            if (LostShown && i.Phone == SensorFlow.Ok)
            {
                LostShown = false;
                c.HideLost = true;
            }
            Unsteady = i.Running && i.Phone == SensorFlow.Ok && i.Unsteady;
            if (!i.Running) return c; // 패널이 떠 있거나 멈춘 동안은 새로 띄우지 않음(닫는 것만)

            if (!LostShown && i.Phone == SensorFlow.Lost)
            {
                LostShown = true;
                c.ShowLost = true;
            }
            if (i.AirPods == SensorFlow.Ok) airPodsWasOk = true;
            else if (airPodsWasOk && i.AirPods != SensorFlow.Waiting && i.Phone == SensorFlow.Ok) // 켜지는 중은 끊김이 아님
            {
                airPodsWasOk = false;
                c.AirPodsDropped = true;
            }
            if (!heatAsked && !LostShown && TooHot(i))
            {
                heatAsked = true;
                c.AskHeat = true;
            }
            return c;
        }

        // "일시정지"로 D-15를 내림. 폰이 아직 안 들어오면 다시 달릴 때 또 뜸
        public void DismissLost() => LostShown = false;

        // "다시 측정"으로 센서를 껐다 켬: 에어팟도 꺼졌다 다시 붙으므로 "끊김"으로 세지 않게 처음부터(리뷰)
        public void SensorRestarted() => airPodsWasOk = false;
    }
}
