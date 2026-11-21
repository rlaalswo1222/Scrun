// 우주 루프 구름 카드: 카메라를 보는 사각형에 미리 렌더한 부피 구름을 입힌다(6방향 조명 방식).
//   _MapA: RGB = 오른쪽·위·앞(카메라 쪽)에서 비춘 밝기, A = 불투명도
//   _MapB: RGB = 왼쪽·아래·뒤에서 비춘 밝기
// 해 방향을 카드 기준 축으로 옮겨 여섯 밝기를 섞으므로, 달리면서 보는 각도가 바뀌어도 해 쪽이 밝고 반대쪽이 어둡다.
// 색은 알파가 곱해진 값(premultiplied). 정점: position = 카드 중심, uv0 = (모서리 xy, 크기 xy), uv1 = (칸 열, 칸 행, 좌우 뒤집기, 0).
// 뒤에서 앞으로 그리는 순서는 SpaceLoopCloudCards가 맞춘다.
Shader "Scrun/CloudCard"
{
    Properties
    {
        _MapA("Light Map A (right, top, front, alpha)", 2D) = "black" {}
        _MapB("Light Map B (left, bottom, back)", 2D) = "black" {}
        _Cells("Atlas Cells (cols, rows)", Vector) = (4, 2, 0, 0)
        _SunBoost("Sun Strength", Range(0, 3)) = 0.8
        _AmbTop("Sky Light From Above", Range(0, 2)) = 0.55
        _AmbBottom("Bounce Light From Below", Range(0, 2)) = 0.35
        _AmbTint("Shade Tint (multiplies sky/bounce light)", Color) = (0.86, 0.93, 1.1, 1)
        _Tint("Tint", Color) = (1,1,1,1)
        _Opacity("Opacity", Range(0, 1)) = 1
        _NearFade("Near Fade (start, end m)", Vector) = (6, 18, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "ScrunCommon.hlsl"
            TEXTURE2D(_MapA); SAMPLER(sampler_MapA);
            TEXTURE2D(_MapB); SAMPLER(sampler_MapB);
            float4 _ScrunAmbientSky, _ScrunAmbientGround;
            CBUFFER_START(UnityPerMaterial)
                float4 _Cells, _Tint, _NearFade, _AmbTint;
                float _SunBoost, _AmbTop, _AmbBottom, _Opacity;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float4 uv0 : TEXCOORD0; float4 uv1 : TEXCOORD1; float4 color : COLOR; };
            struct V
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 lightB : TEXCOORD1;    // 카드 기준 해 방향(오른쪽, 위, 카메라 쪽)
                float4 fog : TEXCOORD2;       // rgb = 더할 안개색, a = 남는 비율
                float4 tint : TEXCOORD3;      // rgb = 색, a = 불투명도
            };
            V vert(A v)
            {
                V o;
                float3 c = TransformObjectToWorld(v.positionOS.xyz);
                float3 toCam = _WorldSpaceCameraPos - c;
                float dist = length(toCam);
                float3 fwd = toCam / max(dist, 1e-3);
                float3 right = normalize(cross(float3(0, 1, 0), fwd));
                float3 up = cross(fwd, right);
                float3 p = c + right * (v.uv0.x * v.uv0.z) + up * (v.uv0.y * v.uv0.w);
                o.positionCS = TransformWorldToHClip(p);
                float flip = v.uv1.z;
                float2 cell = float2((v.uv0.x * flip + 0.5 + v.uv1.x) / _Cells.x, (v.uv0.y + 0.5 + v.uv1.y) / _Cells.y);
                o.uv = cell;
                float3 L = normalize(_MainLightPosition.xyz);
                o.lightB = float3(dot(L, right) * flip, dot(L, up), dot(L, fwd));
                // 안개: 검정과 흰색에 각각 섞어 보면 더해지는 색과 남는 비율이 나온다
                half3 f0 = ScrunFog(half3(0, 0, 0), p, 0);
                half3 f1 = ScrunFog(half3(1, 1, 1), p, 0);
                o.fog = float4(f0, saturate(f1.g - f0.g));
                float nearK = saturate((dist - _NearFade.x) / max(_NearFade.y - _NearFade.x, 1e-3));
                o.tint = float4(v.color.rgb * _Tint.rgb, v.color.a * _Opacity * nearK);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                half4 a = SAMPLE_TEXTURE2D(_MapA, sampler_MapA, i.uv);
                half3 b = SAMPLE_TEXTURE2D(_MapB, sampler_MapB, i.uv).rgb;
                half3 Lb = i.lightB;
                half3 w = Lb * Lb;
                half sunLit = w.x * (Lb.x > 0 ? a.r : b.r) + w.y * (Lb.y > 0 ? a.g : b.g) + w.z * (Lb.z > 0 ? a.b : b.b);
                half3 col = _MainLightColor.rgb * (sunLit * _SunBoost)
                          + (_ScrunAmbientSky.rgb * (a.g * _AmbTop) + _ScrunAmbientGround.rgb * (b.g * _AmbBottom)) * _AmbTint.rgb;
                col *= i.tint.rgb;
                half alpha = a.a;
                col = col * i.fog.a + i.fog.rgb * alpha;
                return half4(col * i.tint.a, alpha * i.tint.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
