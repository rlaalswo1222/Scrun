using System;
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
        // 섬 → 앱: 그래픽 품질 한 단계 낮추기(ScrunQuality가 섬 씬에서 채움, I-12 "품질 낮추기", 계획 0039). 못 낮추면 false
        public static Func<bool> LowerQuality { get; set; }
        public static Func<bool> CanLowerQuality { get; set; } // 지금보다 낮은 단계가 있음
        public static bool SpeedUnsteady { get; set; }            // 섬 → 앱: 추정이 이상해 직전 속도로 버티는 중(AvatarSpeed.Unsteady, I-03)
        // 섬 → 앱(계획 0041): 미니맵용 코스 모양(위에서 본 x·z, 한 바퀴를 같은 간격으로 나눈 점, i번째 = i/개수 × 한 바퀴)
        public static Vector2[] RouteShape { get; set; }
        // 섬 → 앱(계획 0041): 다른 러너 머리 위 화면 위치(픽셀, y는 아래에서 위). 카메라 앞 이름표 거리 안의 사람만, 매 프레임 다시 채움
        public static readonly System.Collections.Generic.Dictionary<string, Vector2> Heads =
            new System.Collections.Generic.Dictionary<string, Vector2>();

        // ---- 앱 안 ----
        public static float? MyRunM { get; set; } // 이번 달리기 거리(m, HUD가 채움, 실시간 state의 run)
        // 앱 → 섬: 내 아바타를 코스 위 한 바퀴 안 위치(m)로 옮김(같이 달리기, 계획 0047). 섬이 열려 있을 때만 있음(AppRunBridge가 넣음).
        // 부르면 바로 ReportPlayer까지 해서 PlayerDistanceM이 새 자리
        public static Action<float> TeleportPlayer { get; set; }

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
            SpeedUnsteady = false;
            RouteShape = null;
            Heads.Clear();
            MyRunM = null;
            TeleportPlayer = null;
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
            Heads.Clear();
            RouteShape = null; // 다시 열린 섬이 새로 넣음(앞 코스 모양이 남지 않게)
        }

        // 실시간 방에 보낼 내 상태. 섬이 아직 안 열렸으면 null(안 보냄: 입장 위치가 아닌 0m에 나타나지 않게)
        public static MyRunState? MyState() =>
            IslandReady ? new MyRunState(PlayerLapPositionM, PlayerSpeedKmh, Moving, MyRunM) : (MyRunState?)null;
    }
}
