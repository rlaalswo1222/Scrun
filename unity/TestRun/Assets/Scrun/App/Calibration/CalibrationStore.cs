using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Scrun.App
{
    // 보정 저장·불러오기(계획 0023 4번). 서버가 원본이고, 서버에 못 올린 보정은 이 폰에 남겨 두었다가 다시 올린다.
    public static class CalibrationStore
    {
        const string PendingKey = "Scrun.Calibration.Pending";

        // 보정 흐름이 끝나면 부른다. 실패하면 이 폰에 남기고 예외를 그대로 던진다(화면은 "서버에 아직 못 올렸어요")
        public static async Task SaveAsync(string model)
        {
            var body = ToUpload(model);
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
