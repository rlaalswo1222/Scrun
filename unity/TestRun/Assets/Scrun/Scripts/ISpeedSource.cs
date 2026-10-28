namespace Scrun
{
    // 러닝머신 속도를 주는 곳. 지금은 키보드(FakeSpeedSource),
    // 나중에 폰 진동/에어팟 추정이나 FTMS 수신으로 바꿔 끼운다.
    public interface ISpeedSource
    {
        float SpeedKmh { get; }
    }
}
