using System;
using System.IO;
using UnityEngine;

namespace Scrun.App
{
    // 에디터용 가짜 계기판 카메라(계획 0033). 아이폰의 E-03 화면 대신 잠깐 기다렸다가
    // Fake Native 창에서 고른 결과(찍음·닫음·시간 초과·카메라 없음)를 준다.
    // 카메라 권한은 가짜 권한(FakeNative)을 그대로 따른다: 안 물어봤으면 먼저 묻고, 거부·제한이면 NoPermission.
    // 찍음이면 노출만 다른 예시 사진(어두운 바탕에 빨간 LED 숫자 막대)을 노출 개수만큼 임시 폴더에 쓴다.
    public sealed class FakeCamera : ICamera
    {
        public static readonly FakeCamera Instance = new FakeCamera(FakeNative.Instance);

        readonly IPermissions permissions;
        readonly FakeNativeConfig ownConfig;
        readonly string dir;
        bool busy;

        FakeNativeConfig Cfg => ownConfig ?? FakeNative.Config;

        public FakeCamera(IPermissions permissions, FakeNativeConfig config = null, string dir = null)
        {
            this.permissions = permissions;
            ownConfig = config;
            this.dir = dir ?? Path.Combine(Application.temporaryCachePath, "scrun-camera");
        }

        public string Folder => dir;

        // Play 도중 멈추면 기다리던 촬영이 끝나지 않은 채 남는다(Play 때 도메인 리로드를 꺼 둠) → 다음 Play에서 풂
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => Instance.busy = false;

        public async Awaitable<CameraShot> ShootAsync(CameraShootOptions options = null)
        {
            if (busy) throw new NativeException("카메라 화면이 이미 떠 있습니다.");
            busy = true;
            try
            {
                options ??= new CameraShootOptions();
                var status = permissions.GetStatus(PermissionKind.Camera);
                if (status == PermissionStatus.NotDetermined) status = await permissions.RequestAsync(PermissionKind.Camera);
                if (status != PermissionStatus.Granted) return new CameraShot { Outcome = CameraOutcome.NoPermission };

                float end = Time.realtimeSinceStartup + Cfg.cameraDelaySec; // 화면을 보고 셔터를 누르기까지
                while (Time.realtimeSinceStartup < end) await Awaitable.NextFrameAsync();

                var shot = new CameraShot { Outcome = Cfg.cameraResult };
                if (shot.Outcome != CameraOutcome.Taken) return shot;

                Directory.CreateDirectory(dir);
                var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
                var biases = options.ExposureBiases is { Length: > 0 } b ? b : new[] { 0f };
                for (int i = 0; i < biases.Length; i++)
                {
                    var path = Path.Combine(dir, $"{stamp}-{i}.jpg");
                    File.WriteAllBytes(path, SamplePhoto(biases[i]));
                    shot.Photos.Add(new CameraPhoto { Path = path, ExposureBias = biases[i] });
                }
                return shot;
            }
            finally
            {
                busy = false;
            }
        }

        public void Discard(CameraShot shot)
        {
            if (shot == null) return;
            var root = Path.GetFullPath(dir);
            foreach (var p in shot.Photos)
            {
                if (string.IsNullOrEmpty(p?.Path)) continue;
                var full = Path.GetFullPath(p.Path);
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // 카메라 폴더 밖은 안 지움
                try { File.Delete(full); }
                catch (IOException e) { Debug.LogWarning("[FakeCamera] 사진을 못 지움: " + e.Message); }
            }
        }

        // 노출 흉내: 0 EV는 LED가 타서 하얗게 번지고 꺼진 획이 비침, 어두울수록 빨간 획만 남다가 흐려짐(설명용, 실제 사진 아님)
        static byte[] SamplePhoto(float bias)
        {
            const int w = 320, h = 180;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            try
            {
                float gain = Mathf.Pow(2f, bias);                      // EV → 밝기 배율
                var bg = new Color(0.16f, 0.18f, 0.22f) * gain;        // 계기판 바탕·헬스장
                var lit = Color.Lerp(new Color(1f, 0.25f, 0.2f), Color.white, Mathf.Clamp01(gain - 0.4f)) * Mathf.Min(1f, 0.35f + gain);
                var off = new Color(0.5f, 0.12f, 0.1f) * Mathf.Clamp01(gain - 0.3f); // 꺼진 "888" 획
                var px = new Color[w * h];
                for (int i = 0; i < px.Length; i++) px[i] = bg;
                for (int d = 0; d < 3; d++)
                {
                    int x0 = 50 + d * 80;
                    Bar(px, w, x0, 40, 50, 10, off); // 위 가로획(꺼짐)
                    Bar(px, w, x0 + 40, 40, 10, 100, lit); // 오른쪽 세로획(켜짐) → "1"처럼 보임
                    Bar(px, w, x0, 85, 50, 10, d == 1 ? lit : off);
                }
                tex.SetPixels(px);
                tex.Apply(false);
                return tex.EncodeToJPG(80);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        static void Bar(Color[] px, int w, int x, int y, int bw, int bh, Color c)
        {
            for (int j = y; j < y + bh; j++)
                for (int i = x; i < x + bw; i++)
                    px[j * w + i] = c;
        }
    }
}
