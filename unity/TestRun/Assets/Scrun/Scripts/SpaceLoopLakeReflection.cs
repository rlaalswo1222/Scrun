using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Scrun
{
    // 숲 호수 반사: 수면을 기준으로 뒤집은 카메라가 "반사 레이어"(먼 산·나무 줄·물안개·달)만 낮은 해상도로 그려
    // 전역 텍스처 _ScrunReflectTex로 넘긴다. Scrun/Water가 화면 좌표로 읽어 섞는다(_ScrunReflectOn).
    // 장면 전체를 두 번 그리지 않도록 레이어를 좁히고, 호수가 켜져 있을 때(숲 구간 근처)만 돈다.
    // 반사 레이어 물체는 양면 재질이어야 한다(거울 변환이라 면 방향이 뒤집힘).
    [DefaultExecutionOrder(10000)]
    public class SpaceLoopLakeReflection : MonoBehaviour
    {
        [SerializeField] Camera mainCamera;
        [SerializeField] Renderer water;
        [SerializeField] float waterHeight = -1f;
        [SerializeField] LayerMask reflectLayers;
        [SerializeField, Range(1, 6)] int downscale = 3;
        [SerializeField, Range(0, 1)] float strength = 0.85f;

        static readonly int ReflectTex = Shader.PropertyToID("_ScrunReflectTex");
        static readonly int ReflectOn = Shader.PropertyToID("_ScrunReflectOn");

        Camera reflCam;
        RenderTexture rt;

        void OnDisable()
        {
            Shader.SetGlobalFloat(ReflectOn, 0f);
            if (reflCam) Destroy(reflCam.gameObject);
            if (rt) { rt.Release(); Destroy(rt); }
            reflCam = null; rt = null;
        }

        void LateUpdate()
        {
            // 저사양 단계(처음부터든 달리는 중 품질 낮추기든)에선 반사 카메라를 끔 → 하늘색 반사만(계획 0066)
            bool on = mainCamera && water && water.gameObject.activeInHierarchy && ScrunQuality.Current != ScrunQuality.Tier.Low;
            Shader.SetGlobalFloat(ReflectOn, on ? strength : 0f);
            if (!on) { if (reflCam) reflCam.enabled = false; return; }
            Ensure();
            reflCam.enabled = true;

            // 수면 기준 거울 행렬
            float d = -waterHeight;
            var R = Matrix4x4.identity;
            R.m11 = -1f; R.m13 = -2f * d;
            reflCam.worldToCameraMatrix = mainCamera.worldToCameraMatrix * R;
            var p = mainCamera.transform.position; p.y = 2 * waterHeight - p.y;
            reflCam.transform.position = p;

            // 물 아래(수면 밑으로 내려간 산자락)를 자르는 비스듬한 근평면
            var plane = CameraSpacePlane(reflCam, new Vector3(0, waterHeight, 0), Vector3.up);
            reflCam.projectionMatrix = mainCamera.CalculateObliqueMatrix(plane);
            reflCam.fieldOfView = mainCamera.fieldOfView;
            reflCam.farClipPlane = mainCamera.farClipPlane;
        }

        void Ensure()
        {
            int w = Mathf.Max(480, Screen.width / downscale), h = Mathf.Max(220, Screen.height / downscale);
            if (rt == null || rt.width != w || rt.height != h)
            {
                if (rt) { rt.Release(); Destroy(rt); }
                rt = new RenderTexture(w, h, 16, RenderTextureFormat.DefaultHDR) { name = "LakeReflection", useMipMap = false };
                rt.Create();
                Shader.SetGlobalTexture(ReflectTex, rt);
                if (reflCam) reflCam.targetTexture = rt;
            }
            if (reflCam) return;
            var go = new GameObject("LakeReflectionCamera") { hideFlags = HideFlags.HideAndDontSave };
            reflCam = go.AddComponent<Camera>();
            reflCam.CopyFrom(mainCamera);
            reflCam.cullingMask = reflectLayers;
            reflCam.depth = mainCamera.depth - 10;
            reflCam.targetTexture = rt;
            reflCam.allowMSAA = false;
            reflCam.allowHDR = true;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.requiresDepthTexture = false;
            data.requiresColorTexture = false;
            data.antialiasing = AntialiasingMode.None;
            Shader.SetGlobalTexture(ReflectTex, rt);
        }

        static Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal)
        {
            var m = cam.worldToCameraMatrix;
            var cpos = m.MultiplyPoint(pos + normal * 0.05f);
            var cnormal = m.MultiplyVector(normal).normalized;
            return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
        }
    }
}
