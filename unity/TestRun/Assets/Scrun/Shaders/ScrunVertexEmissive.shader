// 우주 루프: 스스로 빛나는 물체(LED·창문 빛·결정·행성). 정점 색 × 색 × 세기(HDR → 블룸).
// 그림자 받지 않음, 안개는 선택(_FogAmount). 모바일용으로 가볍게.
Shader "Scrun/VertexEmissive"
{
    Properties
    {
        [HDR] _Color("Color", Color) = (1,1,1,1)
        _Intensity("Intensity", Float) = 1
        _FogAmount("Fog Amount", Range(0,1)) = 1
        _Lit("Mix Lit Shading", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "ScrunCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Intensity;
                float _FogAmount;
                float _Lit;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float4 color : TEXCOORD2; float fog : TEXCOORD3; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 c = i.color.rgb * _Color.rgb;
                if (_Lit > 0)
                {
                    Light l = GetMainLight();
                    half ndl = saturate(dot(normalize(i.normalWS), l.direction) * 0.5 + 0.5);
                    c *= lerp(1, ndl * 1.4, _Lit);
                }
                c *= _Intensity;
                if (_FogAmount > 0)
                {
                    half3 f = ScrunFog(c, i.positionWS, i.fog);
                    c = lerp(c, f, _FogAmount);
                }
                return half4(c, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            float4 vert(Attributes v) : SV_POSITION { return TransformObjectToHClip(v.positionOS.xyz); }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
