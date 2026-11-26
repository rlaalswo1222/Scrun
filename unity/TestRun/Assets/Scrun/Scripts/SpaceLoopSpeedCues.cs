using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Scrun
{
    // 우주 루프 길 가장자리의 촘촘한 속도 단서(계획 0083 1-14, 11/27 상준 "이동하는 거리도 확확 바뀌는 느낌").
    //  - LED 마디: 1m마다 0.6m, 길 양쪽 가장자리(sl_track.py build_led와 같은 폭 3.62–3.78m)
    //  - 경계봉: 2m마다, 길 밖 4.3m, 높이 0.62m, 끝 10cm가 LED 재질로 빛남
    // 원래 맵은 LED 1.3m 마디가 6m마다, 가드레일 기둥이 3.6m마다라 14 km/h에서 1초에 기둥 1개쯤만 지나갔다.
    // 실행할 때 코스를 따라 메쉬를 만든다(장면 파일에 굽지 않음). 100m씩 한 덩어리라 보이는 덩어리만 그려진다.
    // 위치는 맵 위 거리(Track.GetPoint)라 세계 속도 배율(Track.WorldScale)과 상관없이 맵에 붙어 있다.
    public class SpaceLoopSpeedCues : MonoBehaviour
    {
        [SerializeField] Track track;
        [SerializeField] Material ledMaterial;    // SL_LED
        [SerializeField] Material postMaterial;   // SL_Metal

        const float Chunk = 100f;
        const float LedStep = 1.0f, LedLen = 0.6f, LedIn = 3.62f, LedOut = 3.78f;
        // LED 높이 3.2cm: 길 면이 코스 기준선보다 2cm 위(RunnerController.groundOffset)라 1.2cm면 길 아래에 묻혔다(11/27)
        const float LedHeight = 0.032f;
        const float PostStep = 2.0f, PostD = 4.3f, PostW = 0.07f, PostH = 0.62f, CapH = 0.1f;

        readonly List<Mesh> meshes = new List<Mesh>();
        readonly Vector3[] corner = new Vector3[4]; // Box가 기둥마다 새로 만들지 않게
        GameObject root;

        void Start()
        {
            if (!track) track = FindTrack();
            if (!track || !ledMaterial || !postMaterial)
            {
                Debug.LogWarning("[속도 단서] 코스나 재질이 없어 안 만듦", this);
                return;
            }
            Build();
        }

        Track FindTrack()
        {
            foreach (var t in FindObjectsByType<SplineTrack>())
                if (t.gameObject.scene == gameObject.scene) return t;
            return null;
        }

        void Build()
        {
            float t0 = Time.realtimeSinceStartup;
            root = new GameObject("SpeedCues_Dense");
            SceneManager.MoveGameObjectToScene(root, gameObject.scene); // 앱에서 맵 장면을 덧붙여 열어도 맵과 같이 내려가게
            float lap = track.LapLength;
            int chunks = Mathf.CeilToInt(lap / Chunk), verts = 0;
            for (int c = 0; c < chunks; c++)
            {
                float s0 = c * Chunk, s1 = Mathf.Min(lap, s0 + Chunk);
                var chunk = new GameObject("Cues_" + ((int)s0).ToString("0000"));
                chunk.transform.SetParent(root.transform, false);
                verts += Make(chunk, "LED", ledMaterial, b =>
                {
                    for (float s = s0 + 0.2f; s < s1; s += LedStep)
                    {
                        Quad(b, s, s + LedLen, -LedOut, -LedIn, LedHeight);
                        Quad(b, s, s + LedLen, LedIn, LedOut, LedHeight);
                    }
                });
                verts += Make(chunk, "Post", postMaterial, b =>
                {
                    for (float s = s0 + 1f; s < s1; s += PostStep) { Box(b, s, -PostD, PostW, 0f, PostH); Box(b, s, PostD, PostW, 0f, PostH); }
                });
                verts += Make(chunk, "PostCap", ledMaterial, b =>
                {
                    for (float s = s0 + 1f; s < s1; s += PostStep)
                    {
                        Box(b, s, -PostD, PostW * 1.15f, PostH, PostH + CapH);
                        Box(b, s, PostD, PostW * 1.15f, PostH, PostH + CapH);
                    }
                });
            }
            Debug.Log($"[속도 단서] 덩어리 {chunks}개 × 3, 정점 {verts}, 한 바퀴 {lap:0}m, {(Time.realtimeSinceStartup - t0) * 1000f:0}ms");
        }

        void OnDestroy()
        {
            foreach (var m in meshes) if (m) Destroy(m);
            meshes.Clear();
            if (root) Destroy(root);
        }

        sealed class B
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<int> t = new List<int>();
        }

        int Make(GameObject parent, string name, Material mat, System.Action<B> fill)
        {
            var b = new B();
            fill(b);
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var m = new Mesh { name = parent.name + "_" + name };
            if (b.v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(b.v);
            m.SetNormals(b.n);
            m.SetTriangles(b.t, 0);
            m.RecalculateBounds();
            m.UploadMeshData(true); // 다시 안 고침 → CPU 쪽 사본 버림
            meshes.Add(m);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return b.v.Count;
        }

        Vector3 P(float s, float d, float h)
        {
            s = Mathf.Repeat(s, track.LapLength);
            return track.GetPoint(s) + track.GetRight(s) * d + Vector3.up * h;
        }

        // 길 위 평평한 사각형(s0–s1, 가로 d0 < d1). 위에서 볼 때 시계 방향 = Unity 앞면
        void Quad(B b, float s0, float s1, float d0, float d1, float h)
        {
            int i = b.v.Count;
            b.v.Add(P(s0, d0, h)); b.v.Add(P(s0, d1, h)); b.v.Add(P(s1, d1, h)); b.v.Add(P(s1, d0, h));
            for (int k = 0; k < 4; k++) b.n.Add(Vector3.up);
            b.t.Add(i); b.t.Add(i + 2); b.t.Add(i + 1);
            b.t.Add(i); b.t.Add(i + 3); b.t.Add(i + 2);
        }

        // 길을 따라 세운 각기둥(밑면 없음, 면마다 정점 따로 = 각진 음영)
        void Box(B b, float s, float d, float w, float h0, float h1)
        {
            float sm = Mathf.Repeat(s, track.LapLength);
            var f = track.GetForward(sm);
            var rgt = track.GetRight(sm);
            var c = track.GetPoint(sm) + rgt * d;
            var up = Vector3.up;
            corner[0] = -f - rgt; corner[1] = f - rgt; corner[2] = f + rgt; corner[3] = -f + rgt;
            for (int k = 0; k < 4; k++)
            {
                var a = c + corner[k] * (w * 0.5f);
                var e = c + corner[(k + 1) % 4] * (w * 0.5f);
                var nrm = Vector3.Cross(e - a, up).normalized;
                int i = b.v.Count;
                b.v.Add(a + up * h0); b.v.Add(e + up * h0); b.v.Add(e + up * h1); b.v.Add(a + up * h1);
                for (int q = 0; q < 4; q++) b.n.Add(nrm);
                b.t.Add(i); b.t.Add(i + 1); b.t.Add(i + 2);
                b.t.Add(i); b.t.Add(i + 2); b.t.Add(i + 3);   // 바깥에서 볼 때 시계 방향(Unity 앞면)
            }
            int j = b.v.Count;
            for (int k = 0; k < 4; k++) { b.v.Add(c + corner[k] * (w * 0.5f) + up * h1); b.n.Add(up); }
            b.t.Add(j); b.t.Add(j + 1); b.t.Add(j + 2);
            b.t.Add(j); b.t.Add(j + 2); b.t.Add(j + 3);
        }
    }
}
