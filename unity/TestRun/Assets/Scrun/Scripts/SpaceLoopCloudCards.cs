using UnityEngine;

namespace Scrun
{
    // 구름 카드(Scrun/CloudCard) 그리는 순서 맞추기. 반투명이라 먼 카드부터 그려야 겹친 곳이 깨지지 않는다.
    // 카드는 메시 하나(카드 1장 = 정점 4개, 정점 위치 = 카드 중심)라서 삼각형 순서만 다시 쓴다.
    // 카메라가 resortDistance만큼 움직였을 때만 정렬한다(카드 수백 장이라 가볍다).
    [RequireComponent(typeof(MeshFilter))]
    public class SpaceLoopCloudCards : MonoBehaviour
    {
        [SerializeField] Camera mainCamera;
        [SerializeField, Min(0.5f)] float resortDistance = 4f;

        Mesh mesh;
        Vector3[] centers;
        float[] keys;
        int[] order;
        int[] tris;
        Vector3 lastPos;
        bool sorted;

        void OnEnable()
        {
            var mf = GetComponent<MeshFilter>();
            if (mesh == null)
            {
                if (mf.sharedMesh == null) { enabled = false; return; }
                mesh = Instantiate(mf.sharedMesh);   // 원본 자산은 건드리지 않는다
                mesh.name = mf.sharedMesh.name + " (sorted)";
                mesh.MarkDynamic();
                mf.sharedMesh = mesh;
                var v = mesh.vertices;
                int n = v.Length / 4;
                centers = new Vector3[n]; keys = new float[n]; order = new int[n]; tris = new int[n * 6];
                for (int i = 0; i < n; i++) centers[i] = v[i * 4];
            }
            sorted = false;
        }

        void OnDestroy()
        {
            if (mesh) Destroy(mesh);
        }

        void LateUpdate()
        {
            var cam = mainCamera ? mainCamera : Camera.main;
            if (!cam || mesh == null) return;
            Vector3 p = transform.InverseTransformPoint(cam.transform.position);
            if (sorted && (p - lastPos).sqrMagnitude < resortDistance * resortDistance) return;
            lastPos = p; sorted = true;
            int n = centers.Length;
            for (int i = 0; i < n; i++) { keys[i] = -(centers[i] - p).sqrMagnitude; order[i] = i; }
            System.Array.Sort(keys, order);   // 먼 것부터
            for (int i = 0; i < n; i++)
            {
                int b = order[i] * 4, t = i * 6;
                tris[t] = b; tris[t + 1] = b + 2; tris[t + 2] = b + 1;
                tris[t + 3] = b; tris[t + 4] = b + 3; tris[t + 5] = b + 2;
            }
            mesh.SetTriangles(tris, 0, false);
        }
    }
}
