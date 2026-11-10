using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Scrun.App
{
    // 보정 저장·불러오기(계획 0023 4번). 서버가 원본이고, 서버에 못 올린 보정은 이 폰에 남겨 두었다가 다시 올린다.
    // 다시 올리는 곳: 코스 입장이 "보정 없음"(409)일 때(CourseDetailScreen). 로그인 뒤 GET으로 불러오기는 아직(4번 남은 것).
    // 남긴 보정은 로그인이 끝나면(로그아웃·다른 계정 가입·재로그인 필요) 지운다: 다음 사람 계정에 올라가지 않게.
    public static class CalibrationStore
    {
        const string PendingKey = "Scrun.Calibration.Pending";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Hook()
        {
            Session.Cleared -= ClearPending;
            Session.Cleared += ClearPending;
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
