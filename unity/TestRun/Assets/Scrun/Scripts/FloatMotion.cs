using UnityEngine;

namespace Scrun
{
    // 떠 있는 바위·드론·발광 생물: 위아래로 천천히 흔들리고(bob), 원을 그리며 돌고(orbit), 제자리에서 돈다(spin).
    public class FloatMotion : MonoBehaviour
    {
        [SerializeField] float bobAmplitude = 0.6f;
        [SerializeField] float bobPeriod = 6f;
        [SerializeField] float orbitRadius = 0f;
        [SerializeField] float orbitPeriod = 20f;
        [SerializeField] float spinDegPerSec = 0f;
        [SerializeField] bool faceOrbit = true;

        Vector3 origin;
        float phase;

        public void Set(float bobAmp, float bobPer, float orbitR, float orbitPer, float spin)
        {
            bobAmplitude = bobAmp; bobPeriod = bobPer; orbitRadius = orbitR; orbitPeriod = orbitPer; spinDegPerSec = spin;
        }

        void Start()
        {
            origin = transform.position;
            phase = (origin.x * 0.13f + origin.z * 0.07f) % (Mathf.PI * 2);
        }

        void Update()
        {
            float t = Time.time;
            var p = origin;
            p.y += Mathf.Sin(t * Mathf.PI * 2 / Mathf.Max(bobPeriod, 0.1f) + phase) * bobAmplitude;
            if (orbitRadius > 0)
            {
                float a = t * Mathf.PI * 2 / Mathf.Max(orbitPeriod, 0.1f) + phase;
                p.x += Mathf.Cos(a) * orbitRadius;
                p.z += Mathf.Sin(a) * orbitRadius;
                if (faceOrbit) transform.rotation = Quaternion.LookRotation(new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a)));
            }
            transform.position = p;
            if (spinDegPerSec != 0) transform.Rotate(Vector3.up, spinDegPerSec * Time.deltaTime, Space.World);
        }
    }
}
