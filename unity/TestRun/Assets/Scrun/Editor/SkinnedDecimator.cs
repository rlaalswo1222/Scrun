using System.Collections.Generic;
using UnityEngine;

// 스킨 메쉬 단순화(격자 군집): 칸마다 정점 하나로 합치고, 그 칸에서 중심에 가장 가까운 정점의 뼈 가중치를 쓴다.
// 멀리 보이는 봇·고스트용 저해상도 메쉬. 뼈·바인드포즈는 원본 그대로라 같은 SkinnedMeshRenderer에서 바꿔 끼울 수 있다.
public static class SkinnedDecimator
{
    public static Mesh Decimate(Mesh src, float cell, string name)
    {
        var v = src.vertices; var n = src.normals; var w = src.boneWeights;
        var keyOf = new int[v.Length];
        var clusters = new Dictionary<Vector3Int, int>();
        var sumP = new List<Vector3>(); var sumN = new List<Vector3>(); var count = new List<int>();
        for (int i = 0; i < v.Length; i++)
        {
            var k = new Vector3Int(Mathf.FloorToInt(v[i].x / cell), Mathf.FloorToInt(v[i].y / cell), Mathf.FloorToInt(v[i].z / cell));
            if (!clusters.TryGetValue(k, out int c)) { c = sumP.Count; clusters[k] = c; sumP.Add(Vector3.zero); sumN.Add(Vector3.zero); count.Add(0); }
            keyOf[i] = c; sumP[c] += v[i]; sumN[c] += n.Length > 0 ? n[i] : Vector3.up; count[c]++;
        }
        int m = sumP.Count;
        var pos = new Vector3[m]; var nor = new Vector3[m];
        var rep = new int[m]; var best = new float[m];
        for (int c = 0; c < m; c++) { pos[c] = sumP[c] / count[c]; nor[c] = sumN[c].normalized; best[c] = float.MaxValue; }
        for (int i = 0; i < v.Length; i++)
        {
            int c = keyOf[i]; float d = (v[i] - pos[c]).sqrMagnitude;
            if (d < best[c]) { best[c] = d; rep[c] = i; }
        }
        var weights = new BoneWeight[m];
        for (int c = 0; c < m; c++) weights[c] = w.Length > 0 ? w[rep[c]] : default;

        var mesh = new Mesh { name = name, indexFormat = m > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
        mesh.vertices = pos;
        mesh.normals = nor;
        mesh.uv = new Vector2[m];
        mesh.boneWeights = weights;
        mesh.bindposes = src.bindposes;
        mesh.subMeshCount = src.subMeshCount;
        for (int s = 0; s < src.subMeshCount; s++)
        {
            var t = src.GetTriangles(s);
            var outT = new List<int>(t.Length / 4);
            var seen = new HashSet<(int, int, int)>();
            for (int i = 0; i < t.Length; i += 3)
            {
                int a = keyOf[t[i]], b = keyOf[t[i + 1]], c = keyOf[t[i + 2]];
                if (a == b || b == c || a == c) continue;
                // 같은 삼각형 중복 제거(회전 순서 무시)
                int lo = Mathf.Min(a, Mathf.Min(b, c));
                var key = lo == a ? (a, b, c) : lo == b ? (b, c, a) : (c, a, b);
                if (!seen.Add(key)) continue;
                outT.Add(a); outT.Add(b); outT.Add(c);
            }
            mesh.SetTriangles(outT, s);
        }
        mesh.RecalculateBounds();
        return mesh;
    }
}
