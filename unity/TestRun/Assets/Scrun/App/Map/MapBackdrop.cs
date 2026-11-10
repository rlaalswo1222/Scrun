using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // 러닝 설정·거치 안내(D-01·D-05·D-06, 계획 0026) 뒤의 흐린 맵.
    // USS에는 흐림이 없으므로 섬 카메라로 한 장 작게 찍어(화면의 1/4) 반씩 줄였다가(1/32) 다시 반씩 키워(1/8) 흐리게 만든다.
    // 줄이기만 하고 UI가 32배 늘리면 지평선·길에 격자가 보여서, 키우는 단계마다 이웃이 섞이게 한다(dual filter와 같은 생각).
    // 그림은 CPU 쪽 Texture2D로 한 번 읽어 둔다: 설정 앱에 다녀오는 등 GPU 내용이 지워져도(컨텍스트 유실) 검은 화면이 되지 않게.
    // 흐린 그림은 앱 화면 전체(노치·홈 바 여백까지) 뒤에 깐다: 패널 맨 아래(app-root 뒤, 맵 화면에서는 app-root가 투명).
    // 안내가 떠 있는 동안: 섬 카메라를 끄고(이름표도 안 그림), 화면은 이 클래스의 "지우기만 하는 카메라"가 남색으로 지운다
    // (켜진 카메라가 하나도 없으면 아이폰은 뒤가 지워지지 않아 깨진 그림, 에디터는 "No cameras rendering").
    // 화면이 멈춰 있어도 되므로 그리기는 3프레임에 한 번(OnDemandRendering, 입력·스크립트는 매 프레임). 거치하며 몇 분 머무는 화면이라 발열·배터리.
    //   var b = MapBackdrop.Show(Root.panel.visualTree, islandCamera);  ...  b.Dispose();  // 섬 카메라·그리기 간격 되돌림
    public sealed class MapBackdrop : IDisposable
    {
        const int FirstDivisor = 4; // 섬 카메라로 찍는 크기(화면의 1/4)
        const int Downs = 3;        // 반씩 줄이기(1/4 → 1/32)
        const int Ups = 2;          // 다시 반씩 키우기(1/32 → 1/8)
        const int RenderInterval = 3;
        static readonly Color Dim = new Color32(0x0F, 0x1B, 0x3D, 0x80); // App.uss --color-night-dim(USS 변수는 C#에서 못 읽음)

        readonly VisualElement element;
        readonly Camera island;
        readonly bool islandWasOn;
        readonly GameObject clearCamera;
        readonly int renderIntervalBefore;
        Texture2D image;
        bool disposed;

        MapBackdrop(VisualElement panelRoot, Camera island)
        {
            this.island = island;
            element = new VisualElement { name = "map-backdrop", pickingMode = PickingMode.Ignore };
            element.style.position = Position.Absolute;
            element.style.left = 0;
            element.style.top = 0;
            element.style.right = 0;
            element.style.bottom = 0;
            element.style.backgroundColor = MapScreen.Night; // 찍지 못했을 때 바탕

            image = Snapshot(island);
            if (image != null)
            {
                element.style.backgroundImage = Background.FromTexture2D(image);
                element.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            }
            var dim = new VisualElement { pickingMode = PickingMode.Ignore };
            dim.style.flexGrow = 1;
            dim.style.backgroundColor = Dim;
            element.Add(dim);
            panelRoot.Insert(0, element);

            // 지우기만 하는 카메라를 먼저 켜고 섬 카메라를 끈다(그 사이에도 켜진 카메라가 늘 하나 있게)
            clearCamera = new GameObject("MapBackdropClear") { hideFlags = HideFlags.DontSave };
            var c = clearCamera.AddComponent<Camera>();
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = MapScreen.Night;
            c.cullingMask = 0;
            c.depth = -100;
            c.allowHDR = false;
            c.allowMSAA = false;

            if (island != null)
            {
                islandWasOn = island.enabled;
                island.enabled = false;
            }
            renderIntervalBefore = OnDemandRendering.renderFrameInterval;
            OnDemandRendering.renderFrameInterval = RenderInterval;
        }

        public static MapBackdrop Show(VisualElement panelRoot, Camera island)
        {
            if (panelRoot == null) throw new ArgumentNullException(nameof(panelRoot));
            return new MapBackdrop(panelRoot, island);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            OnDemandRendering.renderFrameInterval = renderIntervalBefore;
            if (island != null) island.enabled = islandWasOn; // 섬 씬이 이미 내려갔으면 null(Unity 비교). 지우는 카메라보다 먼저 켬
            if (clearCamera != null) UnityEngine.Object.Destroy(clearCamera);
            element.RemoveFromHierarchy();
            if (image != null)
            {
                UnityEngine.Object.Destroy(image);
                image = null;
            }
        }

        static Texture2D Snapshot(Camera cam)
        {
            if (cam == null || !cam.isActiveAndEnabled) return null;
            int w = Mathf.Max(8, Screen.width / FirstDivisor), h = Mathf.Max(8, Screen.height / FirstDivisor);
            var temps = new List<RenderTexture>(); // 중간에 예외가 나도 모두 돌려준다
            var active = RenderTexture.active;
            Texture2D result = null;
            try
            {
                var src = Temp(temps, w, h, 24);
                var request = new RenderPipeline.StandardRequest { destination = src };
                if (RenderPipeline.SupportsRenderRequest(cam, request))
                    RenderPipeline.SubmitRenderRequest(cam, request);
                else
                {
                    var before = cam.targetTexture;
                    cam.targetTexture = src;
                    cam.Render();
                    cam.targetTexture = before;
                }

                var sizes = new Stack<Vector2Int>();
                for (int i = 0; i < Downs; i++)
                {
                    sizes.Push(new Vector2Int(w, h));
                    w = Mathf.Max(2, w / 2);
                    h = Mathf.Max(2, h / 2);
                    var dst = Temp(temps, w, h, 0);
                    Graphics.Blit(src, dst);
                    src = dst;
                }
                for (int i = 0; i < Ups && sizes.Count > 0; i++)
                {
                    var s = sizes.Pop();
                    w = s.x;
                    h = s.y;
                    var dst = Temp(temps, w, h, 0);
                    Graphics.Blit(src, dst);
                    src = dst;
                }

                RenderTexture.active = src;
                result = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "MapBackdrop", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                result.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                result.Apply(false, false); // CPU 쪽 사본을 남겨 GPU 내용이 지워져도 다시 올라감
                return result;
            }
            catch (Exception e)
            {
                // 흐린 그림이 없어도 안내는 보여야 한다(남색 바탕)
                Debug.LogException(e);
                if (result != null) UnityEngine.Object.Destroy(result);
                return null;
            }
            finally
            {
                RenderTexture.active = active;
                foreach (var t in temps) RenderTexture.ReleaseTemporary(t);
            }
        }

        static RenderTexture Temp(List<RenderTexture> temps, int w, int h, int depth)
        {
            var t = RenderTexture.GetTemporary(w, h, depth);
            t.filterMode = FilterMode.Bilinear;
            temps.Add(t);
            return t;
        }
    }
}
