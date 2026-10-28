using UnityEngine;

namespace Scrun
{
    // 텍스처를 흘려 보냄(폭포 물줄기). 재질 원본을 건드리지 않게 MaterialPropertyBlock으로 오프셋만 바꾼다
    [RequireComponent(typeof(Renderer))]
    public class UVScroller : MonoBehaviour
    {
        [SerializeField] Vector2 speed = new(0f, -0.6f);   // 초당 UV 이동

        Renderer rend;
        MaterialPropertyBlock block;
        static readonly int BaseMapST = Shader.PropertyToID("_BaseMap_ST");

        public void Set(Vector2 uvPerSecond) => speed = uvPerSecond;

        void OnEnable()
        {
            rend = GetComponent<Renderer>();
            block = new MaterialPropertyBlock();
        }

        void Update()
        {
            var st = rend.sharedMaterial.GetVector(BaseMapST);
            float t = Time.time;
            rend.GetPropertyBlock(block);
            block.SetVector(BaseMapST, new Vector4(st.x, st.y, Mathf.Repeat(speed.x * t, 1f), Mathf.Repeat(speed.y * t, 1f)));
            rend.SetPropertyBlock(block);
        }
    }
}
