using UnityEngine;

namespace Scrun.App
{
    // 아이폰 발열(iOS ProcessInfo.thermalState와 같은 4단계). 달리는 중 I-12 안내(계획 0039, BL-15 ③)
    public enum ThermalLevel { Nominal, Fair, Serious, Critical }

    // 폰 상태(발열·배터리). 화면은 Native.Device로 쓴다.
    //   if (Native.Device.Thermal >= ThermalLevel.Serious) ...
    // 발열은 Swift(ScrunBridge "device.thermal"), 배터리는 Unity SystemInfo(아이폰에서 그대로 됨). 에디터는 Fake Native 창
    public interface IDeviceStatus
    {
        ThermalLevel Thermal { get; }
        float BatteryLevel { get; } // 0에서 1. 모르면 -1
        bool Charging { get; }      // 충전 중이거나 다 참
    }

    // 배터리는 Unity가 아이폰에서 바로 읽음(UIDevice.batteryLevel)
    public static class UnityBattery
    {
        public static float Level => SystemInfo.batteryLevel;
        public static bool Charging =>
            SystemInfo.batteryStatus == BatteryStatus.Charging || SystemInfo.batteryStatus == BatteryStatus.Full;
    }
}
