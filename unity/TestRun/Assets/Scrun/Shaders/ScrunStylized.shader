// 손그림풍 조명(나무·덤불·풀·바위·지형·소품). 모바일용으로 가볍게:
//  - 정점 색 × 텍스처 × 색, 빛 감싸기(wrap)로 그림자 쪽이 검게 죽지 않게, 그림자 쪽은 푸른 기운
//  - 바람: 높이에 비례해 휘어짐(길이 유지) + 잎 떨림(UV1.x = 잎 가중치), GPU 인스턴싱 지원
//  - LOD 전환: 개체마다 거리로 디더 교차(InstancedScatter가 _ScrunLodFade를 넣음). 풀은 디더 대신 작아지며 사라짐
//  - 잎 카드: 알파→커버리지(MSAA)로 테두리가 부드럽고 멀리서 지글거리지 않음
//  - 주 광원 그림자 받기·드리우기, 해 방향 산란 안개(ScrunCommon)
//  - 나무껍질(_BARK_ARRAY): UV1.y = 껍질 종류 + 1인 정점(줄기·가지)은 잎 아틀라스 대신 껍질 텍스처 배열(반복 UV)
//  - 바위(_ROCK_TRIPLANAR): 사진 바위 텍스처를 월드 3면 투영(색 결 + 노멀) + 윗면 이끼. _RockSteep > 0이면 가파른 면에만(지형 절벽)
Shader "Scrun/Stylized"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (1,1,1,1)
        _BaseMap("Base Map", 2D) = "white" {}
        _DetailMap("Detail (x2)", 2D) = "grey" {}
        _DetailScale("Detail Tiling", Float) = 0
        _DetailStrength("Detail Strength", Range(0,1)) = 0
        _DetailMap2("Detail 2 (x2)", 2D) = "grey" {}
        _DetailScale2("Detail 2 Tiling", Float) = 0
        _DetailStrength2("Detail 2 Strength", Range(0,1)) = 0
        _StrataStrength("Rock Strata (by height)", Range(0,1)) = 0
        _StrataScale("Strata Bands per m", Float) = 0.35
        [Toggle(_BARK_ARRAY)] _UseBark("Bark Array", Float) = 0
        _BarkArray("Bark (Array)", 2DArray) = "" {}
        [Toggle(_ROCK_TRIPLANAR)] _UseRock("Rock Triplanar", Float) = 0
        _RockMap("Rock Albedo", 2D) = "grey" {}
        _RockMean("Rock Mean Color", Color) = (0.5,0.5,0.5,1)
        [Normal] _RockNormal("Rock Normal", 2D) = "bump" {}
        _RockScale("Rock Tiling per m", Float) = 0.25
        _RockStrength("Rock Albedo Strength", Range(0,1)) = 1
        _RockNormalStrength("Rock Normal Strength", Range(0,2)) = 1
        _RockSteep("Rock Only On Steep (0 = everywhere)", Range(0,1)) = 0
        _MossColor("Moss Color", Color) = (0.30,0.40,0.14,1)
        _MossAmount("Moss on Top Faces", Range(0,1)) = 0
        _GroundMap("Ground Color Map", 2D) = "white" {}
        _GroundRect("Ground Rect (xMin, zMin, 1/w, 1/h)", Vector) = (0,0,0,0)
        _GroundTint("Ground Tint", Range(0,1)) = 0
        _ShadeColor("Shade Tint", Color) = (0.62,0.68,0.85,1)
        _Wrap("Light Wrap", Range(0,1)) = 0.45
        _RimStrength("Rim Light", Range(0,1)) = 0.15
        _Translucency("Back Light (leaves)", Range(0,1)) = 0
        _Wind("Wind Strength", Range(0,2)) = 0
        _WindScale("Wind Height Scale", Float) = 1
        _Flutter("Leaf Flutter", Range(0,1)) = 0
        _FadeShrink("LOD Fade by Shrinking", Float) = 0
        _BackfaceFlip("Flip Normal on Back Face", Float) = 1
        _Cutoff("Alpha Cutoff", Range(0,1)) = 0.5
        [Toggle(_ALPHATEST_ON)] _AlphaTest("Alpha Test", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
        [Toggle] _AlphaToMask("Alpha To Coverage", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 200

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _BaseMap_ST;
            float4 _ShadeColor;
            float _DetailScale;
            float _DetailStrength;
            float _DetailScale2;
            float _DetailStrength2;
            float _StrataStrength;
            float _StrataScale;
            float4 _RockMean;
            float _RockScale;
            float _RockStrength;
            float _RockNormalStrength;
            float _RockSteep;
            float4 _MossColor;
            float _MossAmount;
            float4 _GroundRect;
            float _GroundTint;
            float _Wrap;
            float _RimStrength;
            float _Translucency;
            float _Wind;
            float _WindScale;
            float _Flutter;
            float _FadeShrink;
            float _BackfaceFlip;
            float _Cutoff;
        CBUFFER_END

        // 주변광: 조명 프리셋이 넣는 하늘·땅 색(라이트 프로브 없이, 인스턴싱에서도 안정적)
        float4 _ScrunAmbientSky;
        float4 _ScrunAmbientGround;

        half3 Ambient(float3 n)
        {
            half3 sky = _ScrunAmbientSky.rgb, ground = _ScrunAmbientGround.rgb;
            if (dot(sky, 1) + dot(ground, 1) < 0.001) { sky = half3(0.55, 0.62, 0.75); ground = half3(0.35, 0.33, 0.28); }
            return lerp(ground, sky, saturate(n.y * 0.5 + 0.5));
        }

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_DetailMap); SAMPLER(sampler_DetailMap);
        TEXTURE2D(_GroundMap); SAMPLER(sampler_GroundMap);
        TEXTURE2D(_DetailMap2); SAMPLER(sampler_DetailMap2);
        TEXTURE2D_ARRAY(_BarkArray); SAMPLER(sampler_BarkArray);
        TEXTURE2D(_RockMap); SAMPLER(sampler_RockMap);
        TEXTURE2D(_RockNormal); SAMPLER(sampler_RockNormal);

        // 껍질 정점(UV1.y >= 0.5)은 잎 아틀라스 알파로 잘라내지 않음
        bool IsBark(float flag) { return flag > 0.5; }

        float3 ObjectOrigin() { return GetObjectToWorldMatrix()._m03_m13_m23; }

        // 바람: 물체 기준 높이의 제곱에 비례해 옆으로 휘고, 휜 만큼 내려앉아 길이가 늘지 않게.
        // 위치마다 위상이 달라 바람이 물결처럼 지나감. flutter = 잎 카드 떨림 가중치(UV1.x)
        float3 ApplyWind(float3 positionOS, float3 positionWS, float flutter)
        {
            if (_Wind <= 0) return positionWS;
            float3 origin = ObjectOrigin();
            float h = min(max(0, positionOS.y) * _WindScale, 6);   // 큰 나무(거목) 위쪽은 같은 폭으로
            float t = _Time.y;
            float phase = dot(origin.xz, float2(0.043, 0.031)) + t * 0.9;
            float gust = sin(t * 0.23 + origin.x * 0.006 + origin.z * 0.004) * 0.5 + 0.5;
            float2 sway = float2(sin(phase), cos(phase * 0.71) * 0.6) * (0.35 + 0.65 * gust);
            float2 offset = sway * _Wind * h * h * 0.035;
            float len2 = dot(offset, offset);
            positionWS.xz += offset;
            positionWS.y -= len2 / max(2 * max(positionOS.y, 0.05), 0.1);
            if (_Flutter > 0 && flutter > 0)
            {
                float k = dot(positionWS, float3(1.7, 2.3, 1.3));
                positionWS += float3(sin(t * 3.1 + k), sin(t * 2.3 + k * 1.3), cos(t * 2.7 + k)) * (0.04 * _Flutter * flutter * (0.5 + gust));
            }
            return positionWS;
        }

        // 거리 LOD: 풀은 작아지며 사라짐(물체 원점 쪽으로 오므림)
        float3 ApplyShrink(float3 positionOS, float2 lod)
        {
            if (_FadeShrink > 0) positionOS *= saturate(lod.y * lod.x);
            return positionOS;
        }

        // 완전히 안 보이는 LOD 정점은 화면 밖으로(래스터 안 함)
        bool LodHidden(float2 lod) { return _FadeShrink > 0 ? lod.y * lod.x <= 0.001 : (lod.x <= 0 || lod.y <= 0); }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            AlphaToMask [_AlphaToMask]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local _BARK_ARRAY
            #pragma shader_feature_local _ROCK_TRIPLANAR

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "ScrunCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                float2 uv1        : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float4 color      : TEXCOORD3;
                float  fog        : TEXCOORD4;
                float2 lod        : TEXCOORD5;
                float  bark       : TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // 바위 3면 투영: 월드 위치·법선으로 색 결(평균색으로 나눔 → 정점 색이 기본 색)과 노멀(whiteout 섞기)
            void RockTriplanar(float3 pos, inout float3 n, inout half3 albedo)
            {
                float3 w = pow(abs(n), 4); w /= dot(w, 1);
                float2 uvX = pos.zy * _RockScale, uvY = pos.xz * _RockScale + 0.31, uvZ = pos.xy * _RockScale + 0.67;
                half3 cX = SAMPLE_TEXTURE2D(_RockMap, sampler_RockMap, uvX).rgb;
                half3 cY = SAMPLE_TEXTURE2D(_RockMap, sampler_RockMap, uvY).rgb;
                half3 cZ = SAMPLE_TEXTURE2D(_RockMap, sampler_RockMap, uvZ).rgb;
                half3 rock = (cX * w.x + cY * w.y + cZ * w.z) / max(_RockMean.rgb, 0.02);
                half steep = 1;
                if (_RockSteep > 0) steep = saturate((1 - n.y - _RockSteep) * 6);
                half3 tX = UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal, sampler_RockNormal, uvX), _RockNormalStrength * steep);
                half3 tY = UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal, sampler_RockNormal, uvY), _RockNormalStrength * steep);
                half3 tZ = UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal, sampler_RockNormal, uvZ), _RockNormalStrength * steep);
                tX = half3(tX.xy + n.zy, abs(tX.z) * n.x);
                tY = half3(tY.xy + n.xz, abs(tY.z) * n.y);
                tZ = half3(tZ.xy + n.xy, abs(tZ.z) * n.z);
                float3 dn = normalize(tX.zyx * w.x + tY.xzy * w.y + tZ.xyz * w.z);
                // 윗면 이끼: 위를 보는 면 + 바위 결의 밝은 곳부터(경계가 결을 따라 들쭉날쭉)
                half lum = dot(rock, half3(0.3, 0.59, 0.11));
                half moss = saturate((dn.y - (1 - _MossAmount * 0.75)) * 5 + (lum - 1) * 1.5) * step(0.001, _MossAmount);
                albedo *= lerp(1, rock, _RockStrength * steep);
                albedo = lerp(albedo, _MossColor.rgb * lerp(0.75, 1.2, saturate(lum * 0.5)), moss);
                n = dn;
            }

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.bark = v.uv1.y;
                float2 lod = ScrunLodFactors(ObjectOrigin());
                o.lod = lod;
                if (LodHidden(lod)) { o.positionCS = float4(0, 0, -2, 1); return o; }
                float3 pos = ApplyShrink(v.positionOS.xyz, lod);
                float3 ws = TransformObjectToWorld(pos);
                ws = ApplyWind(pos, ws, v.uv1.x);
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.normalWS = normalize(TransformObjectToWorldDir(v.normalOS))  /* 인스턴싱에서 역행렬 없이(균일 크기만 사용) */;
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.color = v.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                if (_FadeShrink <= 0) ScrunLodClip(i.lod, i.positionCS.xy);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                #if defined(_BARK_ARRAY)
                    half3 barkTex = SAMPLE_TEXTURE2D_ARRAY(_BarkArray, sampler_BarkArray, i.uv, max(i.bark - 1, 0)).rgb;
                    if (IsBark(i.bark)) tex = half4(barkTex, 1);
                #endif
                half4 albedo = tex * _BaseColor * i.color;
                half alphaOut = 1;
                #if defined(_ALPHATEST_ON)
                    // 알파→커버리지: 잘라내기 경계를 화면 1픽셀 폭으로 날카롭게(멀리서도 잎이 녹지 않음)
                    half a = albedo.a;
                    alphaOut = saturate((a - _Cutoff) / max(fwidth(a), 1e-4) + 0.5);
                    clip(alphaOut - 0.01);
                #endif
                // 땅 색 물들이기(풀): 지형 색 지도에서 그 자리 땅색을 가져와 정점 명암(뿌리 어둡게·끝 밝게)만 남김
                // → 지역마다 땅색이 달라도 풀이 땅과 같은 색 계열로 자연스럽게 이어짐
                if (_GroundTint > 0 && _GroundRect.z > 0)
                {
                    half3 g = SAMPLE_TEXTURE2D(_GroundMap, sampler_GroundMap, (i.positionWS.xz - _GroundRect.xy) * _GroundRect.zw).rgb;
                    half lum = dot(i.color.rgb, half3(0.3, 0.59, 0.11));
                    half3 tinted = g * lerp(0.62, 1.3, saturate((lum - 0.18) / 0.36));
                    albedo.rgb = lerp(albedo.rgb, tinted, _GroundTint);
                }
                // 바위 지층: 높이 방향 가로 띠(가파른 면일수록 진하게)
                if (_StrataStrength > 0)
                {
                    float2 suv = float2(dot(i.positionWS.xz, float2(0.011, 0.013)), i.positionWS.y * _StrataScale);
                    half b = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, suv).r * 2;
                    half steep = saturate((1 - abs(normalize(i.normalWS).y)) * 2.5);
                    albedo.rgb *= lerp(1, b, _StrataStrength * steep);
                }
                if (_DetailStrength > 0)
                {
                    half d = SAMPLE_TEXTURE2D(_DetailMap, sampler_DetailMap, i.positionWS.xz * _DetailScale).r * 2;
                    albedo.rgb *= lerp(1, d, _DetailStrength);
                }
                if (_DetailStrength2 > 0)
                {
                    half d2 = SAMPLE_TEXTURE2D(_DetailMap2, sampler_DetailMap2, i.positionWS.xz * _DetailScale2 + 0.37).r * 2;
                    albedo.rgb *= lerp(1, d2, _DetailStrength2);
                }

                // 잎 카드는 뒤집지 않음(나무 전체를 감싸는 둥근 법선을 써서 양면이 똑같이 부드럽게 빛남)
                float3 n = normalize(i.normalWS) * ((front || _BackfaceFlip < 0.5) ? 1 : -1);
                #if defined(_ROCK_TRIPLANAR)
                    RockTriplanar(i.positionWS, n, albedo.rgb);
                #endif
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light light = GetMainLight(shadowCoord);
                half ndl = dot(n, light.direction);
                half wrap = saturate((ndl + _Wrap) / (1 + _Wrap));
                half lit = wrap * light.shadowAttenuation;

                half3 ambient = Ambient(n);
                half3 shade = ambient * _ShadeColor.rgb;
                half3 col = albedo.rgb * (shade + light.color * lit);

                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                // 잎 역광: 해를 마주 볼 때 잎이 빛을 머금어 밝아짐
                if (_Translucency > 0)
                {
                    half back = pow(saturate(dot(-v, light.direction)), 4) * _Translucency * light.shadowAttenuation;
                    col += albedo.rgb * light.color * back * 0.8;
                }
                // 테두리 빛: 역광에서 윤곽이 살아나게
                half rim = pow(1 - saturate(dot(n, v)), 3) * _RimStrength * saturate(dot(-v, light.direction) * 0.5 + 0.5);
                col += light.color * rim * albedo.rgb;

                col = ScrunFog(col, i.positionWS, i.fog);
                return half4(col, alphaOut);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma shader_feature_local _ALPHATEST_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "ScrunCommon.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 uv1        : TEXCOORD1;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float  alpha      : TEXCOORD1;
                float  bark       : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                o.bark = v.uv1.y;
                // 그림자는 디더 없이 구간 가운데에서 바로 바뀜(그림자가 깜빡이거나 사라지지 않게 두 LOD가 이어 받음)
                float2 lod = ScrunLodFactors(ObjectOrigin());
                bool hidden = _FadeShrink > 0 ? lod.x * lod.y <= 0.001 : (lod.x < 0.5 || lod.y <= 0.5);
                if (hidden) { o.positionCS = float4(0, 0, -2, 1); return o; }
                float3 pos = ApplyShrink(v.positionOS.xyz, lod);
                float3 ws = ApplyWind(pos, TransformObjectToWorld(pos), v.uv1.x);
                float3 nws = normalize(TransformObjectToWorldDir(v.normalOS))  /* 인스턴싱에서 역행렬 없이(균일 크기만 사용) */;
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 ld = normalize(_LightPosition - ws);
                #else
                    float3 ld = _LightDirection;
                #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(ws, nws, ld));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.alpha = v.color.a;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                #if defined(_ALPHATEST_ON)
                    if (!IsBark(i.bark)) clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a * i.alpha - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma shader_feature_local _ALPHATEST_ON
            #include "ScrunCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float2 uv1        : TEXCOORD1;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float2 lod        : TEXCOORD1;
                float  alpha      : TEXCOORD2;
                float  bark       : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                float2 lod = ScrunLodFactors(ObjectOrigin());
                o.lod = lod;
                o.bark = v.uv1.y;
                if (LodHidden(lod)) { o.positionCS = float4(0, 0, -2, 1); return o; }
                float3 pos = ApplyShrink(v.positionOS.xyz, lod);
                o.positionCS = TransformWorldToHClip(ApplyWind(pos, TransformObjectToWorld(pos), v.uv1.x));
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.alpha = v.color.a;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                if (_FadeShrink <= 0) ScrunLodClip(i.lod, i.positionCS.xy);
                #if defined(_ALPHATEST_ON)
                    if (!IsBark(i.bark)) clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a * i.alpha - _Cutoff);
                #endif
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
