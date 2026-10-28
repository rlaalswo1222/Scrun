// 손그림풍 물(바다·호수·웅덩이). 모바일용: 깊이 텍스처 없이 "구운 지형 높이 지도"로 수심을 구한다.
//  - 수심에 따라 얕은 청록 → 깊은 파랑, 물가에 흔들리는 거품 띠
//  - 노멀 텍스처 두 장을 서로 다른 방향으로 흘려 잔물결, 하늘색 반사(프레넬), 해 반짝임
//  - 하늘색은 조명 프리셋이 전역 값(_ScrunSkyHorizon/_ScrunSkyZenith)으로 넣어 줌 → 반사 프로브 불필요
Shader "Scrun/Water"
{
    Properties
    {
        _ShallowColor("Shallow", Color) = (0.25,0.75,0.78,1)
        _DeepColor("Deep", Color) = (0.05,0.28,0.45,1)
        _DepthRange("Depth Range (m)", Float) = 6
        _FoamColor("Foam", Color) = (0.95,0.98,1,1)
        _FoamDepth("Foam Depth (m)", Float) = 0.9
        _NormalMap("Wave Normal", 2D) = "bump" {}
        _WaveScale("Wave Tiling (1/m)", Float) = 0.05
        _WaveSpeed("Wave Speed", Float) = 0.03
        _WaveStrength("Wave Strength", Range(0,2)) = 0.6
        _Specular("Sun Glint", Range(0,4)) = 1.4
        _Reflect("Sky Reflection", Range(0,1)) = 0.6
        _HeightMap("Terrain Height", 2D) = "black" {}
        _HeightRect("Height Rect (xMin, zMin, 1/w, 1/h)", Vector) = (0,0,0,0)
        _HeightRange("Height Range (min, max)", Vector) = (-50,50,0,0)
        _WaterLevel("Water Level", Float) = 0
        _MaxDepth("Max Depth (clip deeper)", Float) = 100000
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+10" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "ScrunCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor, _DeepColor, _FoamColor;
                float _DepthRange, _FoamDepth, _WaveScale, _WaveSpeed, _WaveStrength, _Specular, _Reflect;
                float4 _NormalMap_ST;
                float4 _HeightRect, _HeightRange;
                float _WaterLevel;
                float _MaxDepth;
            CBUFFER_END
            float4 _ScrunSkyHorizon;
            float4 _ScrunSkyZenith;

            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            TEXTURE2D(_HeightMap); SAMPLER(sampler_HeightMap);

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            float WaterDepth(float2 xz)
            {
                float2 uv = (xz - _HeightRect.xy) * _HeightRect.zw;
                if (_HeightRect.z <= 0 || any(uv < 0) || any(uv > 1)) return 1000;   // 지도 밖 = 먼 바다
                float h = lerp(_HeightRange.x, _HeightRange.y, SAMPLE_TEXTURE2D_LOD(_HeightMap, sampler_HeightMap, uv, 0).r);
                return _WaterLevel - h;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 xz = i.positionWS.xz;
                float t = _Time.y * _WaveSpeed;
                half3 n1 = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, xz * _WaveScale + float2(t, t * 0.6)));
                half3 n2 = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, xz * _WaveScale * 1.9 + float2(-t * 0.7, t * 1.1)));
                half3 nT = normalize(half3((n1.xy + n2.xy) * _WaveStrength, 1));
                float3 n = normalize(float3(nT.x, nT.z, nT.y));

                float depth = WaterDepth(xz);
                clip(_MaxDepth - depth);   // 호수 수면이 절벽 밖(바다 위)으로 삐져나온 부분은 그리지 않음
                half3 water = lerp(_ShallowColor.rgb, _DeepColor.rgb, saturate(depth / _DepthRange));

                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                float fres = pow(1 - saturate(dot(n, v)), 5);
                float3 r = reflect(-v, n);
                // 손그림풍: 반사는 파란 하늘 쪽으로, 양은 절반까지만 → 물 색이 살아 있음
                half3 sky = lerp(_ScrunSkyHorizon.rgb, _ScrunSkyZenith.rgb, saturate(r.y * 3 + 0.45));
                half3 col = lerp(water, sky, min(0.45, fres * _Reflect + 0.05));

                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half diff = 0.75 + 0.25 * saturate(dot(n, light.direction));
                col *= lerp(0.8, 1, light.shadowAttenuation) * diff;
                float3 hv = normalize(light.direction + v);
                half spec = pow(saturate(dot(n, hv)), 180) * _Specular * light.shadowAttenuation;
                col += light.color * spec;

                // 물가 거품: 수심이 얕은 띠 + 시간에 따라 움직이는 무늬로 끊김
                half foamNoise = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, xz * _WaveScale * 3 + float2(t * 2, -t)).r;
                half foam = saturate(1 - depth / _FoamDepth) * step(0.42 + 0.35 * sin(_Time.y * 1.3 + depth * 6), foamNoise + 0.25);
                col = lerp(col, _FoamColor.rgb, foam * 0.85);

                // 안개는 픽셀마다(수면은 정점이 4개뿐이라 정점 안개는 수십 km를 뭉개 버림)
                col = ScrunFog(col, i.positionWS, ComputeFogFactor(TransformWorldToHClip(i.positionWS).z));
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
