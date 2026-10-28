using UnityEngine;

namespace Scrun
{
    // 원형 트랙. 거리(m) -> 위치/진행 방향을 준다.
    // 나중에 실제 코스(스플라인)로 바꿀 때도 같은 두 함수만 맞추면 된다.
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class CircleTrack : Track
    {
        [SerializeField] float lapLength = 400f;   // 한 바퀴 길이(m), 레인 중앙 기준
        [SerializeField] float width = 4f;
        [SerializeField] int segments = 128;

        public override float LapLength => lapLength;
        float Radius => lapLength / (2f * Mathf.PI);

        // 메쉬는 씬에 저장하지 않고 켜질 때마다 다시 만든다
        void OnEnable() => BuildMesh();
        void OnValidate()
        {
            if (lapLength < 10f) lapLength = 10f;
            if (segments < 8) segments = 8;
        }

        // 반시계 방향으로 진행, 거리 0 = +X 방향 지점
        public override Vector3 GetPoint(float distance)
        {
            float a = distance / Radius;
            return transform.TransformPoint(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Radius);
        }

        public override Vector3 GetForward(float distance)
        {
            float a = distance / Radius;
            return transform.TransformDirection(new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)));
        }

        public void BuildMesh()
        {
            float rIn = Radius - width / 2f, rOut = Radius + width / 2f;
            var verts = new Vector3[(segments + 1) * 2];
            var uvs = new Vector2[verts.Length];
            var tris = new int[segments * 6];

            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float a = t * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                verts[i * 2] = dir * rIn;
                verts[i * 2 + 1] = dir * rOut;
                // u: 폭 방향 0~1, v: 진행 거리(m) → 텍스처 1장 = 길이 1m
                uvs[i * 2] = new Vector2(0f, t * lapLength);
                uvs[i * 2 + 1] = new Vector2(1f, t * lapLength);
            }
            for (int i = 0; i < segments; i++)
            {
                int v = i * 2, k = i * 6;
                // 위에서 보이도록 감는 순서
                tris[k] = v; tris[k + 1] = v + 2; tris[k + 2] = v + 1;
                tris[k + 3] = v + 1; tris[k + 4] = v + 2; tris[k + 5] = v + 3;
            }

            var mf = GetComponent<MeshFilter>();
            if (mf.sharedMesh && mf.sharedMesh.name == "CircleTrack")
            {
                if (Application.isPlaying) Destroy(mf.sharedMesh);
                else DestroyImmediate(mf.sharedMesh);
            }

            var mesh = new Mesh { name = "CircleTrack", hideFlags = HideFlags.DontSave };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mf.sharedMesh = mesh;
        }
    }
}
