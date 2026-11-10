using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // 러닝 설정·거치 안내(D-01·D-05·D-06, 계획 0026) 뒤의 흐린 맵.
    // USS에는 흐림이 없으므로 섬 카메라로 한 장 작게 찍고(화면의 1/4) 반씩 세 번 줄여(1/32) 크게 늘리면 흐리게 보인다.
    // 안내가 떠 있는 동안에는 맵이 멈춰 보여도 되므로 섬 카메라를 끈다(아이폰 발열·배터리). 화면은 앱 카메라가 남색으로 지우기만 한다.
    // 흐린 그림은 앱 화면 전체(노치·홈 바 여백까지) 뒤에 깐다: 패널 맨 아래(app-root 뒤, 맵 화면에서는 app-root가 투명).
    //   var b = MapBackdrop.Show(Root.panel.visualTree, islandCamera, appCamera);  ...  b.Dispose();  // 섬 카메라 다시 켬
    public sealed class MapBackdrop : IDisposable
    {
        const int FirstDivisor = 4; // 섬 카메라로 찍는 크기(화면의 1/4)
        const int Halvings = 3;     // 그다음 반씩 줄이기(1/4 → 1/32). 줄일 때마다 이웃 픽셀이 섞여 흐려짐
        static readonly Color Dim = new Color32(0x0F, 0x1B, 0x3D, 0x80); // App.uss --color-night-dim(USS 변수는 C#에서 못 읽음)

        readonly VisualElement element;
        readonly Camera island, app;
        readonly bool islandWasOn, appWasOn;
        readonly int appMask;
        RenderTexture image;
        bool disposed;

        MapBackdrop(VisualElement panelRoot, Camera island, Camera app)
        {
            this.island = island;
            this.app = app;
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
                element.style.backgroundImage = Background.FromRenderTexture(image);
                element.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
            }
            var dim = new VisualElement { pickingMode = PickingMode.Ignore };
            dim.style.flexGrow = 1;
            dim.style.backgroundColor = Dim;
            element.Add(dim);
            panelRoot.Insert(0, element);

            if (island != null)
            {
                islandWasOn = island.enabled;
                island.enabled = false;
            }
            if (app != null)
            {
                // 화면을 지우기만(그리는 것 없음). 켜진 카메라가 하나도 없으면 에디터가 "No cameras rendering"을 덮어 그림
                appWasOn = app.enabled;
                appMask = app.cullingMask;
                app.cullingMask = 0;
                app.enabled = true;
            }
        }

        public static MapBackdrop Show(VisualElement panelRoot, Camera island, Camera app)
        {
            if (panelRoot == null) throw new ArgumentNullException(nameof(panelRoot));
            return new MapBackdrop(panelRoot, island, app);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            element.RemoveFromHierarchy();
            if (app != null)
            {
                app.cullingMask = appMask;
                app.enabled = appWasOn;
            }
            if (island != null) island.enabled = islandWasOn; // 섬 씬이 이미 내려갔으면 null(Unity 비교)
            if (image != null)
            {
                image.Release();
                UnityEngine.Object.Destroy(image);
                image = null;
            }
        }

        static RenderTexture Snapshot(Camera cam)
        {
            if (cam == null || !cam.isActiveAndEnabled) return null;
            int w = Mathf.Max(8, Screen.width / FirstDivisor), h = Mathf.Max(8, Screen.height / FirstDivisor);
            var temps = new List<RenderTexture>(); // 중간에 예외가 나도 모두 돌려준다
            RenderTexture result = null;
            try
            {
                var src = RenderTexture.GetTemporary(w, h, 24);
                temps.Add(src);
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

                for (int i = 0; i < Halvings; i++)
                {
                    w = Mathf.Max(2, w / 2);
                    h = Mathf.Max(2, h / 2);
                    var dst = RenderTexture.GetTemporary(w, h, 0);
                    temps.Add(dst);
                    dst.filterMode = FilterMode.Bilinear;
                    Graphics.Blit(src, dst);
                    src = dst;
                }

                result = new RenderTexture(w, h, 0) { name = "MapBackdrop", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                Graphics.Blit(src, result);
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
                foreach (var t in temps) RenderTexture.ReleaseTemporary(t);
            }
        }
    }
}
