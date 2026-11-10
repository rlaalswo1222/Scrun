using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Scrun.App
{
    // 앱(맵 화면) → 섬 씬 이음매(계획 0022). 앱 코드 묶음은 섬 스크립트(Assets/Scrun/Scripts, 기본 덩어리)를 부를 수 없으므로
    // 앱이 여기에 "어느 코스, 몇 m 지점"을 남기고, 섬 씬이 불릴 때 섬 쪽 MapLaunchApplier가 읽어 적용한다.
    // 섬 씬을 혼자 Play하면 남긴 것이 없으므로 씬의 기본 코스 그대로.
    public static class MapLaunch
    {
        public const string SceneName = "Island01";
        const string CoursePrefix = "crs_";

        public sealed class Request
        {
            public string CourseId { get; }
            public string RouteId { get; }        // 섬 도로망(RoadNetwork) 코스 ID
            public float EntryPositionM { get; }  // 코스 시작점부터 m

            public Request(string courseId, float entryPositionM)
            {
                CourseId = courseId;
                RouteId = RouteIdOf(courseId);
                EntryPositionM = Math.Max(0f, entryPositionM);
            }
        }

        static Request pending;
        static TaskCompletionSource<bool> applied;

        // 이 프로젝트는 Play 때 도메인 리로드를 꺼 둠 → 지난 Play에 남긴 것이 섬 씬 단독 Play에 적용되지 않게
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            pending = null;
            applied = null;
        }

        // 서버 코스 ID(crs_lake_loop) → 섬 도로망 코스 ID(lake_loop). 서버 코스는 IslandLayout.json routes에서 만들었다(마이그레이션 seed_courses)
        public static string RouteIdOf(string courseId)
        {
            if (string.IsNullOrEmpty(courseId) || !courseId.StartsWith(CoursePrefix, StringComparison.Ordinal) ||
                courseId.Length == CoursePrefix.Length)
                throw new ArgumentException($"코스 ID 모양이 아님: '{courseId}'(crs_로 시작해야 함)", nameof(courseId));
            return courseId.Substring(CoursePrefix.Length);
        }

        // 맵 화면이 씬을 부르기 직전에 남긴다
        public static void Store(MapEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            pending = new Request(entry.CourseId, entry.EntryPositionM);
            applied = new TaskCompletionSource<bool>();
        }

        // 섬 씬이 코스·입장 위치를 다 적용했을 때(실패해서 기본 코스로 둔 때도) 끝난다. 맵 화면은 이걸 기다린 뒤 흐린 배경을 찍는다
        public static Task Applied => applied?.Task ?? Task.CompletedTask;

        // 섬 쪽 MapLaunchApplier가 적용을 끝내고 부른다
        public static void MarkApplied() => applied?.TrySetResult(true);

        // 섬 씬이 한 번 가져간다(두 번 적용되지 않게). 남긴 것이 없으면 null
        public static Request Take()
        {
            var r = pending;
            pending = null;
            return r;
        }

        public static bool HasPending => pending != null;

        // 씬을 못 불렀거나 화면을 떠날 때
        public static void Clear()
        {
            pending = null;
            applied?.TrySetResult(false); // 기다리던 쪽이 멈춰 있지 않게
        }
    }
}
