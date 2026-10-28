using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Scrun
{
    // 페이스 파트너: 정해진 속도(살짝 흔들림)로 계속 달림
    public class PaceSpeed : MonoBehaviour, ISpeedSource
    {
        public float targetKmh = 10f;
        public float wobble = 0.25f;   // km/h
        float seed;

        public float SpeedKmh => Mathf.Max(0f, targetKmh + Mathf.Sin(Time.time * 0.21f + seed) * wobble);

        void Awake() => seed = UnityEngine.Random.value * 10f;
    }
}
