using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Scrun
{
    // 풀·덤불·나무·바위처럼 수십만 개를 깔아야 하는 것을 게임오브젝트 없이 GPU 인스턴싱으로 그린다.
    // 배치는 칸(Cell) 단위로 묶어 두고, 매 프레임 카메라 시야·거리로 칸을 고른다.
    // LOD는 칸 단위로 통째로 바꾸지 않는다(무더기가 한꺼번에 모양을 바꿔 꿈틀거려 보임). 칸이 걸친 LOD를 모두 그리고,
    // 셰이더가 개체마다 카메라 거리로 자기 구간만 남긴다. 경계에서는 두 LOD를 디더로 나눠 그려 서서히 바뀐다(풀은 작아지며 사라짐).
    // 개체 데이터(위치·크기·회전)는 씬에 넣으면 너무 커서 .bytes 파일 하나에 float로 담는다.
    [ExecuteAlways]
    public class InstancedScatter : MonoBehaviour
    {
        [Serializable]
        public class Proto
        {
            public string name;
            public Mesh[] lods;                // 0 = 가까이
            public float[] lodDistances;       // lods[i]를 쓰는 최대 거리. 마지막보다 멀면 안 그림
            public Material material;
            public bool castShadows;           // LOD 0만 그림자를 드리움(LOD 0 거리를 그림자 거리보다 길게 잡아 그림자가 도중에 꺼지지 않게)
            public bool thinnable;             // 품질이 낮으면 개수를 줄여도 되는 것(풀)
        }

        // LOD 경계의 교차 폭(m): 경계 거리의 10%, 최소 3m(넓으면 디더 중인 나무가 많아 화면이 자글거려 보임)
        static float Band(float boundary) => Mathf.Max(3f, boundary * 0.1f);

        [Serializable]
        public class Cell
        {
            public int proto;
            public Bounds bounds;
            public int start, count;   // data 안의 개체 범위
            [NonSerialized] public Matrix4x4[] matrices;
        }

        public const int MaxPerCell = 1023;       // RenderMeshInstanced 한 번에 그릴 수 있는 최대 개수
        public const int FloatsPerInstance = 5;   // x, y, z, 크기, 회전(도)

        // 품질 단계(ScrunQuality가 설정): 그리는 거리 배율, 풀 밀도(0~1)
        public static float GlobalDistanceScale = 1f;
        public static bool ShadowsEnabled = true;   // 저사양은 식생 그림자 끔
        static float grassDensity = 1f;
        public static float GrassDensity
        {
            get => grassDensity;
            set { if (Mathf.Approximately(grassDensity, value)) return; grassDensity = value; densityVersion++; }
        }
        static int densityVersion;

        [SerializeField] List<Proto> protos = new();
        [SerializeField] List<Cell> cells = new();
        [SerializeField] TextAsset data;

        readonly Plane[] planes = new Plane[6];
        float[] values;
        int builtVersion = -1;

        // 칸 묶음(256m): 묶음이 멀거나 화면 밖이면 안의 칸은 검사하지 않음.
        // 묶음 전체가 어떤 종류의 LOD 1 끝보다 멀면 그 종류는 칸마다 따로 그리지 않고 묶음 단위로 합쳐 한 번에 그림
        // (먼 숲은 64m 칸마다 나무 몇 그루씩이라 칸별로 그리면 그리기 호출만 수백 번)
        const float GroupSize = 256f;
        class Far { public Bounds bounds; public Matrix4x4[] matrices; }
        class Group
        {
            public Bounds bounds; public float maxDistance; public List<int> cells = new();
            public Dictionary<int, List<int>> byProto = new();
            public Dictionary<int, List<Far>> far;   // 처음 멀리서 그릴 때 만듦
        }
        List<Group> groups;

        public void Set(List<Proto> p, List<Cell> c, TextAsset d)
        {
            protos = p;
            cells = c;
            data = d;
            Invalidate();
        }

        void Invalidate()
        {
            values = null;
            groups = null;
            foreach (var cell in cells) cell.matrices = null;
        }

        public int InstanceCount
        {
            get { int n = 0; foreach (var c in cells) n += c.count; return n; }
        }

        void OnEnable() => Invalidate();

        void BuildGroups()
        {
            var map = new Dictionary<Vector2Int, Group>();
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                var key = new Vector2Int(Mathf.FloorToInt(c.bounds.center.x / GroupSize), Mathf.FloorToInt(c.bounds.center.z / GroupSize));
                if (!map.TryGetValue(key, out var g)) map[key] = g = new Group { bounds = c.bounds };
                g.bounds.Encapsulate(c.bounds);
                var d = protos[c.proto].lodDistances;
                g.maxDistance = Mathf.Max(g.maxDistance, d[d.Length - 1]);
                g.cells.Add(i);
                if (!g.byProto.TryGetValue(c.proto, out var pl)) g.byProto[c.proto] = pl = new List<int>();
                pl.Add(i);
            }
            groups = new List<Group>(map.Values);
        }

        Matrix4x4[] BuildMatrices(Cell c, bool thin)
        {
            if (values == null)
            {
                var bytes = data.bytes;
                values = new float[bytes.Length / 4];
                Buffer.BlockCopy(bytes, 0, values, 0, values.Length * 4);
            }
            var list = new List<Matrix4x4>(c.count);
            for (int i = 0; i < c.count; i++)
            {
                // 밀도를 줄일 때는 개체 번호의 해시로 골라 고르게 솎아냄
                if (thin && grassDensity < 1f && ((uint)((c.start + i) * 2654435761u) % 1000u) >= grassDensity * 1000f) continue;
                int k = (c.start + i) * FloatsPerInstance;
                list.Add(Matrix4x4.TRS(new Vector3(values[k], values[k + 1], values[k + 2]),
                                       Quaternion.Euler(0f, values[k + 4], 0f), Vector3.one * values[k + 3]));
            }
            return list.ToArray();
        }

        // (proto, lod)별 거리 구간을 담은 속성 블록. 거리 배율이 바뀌면 다시 만든다
        MaterialPropertyBlock[][] blocks;
        Vector4[][] ranges;
        float blocksScale = -1f;
        static readonly int LodFadeId = Shader.PropertyToID("_ScrunLodFade");

        void BuildBlocks(float scale)
        {
            blocks = new MaterialPropertyBlock[protos.Count][];
            ranges = new Vector4[protos.Count][];
            for (int i = 0; i < protos.Count; i++)
            {
                var d = protos[i].lodDistances;
                bool shrink = protos[i].thinnable;
                blocks[i] = new MaterialPropertyBlock[d.Length];
                ranges[i] = new Vector4[d.Length];
                for (int l = 0; l < d.Length; l++)
                {
                    float end = d[l] * scale;
                    Vector4 r;
                    if (shrink)
                        r = new Vector4(-1f, -1f, end * 0.6f, end);   // 풀: 60%부터 줄어들어 끝에서 사라짐
                    else
                    {
                        float bIn = l == 0 ? 0f : Band(d[l - 1] * scale), bOut = Band(end);
                        float start = l == 0 ? -1f : d[l - 1] * scale;
                        r = l == 0 ? new Vector4(-1f, -1f, end - bOut / 2f, end + bOut / 2f)
                                   : new Vector4(start - bIn / 2f, start + bIn / 2f, end - bOut / 2f, end + bOut / 2f);
                    }
                    ranges[i][l] = r;
                    var mpb = new MaterialPropertyBlock();
                    mpb.SetVector(LodFadeId, r);
                    blocks[i][l] = mpb;
                }
            }
            blocksScale = scale;
        }

        void Update()
        {
            var cam = Camera.main;
#if UNITY_EDITOR
            if (!Application.isPlaying && UnityEditor.SceneView.lastActiveSceneView)
                cam = UnityEditor.SceneView.lastActiveSceneView.camera;
#endif
            if (!cam || !data || protos.Count == 0) return;
            if (builtVersion != densityVersion)
            {
                foreach (var c in cells) c.matrices = null;
                if (groups != null) foreach (var g in groups) g.far = null;
                builtVersion = densityVersion;
            }

            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            Vector3 eye = cam.transform.position;
            float scale = GlobalDistanceScale;
            if (blocks == null || blocks.Length != protos.Count || !Mathf.Approximately(blocksScale, scale)) BuildBlocks(scale);

            if (groups == null) BuildGroups();
            foreach (var g in groups)
            {
                float reach = g.maxDistance * scale * 1.1f;
                float gMin2 = g.bounds.SqrDistance(eye);
                if (gMin2 > reach * reach) continue;
                if (!GeometryUtility.TestPlanesAABB(planes, g.bounds)) continue;
                float gMin = Mathf.Sqrt(gMin2);
                foreach (var kv in g.byProto)
                {
                    var r = ranges[kv.Key];
                    float nearEnd = r[Mathf.Min(1, r.Length - 1)].w;   // LOD 1(없으면 0)이 끝나는 거리
                    if (gMin > nearEnd && r.Length > 1) DrawFar(g, kv.Key, eye);
                    else foreach (int ci in kv.Value) DrawCell(cells[ci], eye);
                }
            }
        }

        void DrawCell(Cell c, Vector3 eye)
        {
            var p = protos[c.proto];
            if (!p.material || p.lods == null || p.lods.Length == 0) return;
            // 칸 안 개체들의 카메라 거리 범위(바운딩 박스 기준)
            float dMin = Mathf.Sqrt(c.bounds.SqrDistance(eye));
            float dMax = Vector3.Distance(eye, c.bounds.center) + c.bounds.extents.magnitude;
            var r = ranges[c.proto];
            if (dMin > r[r.Length - 1].w) return;
            if (!GeometryUtility.TestPlanesAABB(planes, c.bounds)) return;

            c.matrices ??= BuildMatrices(c, p.thinnable);
            if (c.matrices.Length == 0) return;
            DrawSplit(c.proto, c.matrices, c.bounds, eye, dMin, dMax, true);
        }

        // 묶음 하나에서 한 종류를 합쳐 그림(LOD 1 끝보다 멀어서 그림자 없음)
        void DrawFar(Group g, int proto, Vector3 eye)
        {
            var p = protos[proto];
            if (!p.material || p.lods == null) return;
            g.far ??= new Dictionary<int, List<Far>>();
            if (!g.far.TryGetValue(proto, out var list))
            {
                list = new List<Far>();
                var all = new List<Matrix4x4>();
                Bounds b = default; bool first = true;
                foreach (int ci in g.byProto[proto])
                {
                    var c = cells[ci];
                    c.matrices ??= BuildMatrices(c, p.thinnable);
                    all.AddRange(c.matrices);
                    if (first) { b = c.bounds; first = false; } else b.Encapsulate(c.bounds);
                }
                for (int i = 0; i < all.Count; i += MaxPerCell)
                    list.Add(new Far { bounds = b, matrices = all.GetRange(i, Mathf.Min(MaxPerCell, all.Count - i)).ToArray() });
                g.far[proto] = list;
            }
            var r = ranges[proto];
            foreach (var f in list)
            {
                if (f.matrices.Length == 0 || !GeometryUtility.TestPlanesAABB(planes, f.bounds)) continue;
                float dMin = Mathf.Sqrt(f.bounds.SqrDistance(eye));
                float dMax = Vector3.Distance(eye, f.bounds.center) + f.bounds.extents.magnitude;
                if (dMin > r[r.Length - 1].w) continue;
                DrawSplit(proto, f.matrices, f.bounds, eye, dMin, dMax, false);
            }
        }

        // 개체 묶음 그리기. 거리 범위가 LOD 하나에만 걸치면 통째로, 여럿에 걸치면 개체마다 거리로 LOD 목록에 나눠 담아
        // 같은 개체를 두 LOD로 중복해 그리지 않는다(교차 구간 안의 개체만 두 LOD에 들어가 디더로 나눠 그려짐)
        readonly List<Matrix4x4>[] split = { new(1024), new(1024), new(1024), new(1024), new(1024), new(1024) };

        void DrawSplit(int proto, Matrix4x4[] matrices, Bounds bounds, Vector3 eye, float dMin, float dMax, bool allowShadow)
        {
            var p = protos[proto];
            var r = ranges[proto];
            int first = -1, last = -1;
            for (int lod = 0; lod < p.lods.Length; lod++)
            {
                var lr = r[lod];
                float from = lod == 0 ? 0f : lr.x;
                if (dMax < from || dMin > lr.w) continue;
                if (first < 0) first = lod;
                last = lod;
            }
            if (first < 0) return;
            if (first == last)
            {
                Submit(proto, first, matrices, matrices.Length, bounds, allowShadow);
                return;
            }
            for (int lod = first; lod <= last; lod++) split[lod].Clear();
            foreach (var m in matrices)
            {
                float d = Vector3.Distance(eye, new Vector3(m.m03, m.m13, m.m23));
                for (int lod = first; lod <= last; lod++)
                {
                    var lr = r[lod];
                    if ((lod == 0 || d >= lr.x) && d <= lr.w) split[lod].Add(m);
                }
            }
            for (int lod = first; lod <= last; lod++)
                if (split[lod].Count > 0) Submit(proto, lod, split[lod], bounds, allowShadow);
        }

        RenderParams Params(int proto, int lod, Bounds bounds, bool allowShadow)
        {
            var p = protos[proto];
            return new RenderParams(p.material)
            {
                shadowCastingMode = allowShadow && ShadowsEnabled && p.castShadows && lod == 0 ? ShadowCastingMode.On : ShadowCastingMode.Off,
                receiveShadows = true,
                lightProbeUsage = LightProbeUsage.Off,
                worldBounds = bounds,
                matProps = blocks[proto][lod],
            };
        }

        void Submit(int proto, int lod, Matrix4x4[] m, int count, Bounds bounds, bool allowShadow)
        {
            var mesh = protos[proto].lods[lod];
            if (mesh) Graphics.RenderMeshInstanced(Params(proto, lod, bounds, allowShadow), mesh, 0, m, count);
        }

        void Submit(int proto, int lod, List<Matrix4x4> m, Bounds bounds, bool allowShadow)
        {
            var mesh = protos[proto].lods[lod];
            if (mesh) Graphics.RenderMeshInstanced(Params(proto, lod, bounds, allowShadow), mesh, 0, m);
        }
    }
}
