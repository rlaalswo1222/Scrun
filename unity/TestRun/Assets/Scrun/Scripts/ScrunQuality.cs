using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Scrun
{
    // 기기 성능에 맞춘 품질 단계. 휴대폰은 러닝머신 거치대 위에서 오래 켜 두므로 발열·배터리를 우선한다.
    //  High(PC): 제한 없음, MSAA 4x / Medium(일반 폰): 30fps, 렌더 배율 0.85, 그림자 40m, 식생 거리 60%, 풀 50%, MSAA 4x
    //  Low(저사양 폰): 30fps, 렌더 배율 0.7, 그림자 25m(식생 그림자 없음), 식생 거리 45%, 풀 30%, 블룸 끔, MSAA 2x
    // URP 에셋 값은 실행 중 바꾸면 에디터에선 에셋 파일이 바뀌므로, 에셋 변경은 실제 기기(플레이어)에서만 한다.
    [DefaultExecutionOrder(-100)]
    public class ScrunQuality : MonoBehaviour
    {
        public enum Tier { Auto, Low, Medium, High }

        [SerializeField] Tier force = Tier.Auto;   // 에디터에서 폰 설정을 흉내 낼 때
        [SerializeField] Volume postVolume;

        public static Tier Current { get; private set; } = Tier.High;

        void Awake()
        {
            var tier = force != Tier.Auto ? force : Detect();
            Apply(tier);
            Screen.sleepTimeout = SleepTimeout.NeverSleep;   // 달리는 동안 화면 꺼짐 방지
        }

        static Tier Detect()
        {
            if (!Application.isMobilePlatform) return Tier.High;
            bool weak = SystemInfo.systemMemorySize < 5000 || SystemInfo.processorCount < 6 || SystemInfo.graphicsMemorySize < 1500;
            return weak ? Tier.Low : Tier.Medium;
        }

        public void Apply(Tier tier)
        {
            Current = tier;
            float dist = 1f, grass = 1f, scale = 1f, shadow = 120f, lodBias = 1f;
            int fps = -1;
            switch (tier)
            {
                case Tier.Medium: dist = 0.55f; grass = 0.45f; scale = 0.85f; shadow = 40f; lodBias = 0.7f; fps = 30; break;
                case Tier.Low: dist = 0.42f; grass = 0.3f; scale = 0.7f; shadow = 25f; lodBias = 0.5f; fps = 30; break;
            }
            InstancedScatter.GlobalDistanceScale = dist;
            SkinnedLod.DistanceScale = tier == Tier.Low ? 0.5f : tier == Tier.Medium ? 0.7f : 1f;
            InstancedScatter.GrassDensity = grass;
            InstancedScatter.ShadowsEnabled = tier != Tier.Low;
            QualitySettings.lodBias = lodBias;
            Application.targetFrameRate = fps;

            var cam = Camera.main;
            if (cam)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                // 계단 줄이기는 MSAA(풀잎·잎 카드가 움직일 때 지글거리지 않음, 타일 GPU 폰에선 값쌈). 후처리 AA는 끔
                data.antialiasing = AntialiasingMode.None;
                cam.allowMSAA = true;
                cam.farClipPlane = tier == Tier.Low ? 9000f : 12000f;   // 먼 산 섬(6~8km)은 삼각형이 적어 저사양도 그림
            }

            // 저사양: 페이스 봇 3명만·무리 없음 / 일반 폰: 무리 6명 / PC: 전부
            int bots = 0, crowd = 0;
            int crowdMax = tier == Tier.Low ? 0 : tier == Tier.Medium ? 6 : 99;
            foreach (var t in FindObjectsByType<RunnerTag>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
            {
                if (t.kind != RunnerTag.Kind.Bot) continue;
                if (t.crowd) t.gameObject.SetActive(crowd++ < crowdMax);
                else t.gameObject.SetActive(tier != Tier.Low || bots++ < 3);
            }

            if (Application.isEditor) return;
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                urp.renderScale = scale;
                urp.shadowDistance = shadow;
                urp.msaaSampleCount = tier == Tier.Low ? 2 : 4;
            }
            if (tier == Tier.Low && postVolume && postVolume.profile.TryGet<Bloom>(out var bloom)) bloom.active = false;
        }
    }
}
