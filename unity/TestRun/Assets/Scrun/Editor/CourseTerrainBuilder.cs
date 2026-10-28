using System.Collections.Generic;
using Scrun;
using UnityEngine;

// 코스 주변 지형(높이 메쉬 + 색 지도)을 만든다. 도로 근처는 노면 높이(가장 가까운 도로)에 맞추고,
// 멀어질수록 여러 도로 높이를 부드럽게 섞은 바탕 위에 언덕 → 봉우리가 솟는다. 호수·협곡은 정해진 높이로 파낸다.
// (도로 높낮이는 화면 전용: 속도·러닝머신 경사와 무관)
public class CourseTerrainBuilder
{
    public Vector2 Center;          // 코스 중심(x,z)
    public float Size = 3600f;      // 한 변(m)
    public float Cell = 10f;        // 메쉬 격자 간격(m)
    public int ChunkCells = 36;     // 청크 한 변의 칸 수
    public float FlatRadius = 30f;  // 도로에서 이 거리까진 완전 평지
    public float HillFull = 260f;   // 이 거리에서 언덕 높이 100%
    public float LakeDepth = 4f;
    public float MountainStart = 750f, MountainFull = 1500f;   // 중심에서 이 거리부터 산맥

    // ---------- 섬 모드: 해안 절벽 + 바다 ----------
    // 해안선 = (섬 중심 기준 물결진 원) ∪ (도로에서 RoadLandMargin 이내). 해안에서 CliffWidth에 걸쳐 SeaFloor까지 떨어짐
    public bool Island;
    public Vector2 IslandCenter;
    public float CoastRadius = 1950f, RoadLandMargin = 130f, CliffWidth = 60f, SeaFloor = -45f;

    // 도로 옆 둑: 위치 → 둑 높이(m). 도로에서 BankStart~BankFull 사이에 가파르게 솟아 길을 감싼다(Zwift식 골짜기 길).
    // 지형 격자(12m)가 도로 위로 솟지 않도록 BankStart는 격자 대각선(17m) + 갓길(6.6m)보다 멀게.
    public System.Func<float, float, float> BankAmount;
    public const float BankStart = 24f, BankFull = 60f;

    // 둔덕(터널이 지나는 언덕 등): 중심·반지름·높이. 도로 바로 옆(갓길)은 노면 그대로라 길은 막지 않음
    public readonly List<(Vector2 c, float r, float h)> Mounds = new();

