using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Scrun.App
{
    // 개발 빌드 아이폰에서 계기판 인증 사진을 남긴다(계획 0085). 서버로 보낸 바이트 그대로 + meta.json(센서 값·서버 답·
    // "맞아요/틀렸어요"). 앱이 실제로 보낸 사진으로 OCR을 채점하려고 — 출시 빌드는 아무것도 안 남김(BL-17 ③).
    // 폴더: Documents/verify-photos/<UTC 시각>_<기록 ID 앞 8자>/ (보내기 한 번 = 폴더 하나). 맥에서 꺼내기·채점은
    // speedlab/ocr/README.md "앱이 보낸 인증 사진으로 채점"(명령은 그 한 곳에만).
    // 저장이 실패해도(공간 부족 등) 경고만 남기고 인증은 그대로 진행한다.
    public sealed class VerifyPhotoLog
    {
        // 에디터·출시 빌드에서는 남기지 않음(RunEventLog와 같은 조건)
        public static bool Enabled => !Application.isEditor && Debug.isDebugBuild;
        public static string DefaultRoot => Path.Combine(Application.persistentDataPath, "verify-photos");

        public string Dir { get; }
        readonly JObject meta;

        VerifyPhotoLog(string dir, JObject meta)
        {
            Dir = dir;
            this.meta = meta;
        }

        // 보내기 직전. 남기지 않는 빌드면 null, 저장에 실패해도 null(인증은 계속).
        // exposureBiases: parts와 같은 순서의 노출(EV, VerifyPhotos.SendOrder). root·enabled는 테스트용
        public static VerifyPhotoLog Begin(string runId, IReadOnlyList<MultipartBody.FilePart> parts,
                                           IReadOnlyList<float> exposureBiases, RunStatsBody sensor,
                                           DateTime nowUtc, string root = null, bool? enabled = null)
        {
            if (!(enabled ?? Enabled) || parts == null || parts.Count == 0) return null;
            try
            {
                string id = runId ?? "";
                string name = nowUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "_" +
                              (id.Length > 8 ? id.Substring(0, 8) : id);
                string dir = Path.Combine(root ?? DefaultRoot, name);
                if (Directory.Exists(dir)) dir += "_" + Guid.NewGuid().ToString("N").Substring(0, 4); // 같은 초에 두 번
                Directory.CreateDirectory(dir);
                var photos = new JArray();
                for (int i = 0; i < parts.Count; i++)
                {
                    var p = parts[i];
                    string file = Path.GetFileName(p.FileName); // 보낸 이름 그대로(console_0_ev+0.0.jpg, 0 EV에 가까운 것부터)
                    File.WriteAllBytes(Path.Combine(dir, file), p.Data);
                    var size = VerifyPhotos.JpegSize(p.Data);
                    photos.Add(new JObject
                    {
                        ["file"] = file,
                        ["ev"] = exposureBiases != null && i < exposureBiases.Count ? exposureBiases[i] : (float?)null,
                        ["bytes"] = p.Data.Length,
                        ["width"] = size?.width,
                        ["height"] = size?.height,
                    });
                }
                var meta = new JObject
                {
                    ["time_utc"] = Iso(nowUtc),
                    ["run_id"] = id,
                    ["device"] = SystemInfo.deviceModel,
                    ["app_version"] = Application.version,
                    ["sensor"] = StatsJson(sensor),
                    ["photos"] = photos,
                    ["answers"] = new JArray(),
                    ["confirmed"] = null,   // 확인 단계 "맞아요"(true)면 answers의 console 값이 정답
                };
                var log = new VerifyPhotoLog(dir, meta);
                log.Save();
                return log;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[인증 사진] 남기지 못함: " + e.Message);
                return null;
            }
        }

        // 서버 답(사진 판정, 값 확인 뒤 판정 모두). 읽은 값은 console
        public void Answer(VerificationResult r, DateTime nowUtc)
        {
            if (r == null) return;
            Add(new JObject
            {
                ["time_utc"] = Iso(nowUtc),
                ["status"] = r.Status,
                ["reason"] = r.Reason,
                ["console"] = StatsJson(r.Console),
                ["attempt_id"] = r.AttemptId,
            });
        }

        // 보내기 실패(네트워크·503 등). 사진은 남았지만 서버 판정이 없음
        public void Failed(string error, DateTime nowUtc)
        {
            Add(new JObject
            {
                ["time_utc"] = Iso(nowUtc),
                ["error"] = error,
            });
        }

        // E-05b 맞아요(true)·틀렸어요(false)
        public void Confirm(bool correct)
        {
            meta["confirmed"] = correct;
            Save();
        }

        void Add(JObject answer)
        {
            ((JArray)meta["answers"]).Add(answer);
            Save();
        }

        void Save()
        {
            try
            {
                File.WriteAllText(Path.Combine(Dir, "meta.json"), meta.ToString(Formatting.Indented));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[인증 사진] meta.json을 못 씀: " + e.Message);
            }
        }

        static string Iso(DateTime utc) => utc.ToString("o", CultureInfo.InvariantCulture);

        static JToken StatsJson(RunStatsBody s) =>
            s == null ? JValue.CreateNull() : new JObject { ["distance_m"] = s.DistanceM, ["duration_s"] = s.DurationS };
    }
}
