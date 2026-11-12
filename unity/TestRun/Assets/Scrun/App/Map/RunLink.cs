using UnityEngine;

namespace Scrun.App
{
    // 맵 화면(앱) ↔ 섬 씬이 달리는 동안 주고받는 것(계획 0029). MapLaunch와 같은 이유로 정적 값:
    // 앱 코드 묶음은 섬 스크립트를 부를 수 없고, 섬 쪽(AppRunBridge)이 매 프레임 여기를 읽고 쓴다.
    // 앱에서 들어오지 않은 섬 단독 Play에서는 아무도 안 씀(Active = false).
    public static class RunLink
    {
        // ---- 앱 → 섬 ----
        public static bool Active { get; private set; }  // 앱 맵 화면이 섬을 쓰는 중
        public static bool Moving { get; set; }          // 달리는 중(카운트다운 뒤). 꺼져 있으면 내 아바타 속도 0
        public static IRunTogether Together { get; private set; } // 같은 맵의 다른 러너(없으면 null)

        // ---- 섬 → 앱 (섬이 매 프레임 씀) ----
        public static bool IslandReady { get; private set; }
        public static float PlayerDistanceM { get; private set; } // 입장 위치부터 이어지는 누적 거리(m, 바퀴 넘어도 계속 늘어남)
        public static float PlayerSpeedKmh { get; private set; }  // 아바타에 쓰는 속도(달리기 전 0)
        public static float LapLengthM { get; private set; }      // 섬 코스 한 바퀴(m)

        public static float PlayerLapPositionM => LoopMath.Wrap(PlayerDistanceM, LapLengthM);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => End();

        public static void Begin(IRunTogether together)
        {
            End();
            Active = true;
            Together = together;
        }

        public static void End()
        {
            Active = false;
            Moving = false;
            Together = null;
            IslandReady = false;
            PlayerDistanceM = 0f;
            PlayerSpeedKmh = 0f;
            LapLengthM = 0f;
        }

        public static void ReportPlayer(float distanceM, float speedKmh, float lapLengthM)
        {
            IslandReady = true;
            PlayerDistanceM = distanceM;
            PlayerSpeedKmh = speedKmh;
            LapLengthM = lapLengthM;
        }

        // 섬이 내려감(다시 열릴 때까지 내 상태를 안 보냄)
        public static void IslandGone()
        {
            IslandReady = false;
            PlayerSpeedKmh = 0f;
        }

        // 실시간 방에 보낼 내 상태. 섬이 아직 안 열렸으면 null(안 보냄: 입장 위치가 아닌 0m에 나타나지 않게)
        public static MyRunState? MyState() =>
            IslandReady ? new MyRunState(PlayerLapPositionM, PlayerSpeedKmh, Moving) : (MyRunState?)null;
    }
}
