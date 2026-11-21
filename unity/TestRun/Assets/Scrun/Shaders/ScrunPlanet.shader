// 우주 루프 행성·위성: 정점 색(또는 두 색 띠) + 잡음 띠 디테일 + 해 방향 명암(부드러운 낮밤 경계) + 대기 테두리 빛.
// 해 방향은 SpaceLoopAtmosphere가 넣는 전역 _ScrunFogSunDir. 그림자·안개 없음(아주 먼 하늘 물체).
Shader "Scrun/Planet"
{
    Properties
    {
        _UseTint("Use Tint Bands (0 = vertex color)", Range(0,1)) = 0
        _TintA("Band Color A", Color) = (0.2,0.4,0.7,1)
        _TintB("Band Color B", Color) = (0.9,0.93,0.97,1)
        _BandFreq("Band Stretch", Float) = 6
        _Detail("Band Detail", Range(0,1)) = 0.5
        _Turb("Band Turbulence", Range(0,2)) = 0.6
        _Night("Night Side", Range(0,0.3)) = 0.05
        _Brightness("Day Brightness", Float) = 1.2
        [HDR] _AtmoColor("Atmosphere Rim", Color) = (0.6,0.8,1,1)
        _AtmoStrength("Atmosphere Strength", Range(0,4)) = 1.2
        _FrontLight("Light From Viewer Side", Range(0,3)) = 1
        _Haze("Sky Haze", Range(0,1)) = 0
        _HazeColor("Haze Color", Color) = (0.8,0.85,0.95,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off   // 호수 반사 카메라(거울 변환)에서도 보이게
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "ScrunCommon.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _TintA, _TintB, _AtmoColor, _HazeColor;
                float _UseTint, _BandFreq, _Detail, _Turb, _Night, _Brightness, _AtmoStrength, _Haze, _FrontLight;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
            struct V { float4 positionCS : SV_POSITION; float3 p : TEXCOORD0; float3 n : TEXCOORD1; float3 w : TEXCOORD2; float4 c : COLOR; };
            V vert(A v)
            {
                V o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.p = normalize(v.positionOS.xyz); o.n = TransformObjectToWorldNormal(normalize(v.positionOS.xyz));
                o.w = TransformObjectToWorld(v.positionOS.xyz); o.c = v.color; return o;
            }
            float h3(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float vn(float3 p)
            {
                float3 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(lerp(h3(i), h3(i + float3(1,0,0)), f.x), lerp(h3(i + float3(0,1,0)), h3(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(h3(i + float3(0,0,1)), h3(i + float3(1,0,1)), f.x), lerp(h3(i + float3(0,1,1)), h3(i + float3(1,1,1)), f.x), f.y), f.z);
            }
            float fbm(float3 p) { float s = 0, a = 0.5; for (int k = 0; k < 4; k++) { s += a * vn(p); p = p * 2.07 + 5.3; a *= 0.5; } return s; }

            half4 frag(V i) : SV_Target
            {
                float3 p = i.p;
                // 위도 띠 + 소용돌이(잡음으로 위도를 흔듦)
                float warp = (fbm(p * 3.0 + 1.7) - 0.5) * _Turb;
                float lat = p.y + warp * 0.35;
                float band = fbm(float3(lat * _BandFreq, lat * _BandFreq * 0.5 + 3.1, 0.7)) ;
                float fine = fbm(p * float3(2.5, 18.0, 2.5) + warp);
                float3 alb = lerp(i.c.rgb, lerp(_TintA.rgb, _TintB.rgb, saturate(band * 1.6 - 0.3)), _UseTint);
                alb *= lerp(1, 0.75 + 0.5 * fine, _Detail);

                float3 N = normalize(i.n);
                float3 Vd = normalize(_WorldSpaceCameraPos - i.w);
                // 해가 행성 뒤에 있으면 거의 검게 보이므로, 보는 쪽에서 빛을 조금 섞어 초승달이 아닌 반달·보름 모양으로
                float3 L = normalize(normalize(_ScrunFogSunDir.xyz + float3(0, 1e-4, 0)) + Vd * _FrontLight);
                float ndl = dot(N, L);
                float day = smoothstep(-0.12, 0.55, ndl);
                float3 col = alb * (_Night + day * _Brightness);
                // 대기 테두리: 가장자리에서 밝고, 낮 쪽이 더 밝음
                float fres = pow(1 - saturate(dot(N, Vd)), 3);
                col += _AtmoColor.rgb * fres * _AtmoStrength * (0.15 + 0.85 * smoothstep(-0.35, 0.4, ndl));
                col = lerp(col, _HazeColor.rgb, _Haze);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
