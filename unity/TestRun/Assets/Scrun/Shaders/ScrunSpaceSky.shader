// 우주 루프 하늘 v2: 그라데이션 + 해 + 별(크기·색 다양, 반짝임) + 성운·은하수 띠 + 새털구름 + 지평선 빛띠.
// 값은 SpaceLoopAtmosphere가 구간에 따라 섞어 넣는다. 잡음은 방향 벡터 기반이라 이음매가 없다.
Shader "Scrun/SpaceSky"
{
    Properties
    {
        _TopColor("Top", Color) = (0.36,0.29,0.45,1)
        _HorizonColor("Horizon", Color) = (0.95,0.77,0.6,1)
        _BelowColor("Below", Color) = (0.88,0.66,0.52,1)
        _HorizonSharp("Horizon Height", Range(0.05,1)) = 0.55
        _SunColor("Sun", Color) = (1,0.8,0.6,1)
        _SunDir("Sun Direction", Vector) = (0,0.2,1,0)
        _SunSize("Sun Size", Range(0.9990,0.99999)) = 0.99985
        _SunGlow("Sun Glow", Range(0,4)) = 1.2
        _SunDisc("Sun Disc", Range(0,1)) = 1
        _Stars("Stars", Range(0,3)) = 0.25
        _Nebula("Nebula", Range(0,3)) = 0
        _NebulaA("Nebula A", Color) = (0.25,0.35,0.8,1)
        _NebulaB("Nebula B", Color) = (0.6,0.4,0.9,1)
        _NebulaAxis("Nebula Band Normal", Vector) = (0.3,0.6,0.74,0)
        _NebulaWidth("Nebula Width", Range(0.05,1)) = 0.28
        _Cirrus("Cirrus", Range(0,2)) = 0
        _CirrusColor("Cirrus Color", Color) = (1,1,1,1)
        _GlowColor("Horizon Glow", Color) = (0,0,0,1)
        _GlowWidth("Horizon Glow Width", Range(0.01,0.5)) = 0.08
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _TopColor, _HorizonColor, _BelowColor, _SunColor, _SunDir;
                float _HorizonSharp, _SunSize, _SunGlow, _SunDisc, _Stars;
                float _Nebula, _NebulaWidth, _Cirrus, _GlowWidth;
                float4 _NebulaA, _NebulaB, _NebulaAxis, _CirrusColor, _GlowColor;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };
            V vert(A v) { V o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.dir = v.positionOS.xyz; return o; }

            float hash(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float vnoise(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(lerp(hash(i), hash(i + float3(1,0,0)), f.x), lerp(hash(i + float3(0,1,0)), hash(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(hash(i + float3(0,0,1)), hash(i + float3(1,0,1)), f.x), lerp(hash(i + float3(0,1,1)), hash(i + float3(1,1,1)), f.x), f.y), f.z);
            }
            float fbm(float3 p)
            {
                float s = 0, a = 0.5;
                for (int k = 0; k < 4; k++) { s += a * vnoise(p); p = p * 2.03 + 17.1; a *= 0.5; }
                return s;
            }

            // 별 한 층: 격자 칸마다 하나, 일부만 켬
            float3 starLayer(float3 d, float scale, float keep, float size, float tw)
            {
                float3 g = d * scale;
                float3 cell = floor(g);
                float h = hash(cell);
                float3 c = cell + 0.5 + (float3(hash(cell + 1.3), hash(cell + 2.7), hash(cell + 4.1)) - 0.5) * 0.6;
                float r = length(g - c);
                float on = step(keep, h);
                float b = 0.35 + 0.65 * hash(cell + 7.7);
                float tws = 0.7 + 0.3 * sin(_Time.y * (1.5 + 3 * hash(cell + 9.1)) + h * 40);
                float core = smoothstep(size, 0.0, r);
                float halo = exp(-r * r / (size * size * 6)) * 0.35;
                float t = hash(cell + 5.3);
                float3 tint = t < 0.33 ? float3(0.75, 0.85, 1.15) : (t < 0.66 ? float3(1, 1, 1) : float3(1.15, 0.95, 0.75));
                return tint * (core + halo) * on * b * lerp(1, tws, tw);
            }

            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float up = smoothstep(0, _HorizonSharp, d.y);
                half3 col = lerp(_HorizonColor.rgb, _TopColor.rgb, up);
                col = lerp(col, _BelowColor.rgb, saturate(-d.y / 0.12));
                float above = saturate(d.y * 6 + 0.15);

                // 성운·은하수: 대원 둘레 띠 + 잡음, 먼지 띠로 어둡게
                float3 ax = normalize(_NebulaAxis.xyz + 1e-4);
                float band = exp(-pow(dot(d, ax) / _NebulaWidth, 2));
                float n1 = fbm(d * 3.2 + 3.7);
                float n2 = fbm(d * 7.5 + 11.3);
                float dust = smoothstep(0.45, 0.75, fbm(d * 5.0 + 23.0));
                float neb = band * (0.35 + n1 * n1 * 1.6) * (1 - dust * 0.65);
                float3 nebCol = lerp(_NebulaA.rgb, _NebulaB.rgb, saturate(n2 * 1.6 - 0.3));
                col += nebCol * neb * _Nebula * above;
                // 띠 밖에도 옅은 성운 구름
                float wisp = smoothstep(0.55, 0.85, fbm(d * 2.1 + 41.0));
                col += lerp(_NebulaB.rgb, _NebulaA.rgb, n2) * wisp * 0.18 * _Nebula * above;

                // 별: 촘촘한 작은 별 + 드문 큰 별, 성운 띠 안은 더 많이
                float3 st = starLayer(d, 320, 0.90 - band * 0.06, 0.16, 0.6) * 0.9
                          + starLayer(d, 95, 0.985, 0.10, 1.0) * 2.2;
                col += st * _Stars * saturate(d.y * 4 + 0.05);

                // 새털구름: 하늘 평면에 투영한 늘어진 잡음, 해 쪽이 밝음
                float3 sd = normalize(_SunDir.xyz);
                float sdot = dot(d, sd);
                if (_Cirrus > 0.001 && d.y > 0)
                {
                    float2 p = d.xz / (d.y + 0.12);
                    float t = _Time.y * 0.004;
                    float q = fbm(float3(p.x * 0.55 + t, p.y * 2.2, 0.0)) * 0.7 + fbm(float3(p * 1.7 + t * 2, 5.0)) * 0.3;
                    float shape = smoothstep(0.48, 0.78, q) * smoothstep(0.02, 0.22, d.y) * (1 - smoothstep(0.55, 0.95, d.y));
                    float3 cc = _CirrusColor.rgb * (0.85 + 0.6 * pow(saturate(sdot), 4));
                    col = lerp(col, cc, shape * saturate(_Cirrus));
                }

                // 지평선 빛띠
                col += _GlowColor.rgb * exp(-abs(d.y - 0.01) / _GlowWidth);

                // 해
                float disc = smoothstep(_SunSize, _SunSize + (1 - _SunSize) * 0.5, sdot) * _SunDisc;
                float glow = pow(saturate(sdot), 64) * 0.6 + pow(saturate(sdot), 8) * 0.25;
                col += _SunColor.rgb * (disc * 14 + glow * _SunGlow);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
