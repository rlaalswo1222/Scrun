using System;
using System.Collections.Generic;

namespace Scrun.App
{
    // 서버 요청·응답 모양(필드 이름은 JSON에서 camelCase로 바뀜). 기준은 backend/app/schemas, docs/api/openapi.json.
    // 서버 모양을 바꾸면 여기도 같이 바꾼다.

    public sealed class LoginResult
    {
        public bool IsNewUser { get; set; }      // true면 가입 화면으로(그때는 SignupToken만 있음)
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public int? ExpiresInS { get; set; }
        public string SignupToken { get; set; }
    }

    public sealed class TokenPair
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public int ExpiresInS { get; set; }
    }

    public sealed class Agreement
    {
        public string AgreementId { get; set; }
        public string Title { get; set; }
        public bool Required { get; set; }
        public string Version { get; set; }
        public string Url { get; set; } // 약관 본문 주소
    }

    public sealed class SignupRequest
    {
        public List<string> AgreedIds { get; set; } = new List<string>();
        public string Nickname { get; set; }
        public int HeightCm { get; set; }  // 100–230
        public int WeightKg { get; set; }  // 20–250
        public string Timezone { get; set; } = "Asia/Seoul";
        public string DeviceId { get; set; } // 비우면 ApiClient가 이 기기 값으로 채움
    }

    // GET·PUT /v1/users/me/calibration (계획 0023). points는 보여 주기용 요약, model은 보정점 원본(서버는 해석 안 함)
    public sealed class CalibrationSummaryPoint
    {
        public float SpeedKmh { get; set; }
        public string Gait { get; set; }  // WALK / RUN
        public int DurationS { get; set; } // 잰 시간(버린 앞 시간 제외)
    }

    public sealed class CalibrationUpload
    {
        public List<CalibrationSummaryPoint> Points { get; set; } = new List<CalibrationSummaryPoint>();
        public Newtonsoft.Json.Linq.JToken Model { get; set; }
        public string Device { get; set; } // 폰 기종(예: iPhone15,4). 바뀌면 재보정 안내(ADR-0005 3번)
    }

    public sealed class CalibrationRecord
    {
        public List<CalibrationSummaryPoint> Points { get; set; }
        public Newtonsoft.Json.Linq.JToken Model { get; set; }
        public string Device { get; set; }
        public DateTime? CreatedAt { get; set; }
    }

    public sealed class NicknameCheck
    {
        public string Nickname { get; set; } // 서버가 정규화한 닉네임(가입 때 이대로 저장됨) → 화면에 이 값을 보여 줌
        public bool Available { get; set; }
        public string Reason { get; set; }   // INVALID(형식) / NOT_ALLOWED(금칙어) / DUPLICATED(중복) / null
    }

    // 코스(GET /v1/courses·/{courseId}, 계획 0021·PR #30). 코스 하나 = 맵 하나
    public class Course
    {
        public string CourseId { get; set; }     // crs_lake_loop 등. 섬 도로망 코스 ID는 앞의 crs_를 뗀 것(MapLaunch.RouteIdOf)
        public string Name { get; set; }
        public string Type { get; set; }         // LOOP_3D(실사 영상 VIDEO는 자리만)
        public int LengthM { get; set; }         // 한 바퀴 m
        public int RunnersNow { get; set; }      // 그 맵에 지금 있는 사람 수
        public string ThumbnailUrl { get; set; } // 없으면 null → 기본 그림
    }

    public sealed class CourseDetail : Course
    {
        public string Description { get; set; }
        public string Mood { get; set; }
        public int MaxRunnersPerMap { get; set; }
    }

    // 맵 입장(POST /v1/courses/{courseId}/entries) 응답
    public sealed class MapEntry
    {
        public string MapInstanceId { get; set; }  // 실시간 서버 방 ID로 씀(지금은 map_<courseId>)
        public string CourseId { get; set; }
        public float EntryPositionM { get; set; }  // 코스 시작점부터 m
        public RealtimeAccess Realtime { get; set; } // 실시간 서버(BL-25) 전에는 null
    }

    public sealed class RealtimeAccess
    {
        public string Url { get; set; }
        public string Token { get; set; }
    }

    // 주간 목표 진행(GET /v1/users/me/weekly-goal). 1회 = 인증 완료 + 5분 이상(서버 집계)
    // PUT /v1/users/me/weekly-goal 본문. 1–7회(서버 WeeklyGoalUpdate)
    public sealed class WeeklyGoalUpdate
    {
        public int TargetCount { get; set; }
    }

    public sealed class WeeklyGoal
    {
        public int? TargetCount { get; set; }     // 목표를 안 정했으면 null
        public int DoneCount { get; set; }
        public int? DaysLeft { get; set; }
        public int? NextTargetCount { get; set; } // 변경 예약값(다음 주기부터)
    }

    // 러닝 목표(POST /v1/runs의 goal). RunGoal.ToRequest()로 만든다
    public sealed class RunGoalBody
    {
        public string Type { get; set; }  // DISTANCE / TIME / NONE
        public int? Value { get; set; }   // DISTANCE면 m, TIME이면 초
    }

    // POST /v1/runs 요청(계획 0035). 고스트는 S7 BL-24
    public sealed class RunStartBody
    {
        public string ClientRunId { get; set; }   // 앱이 만든 ID(다시 보내도 기록 하나)
        public string MapInstanceId { get; set; } // 입장 응답의 mapInstanceId
        public RunGoalBody Goal { get; set; }
        public string GhostRunId { get; set; }
        public DateTime StartedAt { get; set; }   // UTC, 카운트다운이 끝난 시각
    }

    public sealed class RunStarted
    {
        public string RunId { get; set; }
        public string ClientRunId { get; set; }
        public DateTime StartedAt { get; set; }
    }

    // PUT /v1/runs/{runId}/samples(계획 0037): 달린 시간 기준 offsetS(일시정지 구간은 빠짐)부터 1초 간격 속도
    public sealed class RunSamplesBody
    {
        public double OffsetS { get; set; }
        public double IntervalS { get; set; }
        public float[] SpeedsKmh { get; set; }
    }

    public sealed class RunStatsBody
    {
        public int DistanceM { get; set; }
        public int DurationS { get; set; } // 일시정지를 뺀 달린 시간
    }

    // POST /v1/runs/{runId}/finish
    public sealed class RunFinishBody
    {
        public DateTime EndedAt { get; set; } // UTC
        public RunStatsBody Sensor { get; set; }
    }

    // 기록 요약(종료 응답 등). verification: PENDING(인증 대기)·VERIFIED·UNVERIFIED
    public sealed class RunSummary
    {
        public string RunId { get; set; }
        public DateTime StartedAt { get; set; }
        public string CourseId { get; set; }
        public string CourseName { get; set; }
        public int DistanceM { get; set; }
        public int DurationS { get; set; }
        public int? AvgPaceSPerKm { get; set; }
        public string Verification { get; set; }
        public bool Shared { get; set; }
    }

    // ---- 계기판 인증(계획 0038, BL-16–18) ----

    // POST /v1/runs/{runId}/verification/photo·skip 응답. status: VERIFIED(인증 완료, 저장)·UNVERIFIED(미인증, 저장)·
    // RETAKE_REQUIRED(아직 저장 안 됨, 다시 찍기). reason: UNVERIFIED면 미인증 사유, RETAKE_REQUIRED면 MISMATCH(오차 초과, E-08)·
    // UNSUPPORTED_CONSOLE(읽을 수 없는 계기판, E-09)·null(못 읽음, E-07)
    public sealed class VerificationResult
    {
        public string Status { get; set; }
        public string Reason { get; set; }
        public RunStatsBody Console { get; set; } // 계기판에서 읽은 값(없을 수 있음)
        public RunStatsBody Sensor { get; set; }
        public List<ComparisonRow> Comparison { get; set; } = new List<ComparisonRow>();
        public DateTime? RetakeDeadline { get; set; } // 다시 찍기·값 확인일 때 마감(UTC, 값 확인은 유예 포함)
        public int? RetakesLeft { get; set; }
        public string AttemptId { get; set; } // CONFIRM_REQUIRED일 때 확인할 시도(confirm에 그대로, 계획 0045)
    }

    // 대조 표 한 줄: item = distance·duration, diffRatio = (계기판 − 센서) / 계기판
    public sealed class ComparisonRow
    {
        public string Item { get; set; }
        public double? Console { get; set; }
        public double Sensor { get; set; }
        public double? DiffRatio { get; set; }
        public bool Ok { get; set; }
    }
}
