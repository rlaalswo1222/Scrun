// 행성 고리: 중심에서 거리로 가는 줄무늬(여러 겹의 얇은 띠와 틈). 반투명, 양면.
// _RIn/_ROut은 고리 메시의 안·바깥 반지름(물체 공간, 빌더가 넣음).
Shader "Scrun/PlanetRing"
{
    Properties
    {
        _ColA("Color A", Color) = (0.85,0.8,0.72,1)
        _ColB("Color B", Color) = (0.6,0.7,0.85,1)
        _Alpha("Alpha", Range(0,1)) = 0.55
        _RIn("Inner Radius", Float) = 1
        _ROut("Outer Radius", Float) = 2
        _Bands("Band Count", Float) = 60
        _Brightness("Brightness", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _ColA, _ColB; float _Alpha, _RIn, _ROut, _Bands, _Brightness;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float r : TEXCOORD0; };
            V vert(A v) { V o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.r = length(v.positionOS.xyz); return o; }
            float h1(float x) { return frac(sin(x * 127.1) * 43758.5453); }
            float n1(float x) { float i = floor(x), f = frac(x); f = f * f * (3 - 2 * f); return lerp(h1(i), h1(i + 1), f); }
            half4 frag(V i) : SV_Target
            {
                float t = saturate((i.r - _RIn) / max(_ROut - _RIn, 1e-3));
                float b = n1(t * _Bands) * 0.55 + n1(t * _Bands * 3.7 + 9) * 0.3 + n1(t * _Bands * 0.25 + 3) * 0.15;
                float gaps = smoothstep(0.02, 0.0, abs(t - 0.62)) + smoothstep(0.015, 0.0, abs(t - 0.31));
                float a = _Alpha * smoothstep(0.0, 0.06, t) * smoothstep(1.0, 0.9, t) * (0.25 + 0.75 * b) * (1 - saturate(gaps));
                float3 c = lerp(_ColA.rgb, _ColB.rgb, n1(t * 9 + 2)) * _Brightness;
                return half4(c, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
