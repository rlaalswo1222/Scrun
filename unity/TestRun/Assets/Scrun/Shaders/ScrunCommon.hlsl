// Scrun 셰이더 공용: 대기(안개)·LOD 디더 전환
#ifndef SCRUN_COMMON_INCLUDED
#define SCRUN_COMMON_INCLUDED

// ---------- 대기 ----------
// 조명 프리셋(LightingPresets)이 넣는 전역 값. 밀도가 0이면(프리셋 없음) Unity 기본 안개로 대체
float4 _ScrunFogColor;      // 해 반대쪽·지평선 색
float4 _ScrunFogSunColor;   // 해 쪽으로 볼 때 색(빛이 산란되어 밝고 따뜻함)
float4 _ScrunFogParams;     // x = 거리 밀도(1/m), y = 높이 감쇠(1/m), z = 기준 높이(m), w = 최대 농도(0~1)
float4 _ScrunFogSunDir;     // xyz = 해 쪽 방향

// 월드 위치로 안개 섞기. 거리 지수(가까운 중경부터 옅게 끼어 거리감) × 높이 감쇠(낮은 곳·골짜기에 짙게)
half3 ScrunFog(half3 col, float3 positionWS, float unityFogFactor)
{
    if (_ScrunFogParams.x <= 0)
        return MixFog(col, unityFogFactor);
    float3 toP = positionWS - _WorldSpaceCameraPos;
    float dist = length(toP);
    float3 dir = toP / max(dist, 1e-3);
    float f = 1 - exp(-dist * _ScrunFogParams.x);
    // 높이: 기준 높이 위로 올라갈수록 옅어짐(먼 산 윗부분이 또렷하게 남아 하늘선이 보임)
    float h = max(0, positionWS.y - _ScrunFogParams.z);
    f *= lerp(1, exp(-h * _ScrunFogParams.y), 0.75);
    f = min(f, _ScrunFogParams.w);
    half sun = pow(saturate(dot(dir, _ScrunFogSunDir.xyz)), 6);
    half3 fogCol = lerp(_ScrunFogColor.rgb, _ScrunFogSunColor.rgb, sun);
    return lerp(col, fogCol, f);
}

// ---------- LOD 디더 전환 ----------
// InstancedScatter가 그리기마다 넣는 거리 구간(m): x~y에서 나타나고 z~w에서 사라짐
float4 _ScrunLodFade;

// 개체 원점과 카메라 거리로 이 LOD의 보임 정도(0~1)
float2 ScrunLodFactors(float3 originWS)
{
    float4 r = _ScrunLodFade;
    if (r.w <= 0) return float2(1, 1);   // 설정 없음 = 항상 보임
    float dist = distance(originWS, _WorldSpaceCameraPos);
    float fadeIn = r.y > r.x ? saturate((dist - r.x) / (r.y - r.x)) : (dist >= r.x ? 1 : 0);
    float fadeOut = 1 - saturate((dist - r.z) / max(r.w - r.z, 1e-3));
    return float2(fadeIn, fadeOut);
}

// 화면 공간 잡음(interleaved gradient noise) 0~1
float ScrunDitherNoise(float2 pixel)
{
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

// 나타나는 LOD는 t, 사라지는 LOD는 1-t를 써서 두 LOD가 같은 픽셀을 나눠 가짐(겹치거나 비지 않음)
void ScrunLodClip(float2 factors, float2 pixel)
{
    if (factors.x >= 1 && factors.y >= 1) return;
    float t = ScrunDitherNoise(pixel);
    clip(factors.x - (1 - t) - 1e-4);
    clip(factors.y - t);
}
#endif
