using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace Scrun.App
{
    // 카메라(E-03)가 남긴 브래킷 사진 → 서버에 올릴 JPEG(계획 0038).
    // 원본은 4032×3024 세 장이 6.6–8.0 MB(0033 실측)라 서버 상한(한 장 4 MB)·올리는 시간을 위해 긴 변 2048로 줄인다.
    // 아이폰은 Swift가 픽셀을 세우고(EXIF 방향 반영) 이미 2048로 줄여 쓴다(CameraShootOptions.MaxSide) → 여기선 헤더로 크기만 보고 그대로 보냄.
    // 큰 사진(Swift가 세우기에 실패해 원본을 쓴 경우 등)만 여기서 줄인다(메인 스레드 디코딩, EXIF 방향은 잃음).
    // 가이드 영역만 자르는 것은 Swift가 가이드 위치를 알려 주면(민재 0033 후속) — 지금은 전체를 줄임
    public static class VerifyPhotos
    {
        public const int MaxSide = 2048;
        public const int JpegQuality = 85;

        public static async Task<List<MultipartBody.FilePart>> PrepareAsync(CameraShot shot, RunStatsBody sensor, FakeOcrResult? fakeOcr)
        {
            var photos = SendOrder(shot.Photos);
            var paths = new List<string>();
            foreach (var p in photos) paths.Add(p.Path);
            // 파일 읽기는 스레드 풀에서(세 장 합쳐 수 MB). await 뒤는 Unity 메인 스레드로 돌아옴
            var files = await Task.Run(() => paths.ConvertAll(p => File.ReadAllBytes(p)));
            var parts = new List<MultipartBody.FilePart>();
            for (int i = 0; i < files.Count; i++)
                parts.Add(new MultipartBody.FilePart(FileName(i, photos[i].ExposureBias, sensor, fakeOcr), Shrink(files[i])));
            return parts;
        }

        // 보내는 순서: 0 EV(자동 노출)에 가까운 것부터. 서버는 같은 값으로 읽힌 사진이 두 장이면 나머지를 안 읽음(계획 0055).
        // EV가 같으면 찍은 순서(안정 정렬)
        public static List<CameraPhoto> SendOrder(IReadOnlyList<CameraPhoto> shot)
        {
            var idx = new List<int>();
            for (int i = 0; i < shot.Count; i++) idx.Add(i);
            idx.Sort((a, b) =>
            {
                int c = System.Math.Abs(shot[a].ExposureBias).CompareTo(System.Math.Abs(shot[b].ExposureBias));
                return c != 0 ? c : a.CompareTo(b);
            });
            return idx.ConvertAll(i => shot[i]);
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

        // 긴 변이 MaxSide보다 크면 줄여 다시 JPEG. 작으면 그대로(다시 압축해 화질을 잃지 않게, 디코딩도 안 함)
        public static byte[] Shrink(byte[] jpeg)
        {
            var size = JpegSize(jpeg);
            if (size.HasValue && Mathf.Max(size.Value.width, size.Value.height) <= MaxSide) return jpeg;
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
                    Free(dst);
                }
            }
            finally
            {
                Free(src);
            }
        }

        // EditMode 테스트에서는 Destroy를 못 씀
        static void Free(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        // JPEG 헤더(SOF 마커)에서 가로·세로만 읽는다. JPEG가 아니거나 못 찾으면 null
        public static (int width, int height)? JpegSize(byte[] b)
        {
            if (b == null || b.Length < 4 || b[0] != 0xFF || b[1] != 0xD8) return null;
            int i = 2;
            while (i + 3 < b.Length)
            {
                if (b[i] != 0xFF) return null;
                byte marker = b[i + 1];
                if (marker == 0xFF) { i++; continue; }                        // 채움 바이트
                if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7)) { i += 2; continue; } // 길이 없는 마커
                if (marker == 0xD9 || marker == 0xDA) return null;            // 끝·영상 데이터 시작 전에 SOF가 없음
                int len = (b[i + 2] << 8) | b[i + 3];
                if (len < 2) return null;
                bool sof = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
                if (sof)
                {
                    if (i + 8 >= b.Length) return null;
                    int h = (b[i + 5] << 8) | b[i + 6], w = (b[i + 7] << 8) | b[i + 8];
                    if (w <= 0 || h <= 0) return null;
                    return (w, h);
                }
                i += 2 + len;
            }
            return null;
        }
    }
}
