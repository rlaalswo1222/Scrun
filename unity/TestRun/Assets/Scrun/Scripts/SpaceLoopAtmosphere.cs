using UnityEngine;
using UnityEngine.Rendering;

namespace Scrun
{
    // 우주 루프(구간 600m x 5)의 하늘·해·주변광·안개. 내 러너 거리(m)로 구간 가중치를 구하고 경계 앞뒤 50m에서 섞는다.
    // 값은 Blender 목업(scripts/sl_look.py PRESET)과 같다. Scrun 셰이더 전역 안개(_ScrunFog*)와 하늘 재질을 함께 바꾼다.
    [ExecuteAlways]
    public class SpaceLoopAtmosphere : MonoBehaviour
    {
        [System.Serializable]
        public class Zone
        {
            public string name;
            public Color top, horizon, below;
            [Tooltip("진행 방향 기준 해 방위각(도, 오른쪽 -)")] public float sunAzimuth;
            public float sunElevation = 10f;
            public Color sun = Color.white;
            public float sunIntensity = 1.5f;
            public Color ambientSky, ambientGround;
            public Color fog, fogSun;
            public float fogDensity = 0.002f;
            public float stars;
            public float sunDisc = 1f;
            public float sunGlow = 1.2f;   // 하늘의 해 번짐(공기가 없는 달은 작게)
            [Header("하늘 디테일")]
            public float nebula;                       // 성운·은하수 세기
            public Color nebulaA = new Color(0.25f, 0.35f, 0.8f), nebulaB = new Color(0.6f, 0.4f, 0.9f);
            public Vector3 nebulaAxis = new Vector3(0.3f, 0.6f, 0.74f);   // 띠가 감는 대원의 법선(월드)
            public float nebulaWidth = 0.28f;
            public float cirrus;                       // 새털구름
            public Color cirrusColor = Color.white;
            public Color horizonGlow = Color.black;    // 지평선 빛띠(더하기)
            public float glowWidth = 0.08f;
            public float horizonSharp = 0.55f;         // 지평선에서 위 색까지 높이(작을수록 위 색이 낮게 내려옴)
        }

        [SerializeField] RunnerController runner;
        [SerializeField] Light sunLight;
        [SerializeField] Material skyMaterial;
        [SerializeField] float zoneLength = 600f;
        [SerializeField] float blend = 50f;
        [SerializeField] float previewDistance = 900f;   // 러너가 없을 때(에디터) 기준 거리
        public Zone[] zones = DefaultZones();

        static readonly int FogColor = Shader.PropertyToID("_ScrunFogColor");
        static readonly int FogSunColor = Shader.PropertyToID("_ScrunFogSunColor");
        static readonly int FogParams = Shader.PropertyToID("_ScrunFogParams");
        static readonly int FogSunDir = Shader.PropertyToID("_ScrunFogSunDir");
        static readonly int AmbientSky = Shader.PropertyToID("_ScrunAmbientSky");
        static readonly int AmbientGround = Shader.PropertyToID("_ScrunAmbientGround");
        static readonly int SkyHorizon = Shader.PropertyToID("_ScrunSkyHorizon");
        static readonly int SkyZenith = Shader.PropertyToID("_ScrunSkyZenith");

        public void SetRunner(RunnerController r) => runner = r;
        public void ResetZones() { zones = DefaultZones(); Apply(); }
        public void SetPreviewDistance(float d) { previewDistance = d; Apply(); }

        void LateUpdate() => Apply();

        float Weight(int i, float s, float lap)
        {
            float a = i * zoneLength, b = a + zoneLength, best = 0;
            foreach (float off in new[] { -lap, 0f, lap })
            {
                float x = s + off;
                float win = Mathf.SmoothStep(0, 1, Mathf.Clamp01((x - (a - blend)) / (2 * blend)));
                float wout = 1 - Mathf.SmoothStep(0, 1, Mathf.Clamp01((x - (b - blend)) / (2 * blend)));
                best = Mathf.Max(best, Mathf.Min(win, wout));
            }
            return best;
        }

