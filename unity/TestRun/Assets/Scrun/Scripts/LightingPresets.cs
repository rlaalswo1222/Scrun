using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Scrun
{
    // 시간대 프리셋(기본 = 맑은 오후, 선택 = 노을). 하늘·해·주변광·안개와 셰이더 전역 색(물 반사, 손그림풍 주변광, 해 방향 산란 안개)을 한 번에 바꾼다.
    // 반사 프로브를 쓰지 않으므로 프리셋을 바꿔도 다시 구울 필요가 없다(모바일에 가벼움). 개발용: T 키로 전환.
    [ExecuteAlways]
    public class LightingPresets : MonoBehaviour
    {
        [Serializable]
        public class Preset
        {
            public string name;
            public Material skybox;
            public Vector3 sunEuler;
            public Color sunColor = Color.white;
            public float sunIntensity = 1.4f;
            public Color ambientSky, ambientEquator, ambientGround;
            public Color fogColor;
            public float fogDensity = 0.0005f;          // Unity 기본 안개(URP Lit 재질용)
            public Color fogSunColor = Color.white;     // 해 쪽 안개 색(Scrun 셰이더)
            public float fogDistance = 0.0006f;         // Scrun 셰이더 안개: 거리 밀도(1/m)
            public float fogHeightFalloff = 0.012f;     // 높이 감쇠(1/m)
            public float fogBase = 0f;                  // 감쇠 기준 높이
            public float fogMax = 0.95f;
            public Color skyHorizon, skyZenith;   // 물 반사용
        }

        [SerializeField] Light sun;
        [SerializeField] Preset[] presets = Array.Empty<Preset>();
        [SerializeField] int current;

        static readonly int AmbientSky = Shader.PropertyToID("_ScrunAmbientSky");
        static readonly int AmbientGround = Shader.PropertyToID("_ScrunAmbientGround");
        static readonly int SkyHorizon = Shader.PropertyToID("_ScrunSkyHorizon");
        static readonly int SkyZenith = Shader.PropertyToID("_ScrunSkyZenith");
        static readonly int FogColor = Shader.PropertyToID("_ScrunFogColor");
        static readonly int FogSunColor = Shader.PropertyToID("_ScrunFogSunColor");
        static readonly int FogParams = Shader.PropertyToID("_ScrunFogParams");
        static readonly int FogSunDir = Shader.PropertyToID("_ScrunFogSunDir");

        public int Current => current;
        public string CurrentName => presets.Length > 0 ? presets[current].name : "";

        public void Set(Light sunLight, Preset[] list, int index)
        {
            sun = sunLight;
            presets = list;
            Apply(index);
        }

        void OnEnable() => Apply(current);

        void Update()
        {
            if (!Application.isPlaying) return;
            var kb = Keyboard.current;
            if (kb != null && kb.tKey.wasPressedThisFrame && presets.Length > 1) Apply((current + 1) % presets.Length);
        }

        public void Apply(int index)
        {
            if (presets == null || presets.Length == 0) return;
            current = Mathf.Clamp(index, 0, presets.Length - 1);
            var p = presets[current];
            if (p.skybox) RenderSettings.skybox = p.skybox;
            if (sun)
            {
                sun.transform.rotation = Quaternion.Euler(p.sunEuler);
                sun.color = p.sunColor;
                sun.intensity = p.sunIntensity;
                RenderSettings.sun = sun;
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = p.ambientSky;
            RenderSettings.ambientEquatorColor = p.ambientEquator;
            RenderSettings.ambientGroundColor = p.ambientGround;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = p.fogColor;
            RenderSettings.fogDensity = p.fogDensity;
            Shader.SetGlobalColor(AmbientSky, p.ambientSky);
            Shader.SetGlobalColor(AmbientGround, p.ambientGround);
            Shader.SetGlobalColor(SkyHorizon, p.skyHorizon);
            Shader.SetGlobalColor(SkyZenith, p.skyZenith);
            Shader.SetGlobalColor(FogColor, p.fogColor);
            Shader.SetGlobalColor(FogSunColor, p.fogSunColor);
            Shader.SetGlobalVector(FogParams, new Vector4(p.fogDistance, p.fogHeightFalloff, p.fogBase, p.fogMax));
            var toSun = -(Quaternion.Euler(p.sunEuler) * Vector3.forward);
            Shader.SetGlobalVector(FogSunDir, toSun);
        }
    }
}
