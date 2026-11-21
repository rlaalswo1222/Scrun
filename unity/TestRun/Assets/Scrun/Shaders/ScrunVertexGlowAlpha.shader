// 우주 루프: 반투명 발광(오로라·은하수·혜성 꼬리·행성 고리). 정점 색 × 정점 알파. 더하기 또는 알파 섞기.
Shader "Scrun/VertexGlowAlpha"
{
    Properties
    {
        [HDR] _Color("Color", Color) = (1,1,1,1)
        _Intensity("Intensity", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend [_SrcBlend] [_DstBlend]
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
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Intensity;
                float _SrcBlend;
                float _DstBlend;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float4 color : COLOR; };
            struct V { float4 positionCS : SV_POSITION; float4 color : TEXCOORD0; };
            V vert(A v) { V o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.color = v.color; return o; }
            half4 frag(V i) : SV_Target
            {
                half3 c = i.color.rgb * _Color.rgb * _Intensity;
                half a = i.color.a * _Color.a;
                // 더하기(One One)일 때는 알파를 색에 곱해서 넣는다
                if (_DstBlend == 1) c *= a;
                return half4(c, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