        public void Apply()
        {
            if (zones == null || zones.Length == 0) return;
            var track = runner ? runner.Track : null;
            float lap = zones.Length * zoneLength;
            // 편집 모드에서는 러너가 아직 움직이지 않으므로(거리 0) 미리보기 거리를 쓴다
            float s = runner && Application.isPlaying ? runner.TrackDistance : previewDistance;
            s = Mathf.Repeat(s, lap);
            var w = new float[zones.Length];
            float tot = 0;
            for (int i = 0; i < zones.Length; i++) { w[i] = Weight(i, s, lap); tot += w[i]; }
            for (int i = 0; i < zones.Length; i++) w[i] /= Mathf.Max(tot, 1e-4f);

            Color top = default, hor = default, below = default, sun = default, aSky = default, aGround = default, fog = default, fogSun = default;
            float az = 0, el = 0, si = 0, fd = 0, stars = 0, disc = 0, glow = 0;
            float neb = 0, nebW = 0, cir = 0, gw = 0, hs = 0;
            Color nA = default, nB = default, cirC = default, hg = default;
            Vector3 nAx = Vector3.zero;
            for (int i = 0; i < zones.Length; i++)
            {
                var z = zones[i]; float k = w[i];
                if (k <= 0) continue;
                top += z.top * k; hor += z.horizon * k; below += z.below * k; sun += z.sun * k;
                aSky += z.ambientSky * k; aGround += z.ambientGround * k; fog += z.fog * k; fogSun += z.fogSun * k;
                az += z.sunAzimuth * k; el += z.sunElevation * k; si += z.sunIntensity * k; fd += z.fogDensity * k;
                stars += z.stars * k; disc += z.sunDisc * k; glow += z.sunGlow * k;
                neb += z.nebula * k; nebW += z.nebulaWidth * k; cir += z.cirrus * k; gw += z.glowWidth * k; hs += z.horizonSharp * k;
                nA += z.nebulaA * k; nB += z.nebulaB * k; cirC += z.cirrusColor * k; hg += z.horizonGlow * k;
                nAx += z.nebulaAxis.normalized * k;
            }

            // 진행 방향 기준 해 방향
            Vector3 fwd = track ? track.GetForward(s) : Vector3.forward;
            Vector3 dir = Quaternion.AngleAxis(-az, Vector3.up) * fwd;   // 오른쪽(-) 규칙은 Blender 목업과 같음
            dir = Quaternion.AngleAxis(-el, Vector3.Cross(Vector3.up, dir).normalized) * dir;
            dir = Vector3.Slerp(dir, Vector3.up, 0f).normalized;
            Vector3 toSun = new Vector3(dir.x, Mathf.Sin(el * Mathf.Deg2Rad), dir.z).normalized;
            if (sunLight)
            {
                sunLight.transform.rotation = Quaternion.LookRotation(-toSun);
                sunLight.color = sun;
                sunLight.intensity = si;
            }
            if (skyMaterial)
            {
                skyMaterial.SetColor("_TopColor", top);
                skyMaterial.SetColor("_HorizonColor", hor);
                skyMaterial.SetColor("_BelowColor", below);
                skyMaterial.SetColor("_SunColor", sun);
                skyMaterial.SetVector("_SunDir", toSun);
                skyMaterial.SetFloat("_Stars", stars);
                skyMaterial.SetFloat("_SunDisc", disc);
                skyMaterial.SetFloat("_SunGlow", glow);
                skyMaterial.SetFloat("_Nebula", neb);
                skyMaterial.SetColor("_NebulaA", nA);
                skyMaterial.SetColor("_NebulaB", nB);
                skyMaterial.SetVector("_NebulaAxis", nAx.sqrMagnitude > 1e-6f ? nAx.normalized : Vector3.up);
                skyMaterial.SetFloat("_NebulaWidth", Mathf.Max(nebW, 0.05f));
                skyMaterial.SetFloat("_Cirrus", cir);
                skyMaterial.SetColor("_CirrusColor", cirC);
                skyMaterial.SetColor("_GlowColor", hg);
                skyMaterial.SetFloat("_GlowWidth", Mathf.Max(gw, 0.01f));
                skyMaterial.SetFloat("_HorizonSharp", Mathf.Clamp(hs, 0.05f, 1f));
                RenderSettings.skybox = skyMaterial;
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = aSky;
            RenderSettings.ambientEquatorColor = Color.Lerp(aSky, aGround, 0.5f);
            RenderSettings.ambientGroundColor = aGround;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fog;
            RenderSettings.fogDensity = fd * 0.6f;
            Shader.SetGlobalColor(SkyHorizon, hor);   // 물 반사색(Scrun/Water)
            Shader.SetGlobalColor(SkyZenith, top);
            Shader.SetGlobalColor(AmbientSky, aSky);
            Shader.SetGlobalColor(AmbientGround, aGround);
            Shader.SetGlobalColor(FogColor, fog);
            Shader.SetGlobalColor(FogSunColor, fogSun);
            Shader.SetGlobalVector(FogParams, new Vector4(Mathf.Max(fd, 1e-6f), 0.012f, 0f, 0.92f));
            Shader.SetGlobalVector(FogSunDir, toSun);
        }

        static Color H(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }

        public static Zone[] DefaultZones() => new[]
        {
            // 레퍼런스(ref3): 짙은 파랑 하늘 + 새털구름 + 해 쪽 따뜻한 지평선
            new Zone { name = "구름 위 기지", top = H("#1D58C8"), horizon = H("#E9DCCB"), below = H("#FFF2E2"), sunAzimuth = -35, sunElevation = 22,
                sun = H("#FFF1DC"), sunIntensity = 1.7f, ambientSky = H("#CFDAE8"), ambientGround = H("#B8AFA4"), fog = H("#D3DDEA"), fogSun = H("#FFEED2"), fogDensity = 0.0004f, stars = 0, sunDisc = 1, horizonSharp = 0.22f,
                cirrus = 0.65f, cirrusColor = H("#FFFFFF"), horizonGlow = H("#3A2A18"), glowWidth = 0.05f },
            // 노을 협곡: 높은 먼지 구름 + 주황 지평선 빛띠, 위쪽에 옅은 별
            new Zone { name = "화성 협곡", top = H("#4A3B6A"), horizon = H("#F2C49A"), below = H("#E1A985"), sunAzimuth = -30, sunElevation = 8,
                sun = H("#FFCE9E"), sunIntensity = 1.5f, ambientSky = H("#E2BBA6"), ambientGround = H("#9A5A40"), fog = H("#E7B08C"), fogSun = H("#FFD9A8"), fogDensity = 0.0024f, stars = 0.35f, sunDisc = 1,
                nebula = 0.25f, nebulaA = H("#6A4C9C"), nebulaB = H("#C06A7A"), nebulaAxis = new Vector3(-0.781f, 0.332f, 0.53f), nebulaWidth = 0.22f, horizonSharp = 0.5f,
                cirrus = 0.45f, cirrusColor = H("#F6CDA8"), horizonGlow = H("#5A2A10"), glowWidth = 0.06f },
            // 레퍼런스(ref4): 남색 밤하늘, 청록 지평선 빛, 별·옅은 은하수
            new Zone { name = "밤 숲 호수", top = H("#071634"), horizon = H("#2B6672"), below = H("#0C2026"), sunAzimuth = -35, sunElevation = 34,
                sun = H("#BFD4FF"), sunIntensity = 0.6f, ambientSky = H("#6688AA"), ambientGround = H("#22382C"), fog = H("#1C3F50"), fogSun = H("#5F8FA0"), fogDensity = 0.0013f, stars = 1.1f, sunDisc = 0,
                nebula = 0.8f, nebulaA = H("#2E4E9E"), nebulaB = H("#6A5ED8"), nebulaAxis = new Vector3(-0.789f, 0.418f, -0.45f), nebulaWidth = 0.28f, horizonSharp = 0.35f,
                horizonGlow = H("#1E5A50"), glowWidth = 0.07f },
            // 레퍼런스(ref2): 짙은 남색, 별이 많고 푸른 은하수
            new Zone { name = "얼음 위성", top = H("#030A26"), horizon = H("#1E4C9A"), below = H("#BFD3EA"), sunAzimuth = -30, sunElevation = 20,
                sun = H("#D8E6FF"), sunIntensity = 0.95f, ambientSky = H("#4E6AA0"), ambientGround = H("#6A7FA6"), fog = H("#2E5594"), fogSun = H("#6F98D8"), fogDensity = 0.0016f, stars = 1.4f, sunDisc = 0,
                nebula = 0.7f, nebulaA = H("#2C5BD8"), nebulaB = H("#9A6CF0"), nebulaAxis = new Vector3(-0.656f, -0.331f, -0.679f), nebulaWidth = 0.26f, horizonSharp = 0.3f,
                horizonGlow = H("#18407A"), glowWidth = 0.05f },
            // 공기 없음: 검은 하늘에 선명한 은하수(따뜻한 중심 + 차가운 가장자리)
            new Zone { name = "달 기지", top = H("#000000"), horizon = H("#04060B"), below = H("#0A0A0C"), sunAzimuth = 55, sunElevation = 20,
                sun = H("#FFFFFF"), sunIntensity = 1.9f, ambientSky = H("#5C667C"), ambientGround = H("#3C3C44"), fog = H("#000000"), fogSun = H("#000000"), fogDensity = 0.000001f, stars = 1.8f, sunDisc = 1, sunGlow = 0.12f,
                nebula = 1.0f, nebulaA = H("#A8B0E0"), nebulaB = H("#E0B088"), nebulaAxis = new Vector3(0.857f, 0.336f, -0.39f), nebulaWidth = 0.15f },
        };
    }
}
