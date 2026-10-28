using System.Collections.Generic;
using Scrun;
using UnityEngine;

// 코스 곡선을 따라 도로 단면을 밀어내 메쉬를 만든다.
// 단면(오른쪽 기준, 왼쪽은 대칭):
//  연석 있음(항구·광장): 아스팔트 | 발광선 | 연석(안쪽 면·윗면·바깥 경사) | 갓길
//  연석 없음(그 밖, Zwift식): 아스팔트 | 흙 가장자리(살짝 낮아짐) | 흙→풀 갓길(바깥은 지형 색과 같아 경계가 안 보임)
//  갓길 색은 정점 색(ground 함수 = 그 자리 지형 색)으로 칠한다.
// 길이 방향으로 chunkLength마다 메쉬를 나눠 화면 밖 구간은 그리지 않게 한다(절두체 컬링).
public static class CourseRoadBuilder
{
    public const float RoadWidth = 8f;
    public const float RoadY = 0.02f;           // 지면(0) 위로 살짝
    public const float ShoulderOuter = RoadWidth / 2f + 2f;

    public enum Part { Asphalt, Glow, Curb, Shoulder }
    public const int PartCount = 4;

    // 단면의 한 면: 가로 위치 x(오른쪽 +), 높이 y. A→B 방향의 왼쪽 법선이 바깥(보이는 쪽)
    struct Strip
    {
        public Vector2 A, B;
        public Part Part;
        public float U0, U1;      // 가로 UV
        public float VScale;      // 세로 UV = 거리 / VScale
        public Strip(Vector2 a, Vector2 b, Part part, float u0, float u1, float vScale)
        { A = a; B = b; Part = part; U0 = u0; U1 = u1; VScale = vScale; }
    }

    static List<Strip> Profile(bool curb)
    {
        float h = RoadWidth / 2f;
        List<Strip> right;
        if (curb)
        {
            const float curbTop = 0.14f, curbW = 0.3f, curbSlope = 0.1f;
            right = new List<Strip>
            {
                new(new Vector2(h - 0.18f, RoadY + 0.004f), new Vector2(h - 0.06f, RoadY + 0.004f), Part.Glow, 0f, 1f, 1f),
                new(new Vector2(h, RoadY), new Vector2(h, curbTop), Part.Curb, 0f, curbTop - RoadY, 1f),
                new(new Vector2(h, curbTop), new Vector2(h + curbW, curbTop), Part.Curb, 0.12f, 0.12f + curbW, 1f),
                new(new Vector2(h + curbW, curbTop), new Vector2(h + curbW + curbSlope, 0.005f), Part.Curb, 0.42f, 0.6f, 1f),
                new(new Vector2(h + curbW + curbSlope, 0.005f), new Vector2(ShoulderOuter, 0.005f), Part.Shoulder, 0f, 0.8f, 2f),
            };
        }
        else
        {
            // 흙 가장자리 → 흙과 풀이 섞인 띠 → 지형 속으로(바깥 끝은 지면보다 3cm 아래라 지형이 덮음)
            right = new List<Strip>
            {
                new(new Vector2(h, RoadY), new Vector2(h + 0.15f, RoadY - 0.012f), Part.Shoulder, 0f, 0.15f, 2f),
                new(new Vector2(h + 0.15f, RoadY - 0.012f), new Vector2(h + 0.9f, 0.008f), Part.Shoulder, 0.15f, 0.9f, 2f),
                new(new Vector2(h + 0.9f, 0.008f), new Vector2(ShoulderOuter + 0.8f, -0.03f), Part.Shoulder, 0.9f, 2.8f, 2f),
            };
        }
        var all = new List<Strip>
        {
            // 아스팔트 한 장(왼→오), 텍스처 1장 = 폭 8m × 길이 6m
            new(new Vector2(-h, RoadY), new Vector2(h, RoadY), Part.Asphalt, 0f, 1f, 6f),
        };
        all.AddRange(right);
        // 왼쪽 = x 반전 + 순서 반전(법선 방향 유지)
        foreach (var s in right)
            all.Add(new Strip(new Vector2(-s.B.x, s.B.y), new Vector2(-s.A.x, s.A.y), s.Part, s.U1, s.U0, s.VScale));
        return all;
    }

    // 결과: 청크별 (메쉬, 청크 원점). 메쉬 정점은 원점 기준 좌표, 서브메쉬 = Part 순서.
    // [from, to] 구간만 만든다(교차로 앞에서 도로를 끊을 때). to < 0이면 끝까지.
    // ground: 월드 위치 → 그 자리 지형 색(선형). 있으면 갓길을 흙→지형 색으로 칠함
    public static List<(Mesh mesh, Vector3 origin)> Build(IPath path, string prefix, float from = 0f, float to = -1f,
                                                          bool curb = true, System.Func<Vector3, Color> ground = null)
        => Extrude(Profile(curb), PartCount, path, prefix, from, to, 200f, 2f, ground);   // 2m 간격(곡선도 충분히 매끈, 삼각형 절반)

    // 갓길 색: 아스팔트 쪽은 흙(지형 색을 어둡고 누렇게), 바깥으로 갈수록 지형 색 그대로
    static Color ShoulderColor(Color groundCol, float x, float noise)
    {
        float h = RoadWidth / 2f;
        var dirt = Color.Lerp(groundCol * 0.72f, new Color(0.30f, 0.24f, 0.16f), 0.55f);
        float t = Mathf.InverseLerp(h + 0.1f, h + 2.2f + 1.2f * noise, Mathf.Abs(x));
        var c = Color.Lerp(dirt, groundCol, Mathf.SmoothStep(0f, 1f, t));
        c.a = 1f;
        return c;
    }

