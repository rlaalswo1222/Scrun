using UnityEngine;

namespace Scrun
{
    // 우주 루프 구간 불러오기: 러너 근처 구간만 켠다(지금 구간 + 경계에서 pad m 안의 이웃).
    // 구간마다 따라붙는 연출(입자 등)도 같이 켜고 끈다. 아이폰에서 그리는 양을 한 구간 수준으로 유지하기 위함.
    public class SpaceLoopStreamer : MonoBehaviour
    {
        [SerializeField] RunnerController runner;
        [SerializeField] GameObject[] zoneRoots = new GameObject[5];
        [SerializeField] GameObject[] zoneFx = new GameObject[5];
        [SerializeField] float zoneLength = 600f;
        [SerializeField] float pad = 150f;
        [SerializeField] float fxPad = 40f;
        [SerializeField] float editorDistance = 900f;

        public void SetEditorDistance(float d) { editorDistance = d; Refresh(); }

        void LateUpdate() => Refresh();

        bool Near(int i, float s, float p)
        {
            float lap = zoneRoots.Length * zoneLength;
            float a = i * zoneLength - p, b = (i + 1) * zoneLength + p;
            foreach (float off in new[] { -lap, 0f, lap })
                if (s + off >= a && s + off <= b) return true;
            return false;
        }

        public void Refresh()
        {
            float lap = zoneRoots.Length * zoneLength;
            float s = Mathf.Repeat(runner && Application.isPlaying ? runner.Distance : editorDistance, lap);
            for (int i = 0; i < zoneRoots.Length; i++)
            {
                if (zoneRoots[i]) { bool on = Near(i, s, pad); if (zoneRoots[i].activeSelf != on) zoneRoots[i].SetActive(on); }
                if (i < zoneFx.Length && zoneFx[i]) { bool on = Near(i, s, fxPad); if (zoneFx[i].activeSelf != on) zoneFx[i].SetActive(on); }
            }
        }
    }
}
