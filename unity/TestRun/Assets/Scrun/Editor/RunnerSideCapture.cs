using System;
using System.IO;
using Scrun;
using UnityEditor;
using UnityEngine;

// 개발용: Play 중 플레이어 러너를 옆에서 찍어 프레임(PNG)으로 남긴다(계획 0027, 발 미끄러짐을 눈으로 비교).
// 게임 시간을 30fps로 고정해 찍으므로 에디터가 느려도 영상 속도는 실제와 같다. 매번 새 폴더(<out>/side_<시각>)에 쓰고,
// ffmpeg로 묶는다: ffmpeg -framerate 30 -i %04d.png -c:v libx264 -pix_fmt yuv420p side.mp4
//   RunnerSideCapture.Begin("/tmp/scrun_side", 90);   // 3초
public static class RunnerSideCapture
{
    const int Fps = 30;
    static string dir;
    static int left, taken, lastFrame;
    static GameObject cam;

    [MenuItem("Scrun/Dev/Capture Runner Side (3s)")]
    static void Menu() => Begin(Path.Combine(Path.GetTempPath(), "scrun_side"), Fps * 3);

    [MenuItem("Scrun/Dev/Capture Runner Side (3s)", true)]
    static bool CanMenu() => EditorApplication.isPlaying && cam == null;

    // 녹화 중에 스크립트를 다시 컴파일하면 고정한 게임 시간이 남는다 → 불릴 때마다 풀어 둠
    [InitializeOnLoadMethod]
    static void ResetCaptureRate()
    {
        if (cam == null) Time.captureFramerate = 0;
    }

    // 찍기 시작. 저장할 폴더를 돌려준다(못 찍으면 null)
    public static string Begin(string outRoot, int frames)
    {
        if (!EditorApplication.isPlaying) { Debug.LogError("[SideCapture] Play 중에만 찍음(편집 중인 씬에 카메라를 남기지 않게)"); return null; }
        if (cam != null) { Debug.LogWarning("[SideCapture] 이미 찍는 중"); return null; }
        var body = PlayerBody();
        if (body == null) { Debug.LogError("[SideCapture] 러너(RunnerAnimator)가 없음"); return null; }

        dir = Path.Combine(outRoot, "side_" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(dir);

        // 러너 오른쪽 2.4 m, 무릎 높이에서 발을 본다. 러너를 따라가게 자식으로
        cam = new GameObject("SideCaptureCamera") { hideFlags = HideFlags.DontSave };
        cam.transform.SetParent(body, false);
        cam.transform.localPosition = new Vector3(2.4f, 0.55f, 0f);
        cam.transform.localRotation = Quaternion.LookRotation(new Vector3(-1f, -0.12f, 0f));
        var c = cam.AddComponent<Camera>();
        c.depth = 100;
        c.fieldOfView = 40;
        c.nearClipPlane = 0.05f;

        left = Mathf.Max(1, frames);
        taken = 0;
        lastFrame = -1;
        Time.captureFramerate = Fps;
        EditorApplication.update += Tick;
        return dir;
    }

    // 플레이어(RunnerTag.Player)의 몸, 없으면 처음 찾은 러너
    static Transform PlayerBody()
    {
        var player = RunnerTag.Player;
        if (player != null && player.runner != null)
        {
            var a = player.runner.GetComponentInChildren<RunnerAnimator>();
            if (a != null) return a.transform;
        }
        var any = UnityEngine.Object.FindFirstObjectByType<RunnerAnimator>();
        return any != null ? any.transform : null;
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying || cam == null) { End(); return; }
        if (Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;
        ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"{taken++:0000}.png"));
        if (--left <= 0) End();
    }

    static void End()
    {
        EditorApplication.update -= Tick;
        Time.captureFramerate = 0;
        if (cam != null)
        {
            if (EditorApplication.isPlaying) UnityEngine.Object.Destroy(cam);
            else UnityEngine.Object.DestroyImmediate(cam);
        }
        cam = null;
        Debug.Log($"[SideCapture] 끝: {dir}");
    }
}
