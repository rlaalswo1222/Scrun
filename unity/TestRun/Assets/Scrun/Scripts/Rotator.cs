using UnityEngine;

namespace Scrun
{
    // 일정 속도로 회전(풍력발전기 날개, 등대 불빛)
    public class Rotator : MonoBehaviour
    {
        [SerializeField] Vector3 axis = Vector3.forward;   // 로컬 축
        [SerializeField] float degreesPerSecond = 15f;

        public void Set(Vector3 localAxis, float speed)
        {
            axis = localAxis;
            degreesPerSecond = speed;
        }

        void Update() => transform.Rotate(axis, degreesPerSecond * Time.deltaTime, Space.Self);
    }
}
