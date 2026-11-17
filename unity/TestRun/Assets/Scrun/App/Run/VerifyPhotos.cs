using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Scrun.App
{
    // 카메라(E-03)가 남긴 브래킷 사진 → 서버에 올릴 JPEG(계획 0038).
    // 한 번 찍으면 4032×3024 세 장이 6.6–8.0 MB(0033 실측)라 서버 상한(한 장 4 MB)·올리는 시간을 위해 긴 변 2048로 줄인다.
    // 가이드 영역만 자르는 것은 Swift가 가이드 위치를 알려 주면(민재 0033 후속) — 지금은 전체를 줄임
    public static class VerifyPhotos
    {
        public const int MaxSide = 2048;
        public const int JpegQuality = 85;

        public static List<MultipartBody.FilePart> Prepare(CameraShot shot, RunStatsBody sensor, FakeOcrResult? fakeOcr)
        {
            var parts = new List<MultipartBody.FilePart>();
            for (int i = 0; i < shot.Photos.Count; i++)
            {
                var p = shot.Photos[i];
                parts.Add(new MultipartBody.FilePart(FileName(i, p.ExposureBias, sensor, fakeOcr), Shrink(File.ReadAllBytes(p.Path))));
            }
            return parts;
        }

        // 파일 이름. 에디터에서는 로컬 서버 가짜 OCR(파일 이름으로 값을 정함)이 고른 결과를 내게 붙인다 — 진짜 서버는 이름을 보지 않음
        public static string FileName(int index, float bias, RunStatsBody sensor, FakeOcrResult? fakeOcr)
        {
            string name = "console_" + index + "_ev" + bias.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);
            if (fakeOcr.HasValue && index == 0)
            {
                int m = sensor?.DistanceM ?? 0, s = (sensor?.DurationS ?? 0) + 40; // 계기판은 카운트다운 전 걷기만큼 길게
                name += fakeOcr.Value switch
                {
                    FakeOcrResult.Match => $"_d{m}_t{s}",
                    FakeOcrResult.Mismatch => $"_d{m * 13 / 10}_t{s}",
                    FakeOcrResult.Unsupported => "_unsupported",
                    _ => "_blur",
                };
            }
            return name + ".jpg";
        }

        // 긴 변이 MaxSide보다 크면 줄여 다시 JPEG. 작으면 그대로(다시 압축해 화질을 잃지 않게)
        public static byte[] Shrink(byte[] jpeg)
        {
            var src = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                if (!src.LoadImage(jpeg, false)) return jpeg; // 못 열면 서버가 판단(깨진 사진은 다시 찍기)
                int w = src.width, h = src.height, longSide = Mathf.Max(w, h);
                if (longSide <= MaxSide) return jpeg;
                float k = (float)MaxSide / longSide;
                int nw = Mathf.RoundToInt(w * k), nh = Mathf.RoundToInt(h * k);
                var rt = RenderTexture.GetTemporary(nw, nh, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var prev = RenderTexture.active;
                var dst = new Texture2D(nw, nh, TextureFormat.RGB24, false);
                try
                {
                    Graphics.Blit(src, rt);
                    RenderTexture.active = rt;
                    dst.ReadPixels(new Rect(0, 0, nw, nh), 0, 0);
                    dst.Apply(false);
                    return dst.EncodeToJPG(JpegQuality);
                }
                finally
                {
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(rt);
                    Object.Destroy(dst);
                }
            }
            finally
            {
                Object.Destroy(src);
            }
        }
    }
}