    // 다리 바닥판: 도로 아래 두께 1.2m 판 + 양옆 난간벽(높이 1m). 서브메쉬 1개(콘크리트)
    public static List<(Mesh mesh, Vector3 origin)> BuildDeck(IPath path, string prefix, float from, float to)
    {
        float h = ShoulderOuter + 0.2f;   // 갓길 바깥까지 덮음
        const float bottom = -1.4f, wallTop = 0.55f, wallW = 0.3f;   // 난간을 낮게 → 달리면서 협곡이 보임
        var right = new List<Strip>
        {
            new(new Vector2(h, 0.005f), new Vector2(h, wallTop), Part.Asphalt, 0f, 1f, 1f),                 // 난간 안쪽 면
            new(new Vector2(h, wallTop), new Vector2(h + wallW, wallTop), Part.Asphalt, 1f, 1.3f, 1f),       // 난간 윗면
            new(new Vector2(h + wallW, wallTop), new Vector2(h + wallW, bottom), Part.Asphalt, 1.3f, 3.8f, 1f), // 바깥 면
        };
        var profile = new List<Strip>
        {
            // 바닥판 아랫면(아래에서 보임): 오른쪽 → 왼쪽
            new(new Vector2(h + wallW, bottom), new Vector2(-h - wallW, bottom), Part.Asphalt, 0f, (h + wallW) * 2f, 1f),
        };
        profile.AddRange(right);
        foreach (var st in right)
            profile.Add(new Strip(new Vector2(-st.B.x, st.B.y), new Vector2(-st.A.x, st.A.y), st.Part, st.U1, st.U0, st.VScale));
        return Extrude(profile, 1, path, prefix, from, to, 200f, 1f, null);
    }

    static List<(Mesh mesh, Vector3 origin)> Extrude(List<Strip> profile, int partCount, IPath path, string prefix,
                                                     float from, float to, float chunkLength, float step, System.Func<Vector3, Color> ground)
    {
        if (to < 0f) to = path.Length;
        float span = Mathf.Max(0.5f, to - from);
        int chunks = Mathf.CeilToInt(span / chunkLength);
        var meshes = new List<(Mesh, Vector3)>();

        for (int c = 0; c < chunks; c++)
        {
            float d0 = from + c * chunkLength;
            float d1 = Mathf.Min(to, d0 + chunkLength);
            int rows = Mathf.Max(2, Mathf.CeilToInt((d1 - d0) / step) + 1);

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var cols = new List<Color>();
            var tris = new List<int>[partCount];
            for (int p = 0; p < partCount; p++) tris[p] = new List<int>();

            // 청크 중심을 원점으로(정밀도·바운딩 박스)
            Vector3 origin = path.GetPoint((d0 + d1) / 2f);
            origin.y = 0f;

            foreach (var s in profile)
            {
                int baseIndex = verts.Count;
                for (int r = 0; r < rows; r++)
                {
                    float d = Mathf.Lerp(d0, d1, (float)r / (rows - 1));
                    Vector3 center = path.GetPoint(d);   // 높이 포함(오르막·내리막)
                    Vector3 right = path.GetRight(d);
                    var pa = center + right * s.A.x + Vector3.up * s.A.y;
                    var pb = center + right * s.B.x + Vector3.up * s.B.y;
                    verts.Add(pa - origin);
                    verts.Add(pb - origin);
                    float v = d / s.VScale;
                    uvs.Add(new Vector2(s.U0, v));
                    uvs.Add(new Vector2(s.U1, v));
                    if (ground != null && s.Part == Part.Shoulder)
                    {
                        float nz = Mathf.PerlinNoise(d * 0.09f, s.A.x > 0 ? 3f : 7f);
                        cols.Add(ShoulderColor(ground(pa), s.A.x, nz));
                        cols.Add(ShoulderColor(ground(pb), s.B.x, nz));
                    }
                    else { cols.Add(Color.white); cols.Add(Color.white); }
                }
                var t = tris[(int)s.Part];
                for (int r = 0; r < rows - 1; r++)
                {
                    int a0 = baseIndex + r * 2, b0 = a0 + 1, a1 = a0 + 2, b1 = a0 + 3;
                    // 위에서 볼 때 시계방향 = 앞면 (A=왼쪽, 진행 방향=위)
                    t.Add(a0); t.Add(a1); t.Add(b0);
                    t.Add(a1); t.Add(b1); t.Add(b0);
                }
            }

            var mesh = new Mesh { name = $"{prefix}_{c:00}" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            if (ground != null) mesh.SetColors(cols);
            mesh.subMeshCount = partCount;
            for (int p = 0; p < partCount; p++) mesh.SetTriangles(tris[p], p);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            meshes.Add((mesh, origin));
        }
        return meshes;
    }

    // 교차로: 차선 없는 아스팔트 원판(도로보다 3mm 위)으로 겹치는 도로 끝을 덮는다. UV = 월드 m / 8
    public static Mesh BuildJunction(string name, float radius, int segments = 48)
    {
        var v = new List<Vector3> { new(0f, RoadY + 0.003f, 0f) };
        var uv = new List<Vector2> { Vector2.zero };
        var t = new List<int>();
        for (int i = 0; i <= segments; i++)
        {
            float a = (float)i / segments * Mathf.PI * 2f;
            var p = new Vector3(Mathf.Cos(a) * radius, RoadY + 0.003f, Mathf.Sin(a) * radius);
            v.Add(p); uv.Add(new Vector2(p.x / 8f, p.z / 8f));
            if (i > 0) { t.Add(0); t.Add(i + 1); t.Add(i); }
        }
        var mesh = new Mesh { name = name };
        mesh.SetVertices(v); mesh.SetUVs(0, uv); mesh.SetTriangles(t, 0);
        mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
        return mesh;
    }
}
