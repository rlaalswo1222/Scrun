using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Scrun.App
{
    // 보정 저장·불러오기(계획 0023 4번). 서버가 원본이고, 서버에 못 올린 보정은 이 폰에 남겨 두었다가 다시 올린다.
    // 다시 올리는 곳: 코스 입장이 "보정 없음"(409)일 때(CourseDetailScreen).
    // 불러오는 곳: 맵 입장이 된 직후 센서에 보정이 없으면(앱을 다시 켜면 센서 보정은 비어 있음, MapScreen) EnsureLoadedAsync.
    // 로그인이 끝나면(로그아웃·다른 계정 가입·재로그인 필요) 남긴 보정과 센서의 보정을 지운다: 다음 사람이 앞 사람 보정으로 달리거나 올리지 않게.
    // EnsureLoadedAsync 결과(계획 0035 리뷰: 센서 준비 화면이 "다시 시도"와 "다시 보정"을 가르는 데 씀)
    public enum CalibrationLoad
    {
        Loaded,      // 센서에 보정이 있음(원래 있었거나 이 폰·서버에서 넣음)
        NotOnServer, // 서버에도 없음 → 다시 보정
        Unreadable,  // 서버 보정을 이 앱이 못 읽음(옛 모양) → 다시 보정
        Cancelled,   // 기다리는 사이 로그인이 바뀜(넣지 않음, 로그인 쪽이 화면을 옮김)
    }

    public static class CalibrationStore
    {
        const string PendingKey = "Scrun.Calibration.Pending";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Hook()
        {
            Session.Cleared -= OnLoginEnded;
            Session.Cleared += OnLoginEnded;
        }

        static void OnLoginEnded()
        {
            ClearPending();
            try { Native.Speed.ClearCalibration(); }
            catch (System.Exception e) { Debug.LogWarning("[보정] 로그아웃 때 센서 보정을 못 지움: " + e.Message); } // 센서 없는 빌드
        }

        // 센서에 보정이 없으면 채운다: 이 폰에 못 올린 보정이 있으면 그게 가장 새것이라 그것, 아니면 서버(GET).
        // 서버에도 없거나 모양이 다르면(옛 버전) 그대로 두고 그 이유를 돌려준다. 서버 오류는 ApiException 그대로
        public static async Task<CalibrationLoad> EnsureLoadedAsync(ISpeedSensor sensor)
        {
            if (sensor.HasCalibration) return CalibrationLoad.Loaded;
            var pending = Pending;
            if (pending != null && sensor.LoadCalibration(pending)) return CalibrationLoad.Loaded;
            int epoch = Api.Session.Epoch;
            var rec = await Api.Users.GetCalibrationAsync();
            // 기다리는 사이 로그아웃·다른 계정으로 바뀌었으면 넣지 않음(앞 사람 보정이 되살아나지 않게)
            if (Api.Session.Epoch != epoch) return CalibrationLoad.Cancelled;
            if (sensor.HasCalibration) return CalibrationLoad.Loaded; // 다른 곳에서 이미 채움
            if (rec?.Model == null || rec.Model.Type == JTokenType.Null) return CalibrationLoad.NotOnServer;
            var model = rec.Model.ToString(Newtonsoft.Json.Formatting.None);
            if (sensor.LoadCalibration(model)) return CalibrationLoad.Loaded;
            Debug.LogWarning("[보정] 서버 보정을 이 앱이 읽을 수 없음(버전이 다름) → 다시 보정해야 함");
            return CalibrationLoad.Unreadable;
        }

        // 보정 흐름이 끝나면 부른다. 실패하면 이 폰에 남기고 예외를 그대로 던진다(화면은 "서버에 아직 못 올렸어요")
        public static async Task SaveAsync(string model)
        {
            var body = ToUpload(model); // 모양이 틀리면 남겨도 못 올리므로 남기지 않고 예외(센서가 쓸 수 없는 점은 받지 않음)
            try
            {
                await Api.Users.PutCalibrationAsync(body);
                ClearPending();
            }
            catch
            {
                SetPending(model);
                throw;
            }
        }

        // 남겨 둔 보정을 다시 올린다. 성공하면 지우고, 실패하면 남긴 채 예외(ApiException)
        public static async Task UploadPendingAsync()
        {
            var model = Pending;
            if (model == null) return;
            if (CalibrationModel.Parse(model) == null)
            {
                ClearPending(); // 예전 버전 등 올릴 수 없는 것
                return;
            }
            await Api.Users.PutCalibrationAsync(ToUpload(model));
            ClearPending();
        }

        // 서버 요청 본문: points는 보여 주기용 요약, model은 원본 그대로, device는 폰 기종
        public static CalibrationUpload ToUpload(string model)
        {
            var m = CalibrationModel.Parse(model) ?? throw new System.ArgumentException("보정 모양이 틀립니다.", nameof(model));
            int measured = Mathf.RoundToInt(CalibrationLadder.StageSec - SpeedSensorBase.StageSkipSec);
            return new CalibrationUpload
            {
                Points = m.Points.Select(p => new CalibrationSummaryPoint
                {
                    SpeedKmh = (float)p.V,
                    Gait = p.Running ? "RUN" : "WALK",
                    DurationS = measured,
                }).ToList(),
                Model = JToken.Parse(model),
                Device = SystemInfo.deviceModel,
            };
        }

        public static string Pending => PlayerPrefs.GetString(PendingKey, null) is string s && s.Length > 0 ? s : null;

        static void SetPending(string model)
        {
            PlayerPrefs.SetString(PendingKey, model);
            PlayerPrefs.Save();
        }

        public static void ClearPending()
        {
            if (!PlayerPrefs.HasKey(PendingKey)) return;
            PlayerPrefs.DeleteKey(PendingKey);
            PlayerPrefs.Save();
        }
    }
}