    float MoundHeight(float x, float z)
    {
        float h = 0f;
        var q = new Vector2(x, z);
        foreach (var (c, r, height) in Mounds)
        {
            float f = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(r * 0.2f, r, Vector2.Distance(q, c)));
            h = Mathf.Max(h, height * f);
        }
        return h;
    }

    public float Bank(float x, float z, float roadDist)
    {
        if (BankAmount == null || roadDist <= BankStart) return 0f;
        float b = BankAmount(x, z) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(BankStart, BankFull, roadDist))
                  * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(110f, 220f, roadDist)));
        if (Island) b *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(30f, 120f, CoastDistance(x, z)));   // 바다 쪽은 낮게
        if (FlatZones.Count > 0) b *= FlatFactor(x, z);
        return b;
    }

    // 평지 구역(트랙·광장): 반지름 안은 언덕 없음, 바깥 blend m에 걸쳐 원래 지형으로. 식생도 안 심음
    public readonly List<(Vector2 c, float r, float blend)> FlatZones = new();
    // 식생 제외 구역(등대·풍력발전기 받침 등): 지형은 그대로
    public readonly List<(Vector2 c, float r)> Exclusions = new();

    public bool Excluded(float x, float z)
    {
        var q = new Vector2(x, z);
        foreach (var (c, r, _) in FlatZones) if ((q - c).sqrMagnitude < r * r) return true;
        foreach (var (c, r) in Exclusions) if ((q - c).sqrMagnitude < r * r) return true;
        return false;
    }

    float FlatFactor(float x, float z)
    {
        float f = 1f;
        var q = new Vector2(x, z);
        foreach (var (c, r, blend) in FlatZones)
            f = Mathf.Min(f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(r, r + blend, Vector2.Distance(q, c))));
        return f;
    }

    // 협곡: 꺾은선을 따라 width 폭, depth 깊이로 파냄(도로가 지나가면 다리 필요)
    public readonly List<(Vector2[] pts, float width, float depth, Rect bounds)> Canyons = new();

    public void AddCanyon(IList<Vector3> controlPoints, float width, float depth)
    {
        var line = Polyline.FromControlPoints(controlPoints, false, 20f);
        var pts = new Vector2[line.Points.Count];
        var r = Rect.MinMaxRect(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
        for (int i = 0; i < pts.Length; i++)
        {
            pts[i] = new Vector2(line.Points[i].x, line.Points[i].z);
            r.xMin = Mathf.Min(r.xMin, pts[i].x - width - 10f); r.xMax = Mathf.Max(r.xMax, pts[i].x + width + 10f);
            r.yMin = Mathf.Min(r.yMin, pts[i].y - width - 10f); r.yMax = Mathf.Max(r.yMax, pts[i].y + width + 10f);
        }
        Canyons.Add((pts, width, depth, r));
    }

    // 해안선 안쪽까지의 거리(+ = 육지 안쪽, - = 바다 쪽)
    public float CoastDistance(float x, float z)
    {
        var v = new Vector2(x, z) - IslandCenter;
        float a = Mathf.Atan2(v.y, v.x);
        float r = CoastRadius * (1f + 0.068f * Mathf.Sin(3f * a + 0.4f) + 0.038f * Mathf.Sin(7f * a + 1.3f) + 0.021f * Mathf.Sin(13f * a));
        r += 25f * (Mathf.PerlinNoise(x * 0.01f + 3f, z * 0.01f + 9f) - 0.5f);
        float radial = r - v.magnitude;
        float road = Mathf.Max(RoadLandMargin - LandDistance(x, z), CliffRoadMargin - RoadDistance(x, z));
        return SmoothMax(radial, road, 60f);
    }

    static float SmoothMax(float a, float b, float k)
    {
        float h = Mathf.Clamp01(0.5f + 0.5f * (a - b) / k);
        return Mathf.Lerp(b, a, h) + k * h * (1f - h);
    }

    const float RoadWidthHalfShoulder = 6.6f;

    // 협곡 단면(0 = 밖, 1 = 바닥): 바닥은 평평하고 벽은 가파르게
    float CanyonCarve(float x, float z)
    {
        float carve = 0f;
        var q = new Vector2(x, z);
        foreach (var (pts, width, depth, bounds) in Canyons)
        {
            if (!bounds.Contains(q)) continue;
            float best = float.MaxValue;
            for (int i = 0; i < pts.Length - 1; i++) best = Mathf.Min(best, SegDistSq(q, pts[i], pts[i + 1]));
            float d = Mathf.Sqrt(best) + 10f * (Mathf.PerlinNoise(x * 0.03f, z * 0.03f) - 0.5f);
            if (d >= width) continue;
            float t = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(width * 0.3f, width, d));
            carve = Mathf.Max(carve, Mathf.Pow(t, 0.6f));
        }
        return carve;
    }
    // 호수 = 도로 한쪽을 따라 늘어선 원들의 합집합 → 달리는 방향 앞쪽으로 물가가 계속 보임
    public readonly List<(Vector2 c, float r)> LakeCircles = new();

    // 코스 구간 [from, to]의 왼쪽(진행 방향 기준)에 호수를 만든다. shore = 도로 중앙에서 물가까지 최소 거리
    // side: -1 = 진행 방향 왼쪽, +1 = 오른쪽
    public void AddLakeAlong(IPath track, float from, float to, float shore, float minR, float maxR, float side = -1f)
    {
        for (float d = from; d <= to; d += 20f)
        {
            float t = Mathf.InverseLerp(from, to, d);
            // 양 끝은 좁게, 가운데는 넓게 + 들쭉날쭉
            float r = Mathf.Lerp(minR, maxR, Mathf.Sin(t * Mathf.PI)) * (0.85f + 0.3f * Mathf.PerlinNoise(d * 0.01f, 3.3f));
            var p = track.GetPoint(d);
            var dir = track.GetRight(d) * side;
            var c = new Vector3(p.x, 0f, p.z) + dir * (shore + r);
            LakeCircles.Add((new Vector2(c.x, c.z), r));
            lakeRect = null;
        }
    }

    public Rect LakeBounds()
    {
        var r = Rect.MinMaxRect(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
        foreach (var (c, rad) in LakeCircles)
        {
            r.xMin = Mathf.Min(r.xMin, c.x - rad); r.yMin = Mathf.Min(r.yMin, c.y - rad);
            r.xMax = Mathf.Max(r.xMax, c.x + rad); r.yMax = Mathf.Max(r.yMax, c.y + rad);
        }
        return r;
    }

    const float FieldCell = 8f;     // 도로 거리장 격자(m)
    const float FieldMax = 400f;    // 이보다 먼 거리는 400으로 취급
    float[] field;
    float[] nearH;   // 가장 가까운 도로 점의 노면 높이
    float[] blendH;  // 주변 도로 높이의 거리 가중 평균(도로 사이 땅이 부드럽게 이어지는 바탕)
    int fieldN;

    float MinX => Center.x - Size / 2f;
    float MinZ => Center.y - Size / 2f;
    // 색 지도 좌표(셰이더 _GroundRect용): xMin, zMin, 1/폭, 1/높이
    public Vector4 GroundRect => new(MinX, MinZ, 1f / Size, 1f / Size);

    // ---------- 도로 거리장 ----------
    public void BuildDistanceField(Track track, float sampleStep = 5f) => BuildDistanceField(new IPath[] { track }, sampleStep);

    public void BuildDistanceField(IEnumerable<IPath> paths, float sampleStep = 5f)
    {
        fieldN = Mathf.CeilToInt(Size / FieldCell) + 1;
        field = ComputeField(paths, sampleStep, out nearH, out blendH);
    }

    // 섬 해안선용 "육지 보장" 거리장(해안 절벽 길처럼 바다에 바짝 붙는 길은 빼고 만듦)
    float[] landField;
    public float CliffRoadMargin = 30f;   // landField에 없는 길(해안 절벽 길)은 이만큼만 육지 보장

    public void BuildLandField(IEnumerable<IPath> paths, float sampleStep = 5f) => landField = ComputeField(paths, sampleStep, out _, out _);

    float[] ComputeField(IEnumerable<IPath> paths, float sampleStep, out float[] nearHeights, out float[] blendHeights)
    {
        var segs = new List<(Vector2 a, Vector2 b)>();
        var segY = new List<(float a, float b)>();
        float ySum = 0f;
        foreach (var path in paths)
        {
            var p0 = path.GetPoint(0f);
            Vector2 prev = Flat(p0); float prevY = p0.y;
            for (float d = sampleStep; ; d += sampleStep)
            {
                bool last = d >= path.Length;
                var p3 = path.GetPoint(last ? path.Length : d);
                var p = Flat(p3);
                segs.Add((prev, p));
                segY.Add((prevY, p3.y));
                ySum += p3.y;
                prev = p; prevY = p3.y;
                if (last) break;
            }
        }
        float yAvg = segs.Count > 0 ? ySum / segs.Count : 0f;

        // 선분을 100m 칸에 나눠 담아 가까운 칸만 검사
        const float bucket = 100f;
        var buckets = new Dictionary<Vector2Int, List<int>>();
        for (int i = 0; i < segs.Count; i++)
        {
            var mid = (segs[i].a + segs[i].b) / 2f;
            var key = new Vector2Int(Mathf.FloorToInt(mid.x / bucket), Mathf.FloorToInt(mid.y / bucket));
            if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<int>();
            list.Add(i);
        }
        int reach = Mathf.CeilToInt(FieldMax / bucket) + 1;

        int n = Mathf.CeilToInt(Size / FieldCell) + 1;
        var f = new float[n * n];
        var nh = new float[n * n];
        var bh = new float[n * n];
        for (int iz = 0; iz < n; iz++)
            for (int ix = 0; ix < n; ix++)
            {
                var q = new Vector2(MinX + ix * FieldCell, MinZ + iz * FieldCell);
                var kq = new Vector2Int(Mathf.FloorToInt(q.x / bucket), Mathf.FloorToInt(q.y / bucket));
                float best = FieldMax * FieldMax, bestY = yAvg, wSum = 0f, hSum = 0f;
                for (int bz = -reach; bz <= reach; bz++)
                    for (int bx = -reach; bx <= reach; bx++)
                    {
                        if (!buckets.TryGetValue(new Vector2Int(kq.x + bx, kq.y + bz), out var list)) continue;
                        foreach (int i in list)
                        {
                            var (a, b) = segs[i];
                            var ab = b - a;
                            float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                            float dsq = (a + ab * t - q).sqrMagnitude;
                            float y = Mathf.Lerp(segY[i].a, segY[i].b, t);
                            if (dsq < best) { best = dsq; bestY = y; }
                            if (dsq < FieldMax * FieldMax)
                            {
                                float dd = Mathf.Sqrt(dsq) + 25f;
                                float w = 1f / (dd * dd * dd);
                                wSum += w; hSum += w * y;
                            }
                        }
                    }
                int idx = iz * n + ix;
                f[idx] = Mathf.Sqrt(best);
                nh[idx] = bestY;
                bh[idx] = wSum > 0f ? hSum / wSum : yAvg;
            }
        nearHeights = nh;
        blendHeights = bh;
        return f;
    }

    static Vector2 Flat(Vector3 p) => new(p.x, p.z);

    static float SegDistSq(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
        return (a + ab * t - p).sqrMagnitude;
    }

    public float RoadDistance(float x, float z) => SampleField(field, x, z);
    // 가장 가까운 도로의 노면 높이(높이표 없는 평지 코스는 0)
    public float RoadHeight(float x, float z) => nearH != null ? SampleField(nearH, x, z) : 0f;
    float BlendHeight(float x, float z) => blendH != null ? SampleField(blendH, x, z) : 0f;
    public float LandDistance(float x, float z) => landField != null ? SampleField(landField, x, z) : RoadDistance(x, z);

    float SampleField(float[] f, float x, float z)
    {
        var field = f;
        float fx = Mathf.Clamp((x - MinX) / FieldCell, 0, fieldN - 1.001f);
        float fz = Mathf.Clamp((z - MinZ) / FieldCell, 0, fieldN - 1.001f);
        int ix = (int)fx, iz = (int)fz;
        float tx = fx - ix, tz = fz - iz;
        float a = field[iz * fieldN + ix], b = field[iz * fieldN + ix + 1];
        float c = field[(iz + 1) * fieldN + ix], d = field[(iz + 1) * fieldN + ix + 1];
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
    }

    // ---------- 높이 ----------
    static float Fbm(float x, float z, int octaves)
    {
        float sum = 0, amp = 0.5f, freq = 1f, norm = 0;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * Mathf.PerlinNoise(x * freq + 31.7f * i, z * freq - 17.3f * i);
            norm += amp; amp *= 0.5f; freq *= 2.03f;
        }
        return sum / norm;
    }

    static float Ridged(float x, float z)
    {
        float sum = 0, amp = 0.55f, freq = 1f;
        for (int i = 0; i < 4; i++)
        {
            float n = 1f - Mathf.Abs(Mathf.PerlinNoise(x * freq + 5.1f * i, z * freq + 9.2f * i) * 2f - 1f);
            sum += amp * n * n; amp *= 0.5f; freq *= 2.1f;
        }
        return sum;
    }

    // 0(호수 밖) ~ 1(호수 안). 물가선은 노이즈로 울퉁불퉁
    Rect? lakeRect;

    public float LakeMask(float x, float z)
    {
        if (LakeCircles.Count == 0) return 0f;
        lakeRect ??= LakeBounds();
        var lr = lakeRect.Value;
        const float margin = 40f;   // 물가 노이즈 여유
        if (x < lr.xMin - margin || x > lr.xMax + margin || z < lr.yMin - margin || z > lr.yMax + margin) return 0f;
        float wobble = 1f + 0.12f * (Mathf.PerlinNoise(x * 0.02f + 7f, z * 0.02f + 3f) - 0.5f) * 2f;
        float best = 0f;
        foreach (var (c, r) in LakeCircles)
        {
            float k = Vector2.Distance(new Vector2(x, z), c) / (r * wobble);
            if (k < 1.05f) best = Mathf.Max(best, 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.75f, 1.05f, k)));
        }
        return best;
    }

    // 협곡 바닥 높이(바다 수면 -25 아래 → 물이 참). 도로가 높은 곳을 지나도 협곡은 이 높이까지 파임
    public float CanyonFloor = -30f;

    public float Height(float x, float z)
    {
        float d = RoadDistance(x, z);
        // 바탕 높이: 길 가까이는 그 길의 노면, 멀어질수록 주변 길들의 부드러운 평균. 평지 구역(광장·트랙)은 0
        float roadH = RoadHeight(x, z);
        float baseH = Mathf.Lerp(roadH, BlendHeight(x, z), Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FlatRadius, 110f, d)));
        if (FlatZones.Count > 0) baseH *= FlatFactor(x, z);
        // 도로·갓길 밑은 노면보다 4cm 아래(겹쳐 깜빡이지 않게). 협곡 위 다리 구간은 제외(아래가 파여야 함)
        if (d < RoadWidthHalfShoulder && (Canyons.Count == 0 || CanyonCarve(x, z) <= 0f)) return roadH - 0.04f;
        float hill = Fbm(x * 0.0028f + 11f, z * 0.0028f + 7f, 4);
        float h = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FlatRadius, HillFull, d)) * hill * 55f;
        if (Island) h *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(10f, 250f, CoastDistance(x, z)));   // 해안 쪽은 낮게 → 절벽 길에서 바다가 보임
        if (FlatZones.Count > 0) h *= FlatFactor(x, z);
        h += PeakHeight(x, z, d);
        h += Bank(x, z, d);
        // 둔덕은 길에서 20m 밖부터(지형 격자 12m가 노면 위로 비스듬히 덮지 않게). 그 안쪽은 터널 옆 바위가 채움
        if (Mounds.Count > 0) h += MoundHeight(x, z) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(20f, 42f, d));

        float r = Vector2.Distance(new Vector2(x, z), Center);
        float m = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(MountainStart, MountainFull, r));
        h += m * (80f + 240f * Ridged(x * 0.0011f, z * 0.0011f));

        h += baseH;
        float lake = LakeMask(x, z) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(9f, 20f, d));
        h = Mathf.Lerp(h, -LakeDepth, lake);

        if (Canyons.Count > 0) h = Mathf.Lerp(h, CanyonFloor + 4f * Mathf.PerlinNoise(x * 0.02f, z * 0.02f), CanyonCarve(x, z));
        if (Island)
        {
            float s = CoastDistance(x, z);
            float t = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-CliffWidth * 0.5f, CliffWidth * 0.5f, s));
            float floor = SeaFloor + 6f * Mathf.PerlinNoise(x * 0.004f + 21f, z * 0.004f + 4f);
            h = Mathf.Lerp(floor, h, t);
        }
        return h;
    }

    public Vector3 NormalAt(float x, float z) => Normal(x, z);

    // ---------- 산봉우리(하늘선) ----------
    // 도로에서 가장 먼 곳들을 골라 봉우리를 세운다(도로는 평지 유지). 반지름 = 도로까지 거리 - 여유
    public readonly List<(Vector2 c, float radius, float height)> Peaks = new();

    public void AutoPeaks(int count, float minRoad = 240f, float minCoast = 300f, float maxHeight = 230f)
    {
        var cands = new List<(Vector2 p, float d)>();
        float half = Size / 2f;
        for (float x = Center.x - half; x < Center.x + half; x += 40f)
            for (float z = Center.y - half; z < Center.y + half; z += 40f)
            {
                float d = RoadDistance(x, z);
                if (d < minRoad) continue;
                if (Island && CoastDistance(x, z) < minCoast) continue;
                if (LakeMask(x, z) > 0f) continue;
                cands.Add((new Vector2(x, z), d));
            }
        cands.Sort((a, b) => b.d.CompareTo(a.d));
        foreach (var (p, d) in cands)
        {
            if (Peaks.Exists(k => Vector2.Distance(k.c, p) < 650f)) continue;
            float radius = Mathf.Min(d - 60f, Island ? CoastDistance(p.x, p.y) - 40f : d);
            Peaks.Add((p, radius, Mathf.Min(maxHeight, radius * 0.8f)));
            if (Peaks.Count == count) break;
        }
    }

    float PeakHeight(float x, float z, float roadDist)
    {
        if (Peaks.Count == 0) return 0f;
        float h = 0f;
        var q = new Vector2(x, z);
        foreach (var (c, radius, height) in Peaks)
        {
            float f = 1f - Vector2.Distance(q, c) / radius;
            if (f <= 0f) continue;
            float shape = Mathf.Pow(f, 1.5f) * (0.7f + 0.6f * Ridged(x * 0.004f + c.x * 0.01f, z * 0.004f));
            h = Mathf.Max(h, height * shape);
        }
        return h * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(40f, 160f, roadDist));
    }

    Vector3 Normal(float x, float z)
    {
        float e = Cell * 0.5f;
        float hx = Height(x + e, z) - Height(x - e, z);
        float hz = Height(x, z + e) - Height(x, z - e);
        return new Vector3(-hx, 2f * e, -hz).normalized;
    }

    // ---------- 메쉬 ----------
    // 청크마다 자세한 메쉬 + 거친 메쉬(4칸씩 건너뜀, 멀리용). 가장자리에 아래로 늘어진 "치마"를 둘러
    // LOD가 다른 이웃 청크 사이 틈이 보이지 않게 한다. 전부 skipBelow보다 낮은 청크(깊은 바다 밑)는 만들지 않음.
    public List<(Mesh mesh, Mesh low, Vector3 origin)> BuildChunks(float skipBelow = float.MinValue)
    {
        int cells = Mathf.CeilToInt(Size / Cell);
        int chunks = Mathf.CeilToInt((float)cells / ChunkCells);
        var result = new List<(Mesh, Mesh, Vector3)>();
        for (int cz = 0; cz < chunks; cz++)
            for (int cx = 0; cx < chunks; cx++)
            {
                var origin = new Vector3(MinX + cx * ChunkCells * Cell, 0f, MinZ + cz * ChunkCells * Cell);
                var hi = ChunkMesh($"Terrain_{cx}_{cz}", origin, 1, out float maxH);
                if (maxH < skipBelow) { Object.DestroyImmediate(hi); continue; }
                var lo = ChunkMesh($"Terrain_{cx}_{cz}_L1", origin, 4, out _);
                result.Add((hi, lo, origin));
            }
        return result;
    }

    Mesh ChunkMesh(string name, Vector3 origin, int stride, out float maxH)
    {
        int cellsN = ChunkCells / stride;
        int n = cellsN + 1;
        float cell = Cell * stride;
        var verts = new List<Vector3>(n * n + n * 4);
        var norms = new List<Vector3>(verts.Capacity);
        var uvs = new List<Vector2>(verts.Capacity);
        maxH = float.MinValue;
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                float x = origin.x + i * cell, z = origin.z + j * cell;
                float h = Height(x, z);
                maxH = Mathf.Max(maxH, h);
                verts.Add(new Vector3(i * cell, h, j * cell));
                norms.Add(Normal(x, z));
                uvs.Add(new Vector2((x - MinX) / Size, (z - MinZ) / Size));
            }
        var tris = new List<int>(cellsN * cellsN * 6 + n * 24);
        for (int j = 0; j < cellsN; j++)
            for (int i = 0; i < cellsN; i++)
            {
                int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }
        // 치마: 네 변을 따라 6m 아래로(양면이 보이도록 두 방향 모두)
        void Skirt(System.Func<int, int> edgeIndex)
        {
            int start = verts.Count;
            for (int k = 0; k < n; k++)
            {
                int e = edgeIndex(k);
                verts.Add(verts[e] + Vector3.down * 6f); norms.Add(norms[e]); uvs.Add(uvs[e]);
            }
            for (int k = 0; k < n - 1; k++)
            {
                int a = edgeIndex(k), b = edgeIndex(k + 1), c = start + k, d = start + k + 1;
                tris.Add(a); tris.Add(b); tris.Add(c); tris.Add(b); tris.Add(d); tris.Add(c);
                tris.Add(a); tris.Add(c); tris.Add(b); tris.Add(b); tris.Add(c); tris.Add(d);
            }
        }
        Skirt(k => k);
        Skirt(k => (n - 1) * n + k);
        Skirt(k => k * n);
        Skirt(k => k * n + n - 1);

        var mesh = new Mesh { name = name };
        mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    // ---------- 지역별 땅 색 ----------
    // 지역마다 풀색이 다르다(항구 = 마른 지중해 풀·모래, 호수 = 싱그러운 초록, 숲 = 짙은 이끼·낙엽, 고원 = 황금빛 초원, 절벽 = 회록색 황야).
    // 경계는 지역 중심 거리로 부드럽게 섞는다. 지역이 없으면(코스01) 기본 초록.
    public struct GroundPalette { public Color Dark, Light, Accent; public float AccentAmount; public Color Soil; }
    public readonly List<(Vector2 c, float r, GroundPalette pal)> Regions = new();

    public static GroundPalette PaletteFor(string region) => region switch
    {
        "harbor" => new GroundPalette { Dark = new Color(0.30f, 0.38f, 0.16f), Light = new Color(0.56f, 0.58f, 0.28f), Accent = new Color(0.74f, 0.66f, 0.42f), AccentAmount = 0.55f, Soil = new Color(0.62f, 0.52f, 0.36f) },
        "lake" => new GroundPalette { Dark = new Color(0.20f, 0.38f, 0.13f), Light = new Color(0.42f, 0.60f, 0.20f), Accent = new Color(0.55f, 0.62f, 0.24f), AccentAmount = 0.3f, Soil = new Color(0.42f, 0.34f, 0.22f) },
        "forest" => new GroundPalette { Dark = new Color(0.13f, 0.26f, 0.10f), Light = new Color(0.27f, 0.42f, 0.16f), Accent = new Color(0.40f, 0.30f, 0.18f), AccentAmount = 0.45f, Soil = new Color(0.32f, 0.24f, 0.16f) },
        "highland" => new GroundPalette { Dark = new Color(0.30f, 0.42f, 0.14f), Light = new Color(0.58f, 0.64f, 0.26f), Accent = new Color(0.78f, 0.66f, 0.30f), AccentAmount = 0.65f, Soil = new Color(0.50f, 0.40f, 0.26f) },
        "cliff" => new GroundPalette { Dark = new Color(0.24f, 0.34f, 0.17f), Light = new Color(0.46f, 0.54f, 0.32f), Accent = new Color(0.50f, 0.40f, 0.42f), AccentAmount = 0.35f, Soil = new Color(0.46f, 0.42f, 0.36f) },
        _ => new GroundPalette { Dark = new Color(0.20f, 0.38f, 0.13f), Light = new Color(0.44f, 0.60f, 0.21f), Accent = new Color(0.66f, 0.62f, 0.30f), AccentAmount = 0.4f, Soil = new Color(0.44f, 0.36f, 0.24f) },
    };

    GroundPalette PaletteAt(float x, float z)
    {
        if (Regions.Count == 0) return PaletteFor(null);
        var q = new Vector2(x, z);
        float wsum = 0f; var p = new GroundPalette();
        foreach (var (c, r, pal) in Regions)
        {
            float k = Vector2.Distance(q, c) / r;
            float w = 1f / (Mathf.Pow(k, 6f) + 0.02f);
            wsum += w;
            p.Dark += pal.Dark * w; p.Light += pal.Light * w; p.Accent += pal.Accent * w; p.Soil += pal.Soil * w;
            p.AccentAmount += pal.AccentAmount * w;
        }
        p.Dark /= wsum; p.Light /= wsum; p.Accent /= wsum; p.Soil /= wsum; p.AccentAmount /= wsum;
        return p;
    }

    // ---------- 색 지도(지형 전체 1장) ----------
    // 월드 위치의 지형 색(선형 색 공간, 정점 색으로 쓸 때 — 갓길이 지형과 이어지게)
    public Color GroundColorLinear(float x, float z) => ColorAt((x - MinX) / Size, (z - MinZ) / Size).linear;

    public Color ColorAt(float u, float v)
    {
        float x = MinX + u * Size, z = MinZ + v * Size;
        float h = Height(x, z);
        var n = Normal(x, z);
        float slope = 1f - n.y;
        var pal = PaletteAt(x, z);

        // 손그림풍: 큰 얼룩 + 작은 얼룩, 지역 강조색(마른 풀·낙엽·황금 초원) 무리, 흙이 드러난 자리
        float n1 = Fbm(x * 0.012f, z * 0.012f, 3);
        float n2 = Fbm(x * 0.0035f + 50f, z * 0.0035f + 50f, 3);
        float n3 = Fbm(x * 0.05f + 9f, z * 0.05f - 4f, 2);
        float n4 = Fbm(x * 0.03f - 20f, z * 0.03f + 13f, 3);
        var c = Color.Lerp(pal.Dark, pal.Light, Mathf.SmoothStep(0.3f, 0.7f, n1) * 0.75f + n3 * 0.25f);
        c = Color.Lerp(c, pal.Accent, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.75f, n2)) * pal.AccentAmount);
        c = Color.Lerp(c, pal.Soil, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.68f, 0.8f, n4)) * 0.45f);
        // 높은 곳은 조금 더 시원한 녹색
        c = Color.Lerp(c, new Color(0.30f, 0.44f, 0.28f), Mathf.InverseLerp(40f, 180f, h) * 0.45f);

        // 바위: 경사가 급한 곳(절벽·협곡 벽·봉우리·도로 옆 둑), 층 무늬
        var rock = Color.Lerp(new Color(0.46f, 0.43f, 0.40f), new Color(0.64f, 0.60f, 0.54f), n1);
        rock = Color.Lerp(rock, new Color(0.56f, 0.46f, 0.38f), Mathf.PerlinNoise(h * 0.15f, x * 0.002f) * 0.5f);
        float rockW = Mathf.InverseLerp(0.2f, 0.36f, slope);
        c = Color.Lerp(c, rock, Mathf.Clamp01(rockW));

        // 바다 밑(절벽 아래)은 밝은 모래
        if (h < -12f) c = Color.Lerp(c, new Color(0.80f, 0.74f, 0.55f), Mathf.InverseLerp(-12f, -24f, h));

        // 호숫가 모래, 물속
        float lake = LakeMask(x, z);
        if (lake > 0.01f)
        {
            var sand = new Color(0.82f, 0.74f, 0.55f);
            var bed = new Color(0.52f, 0.52f, 0.38f);
            c = Color.Lerp(c, sand, Mathf.Clamp01(lake * 2.2f) * (1f - Mathf.InverseLerp(0.4f, 1.5f, h)));
            c = Color.Lerp(c, bed, Mathf.InverseLerp(-0.8f, -3f, h));
        }
        c.a = 1f;
        return c;
    }
}
