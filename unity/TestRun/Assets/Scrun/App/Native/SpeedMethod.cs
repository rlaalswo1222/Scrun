using System;
using UnityEngine;

namespace Scrun.App
{
    // 달리기 속도 계산 방식(계획 0060, 실험). 숫자는 Swift SpeedEstimator.method·Python fusion_live.METHOD_*와 같다.
    // 기본 = 충격 + 흔들림 폭(지금까지). 섞기 = 기본 50% + 걸음 수 × 보폭 50%(세게 밟아도 덜 튐).
    // 섞기 + 치우침 고치기 = 섞기에 더해, 기본이 보폭과 오래(60초) 어긋나면 그만큼 뺌(지쳐서 충격이 커지는 경우).
    // 새 두 방식은 에어팟을 끼고 다시 보정해야(보정점에 공중 시간) 적용되고, 그 전엔 기본과 같다.
    public enum SpeedMethod
    {
        Base = 0,
        Blend = 1,
        BlendFix = 2,
    }

    // 방식을 바꿀 수 있는 속도 센서(아이폰 진짜만. 에디터 가짜 센서는 속도를 계산하지 않아 해당 없음)
    public interface ISpeedMethodSwitch
    {
        void SetMethod(SpeedMethod method);
    }

    // 마이 탭 "속도 계산 방식(실험)". 개발 빌드에서만 보이고(출시 빌드는 늘 기본), 이 기기에만 저장
    public static class SpeedMethodPrefs
    {
        internal const string Key = "scrun.speed.method";

        public static bool Available => Debug.isDebugBuild;

        public static SpeedMethod Current
        {
            get
            {
                if (!Available) return SpeedMethod.Base;
                int v = PlayerPrefs.GetInt(Key, (int)SpeedMethod.Base);
                return Enum.IsDefined(typeof(SpeedMethod), v) ? (SpeedMethod)v : SpeedMethod.Base;
            }
        }

        // 저장하고, 켜져 있는 센서에도 바로(달리는 중에 바꿔도 다음 0.5초부터)
        public static void Set(SpeedMethod method, ISpeedSensor sensor = null)
        {
            PlayerPrefs.SetInt(Key, (int)method);
            PlayerPrefs.Save();
            ((sensor ?? Native.Speed) as ISpeedMethodSwitch)?.SetMethod(method);
        }

        public static string Name(SpeedMethod m) => m switch
        {
            SpeedMethod.Blend => "섞기",
            SpeedMethod.BlendFix => "섞기 + 치우침 고치기",
            _ => "기본",
        };

        public static string Describe(SpeedMethod m) => m switch
        {
            SpeedMethod.Blend => "충격 50% + 걸음 수 × 보폭 50%. 세게 밟아도 덜 튀어요",
            SpeedMethod.BlendFix => "섞기 + 충격이 보폭과 오래 어긋나면 천천히 고쳐요(지칠 때)",
            _ => "충격 세기 + 머리 흔들림(지금까지 방식)",
        };
    }
}
