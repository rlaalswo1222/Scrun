using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Scrun
{
    // 하늘을 도는 새 무리(날갯짓). 런타임에 아주 작은 메쉬로 만들어 에셋 불필요
    public class BirdFlock : MonoBehaviour
    {
        public int count = 8;
        public float radius = 60f, height = 40f, speed = 9f;
        public Material material;

        readonly List<(Transform body, Transform l, Transform r, float phase, float rad, float h)> birds = new();

        void Start()
        {
            var wing = new Mesh { name = "BirdWing" };
            wing.SetVertices(new List<Vector3> { Vector3.zero, new(0.9f, 0f, 0.15f), new(0.1f, 0f, -0.35f) });
            wing.SetNormals(new List<Vector3> { Vector3.up, Vector3.up, Vector3.up });
            wing.SetColors(new List<Color> { Color.white, Color.white, Color.white });
            wing.SetTriangles(new[] { 0, 1, 2, 0, 2, 1 }, 0);
            wing.RecalculateBounds();
            for (int i = 0; i < count; i++)
            {
                var body = new GameObject($"Bird{i}").transform;
                body.SetParent(transform, false);
                Transform W(float side)
                {
                    var w = new GameObject("Wing", typeof(MeshFilter), typeof(MeshRenderer)).transform;
                    w.SetParent(body, false);
                    w.localScale = new Vector3(side, 1f, 1f);
                    w.GetComponent<MeshFilter>().sharedMesh = wing;
                    var mr = w.GetComponent<MeshRenderer>();
                    mr.sharedMaterial = material;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    return w;
                }
                birds.Add((body, W(-1f), W(1f), UnityEngine.Random.value * 6.28f, radius * (0.7f + 0.6f * UnityEngine.Random.value), height + UnityEngine.Random.Range(-8f, 8f)));
            }
        }

        void Update()
        {
            float t = Time.time;
            foreach (var (body, l, r, phase, rad, h) in birds)
            {
                float a = phase + t * speed / rad;
                var pos = transform.position + new Vector3(Mathf.Cos(a) * rad, h + Mathf.Sin(t * 0.7f + phase) * 3f, Mathf.Sin(a) * rad);
                body.position = pos;
                body.rotation = Quaternion.LookRotation(new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)));
                float flap = Mathf.Sin(t * 9f + phase * 3f) * 35f;
                l.localRotation = Quaternion.Euler(0f, 0f, -flap);
                r.localRotation = Quaternion.Euler(0f, 0f, flap);
            }
        }
    }
}
