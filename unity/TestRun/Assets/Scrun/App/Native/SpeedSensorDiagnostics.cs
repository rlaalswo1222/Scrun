namespace Scrun.App
{
    // 점검 화면(NativeCheckScreen)만 보는 값. 아이폰 센서(IosSpeedSensor)가 scrun_speed_read 칸 9·10으로 채운다(계획 0024).
    // 화면 코드는 ISpeedSensor만 쓰고, 이건 `Native.Speed as ISpeedSensorDiagnostics`로 있을 때만 읽는다.
    public interface ISpeedSensorDiagnostics
    {
        int PhoneSamplesPerSec { get; }   // 지난 1초 폰 가속도 샘플 수(요청 100 Hz)
        int AirPodsSamplesPerSec { get; } // 지난 1초 에어팟 헤드폰 모션 샘플 수(에어팟 프로 3 약 49 Hz)
    }
}
