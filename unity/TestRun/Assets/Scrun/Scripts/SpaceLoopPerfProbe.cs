#if SCRUN_PERF
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Scrun
{
    // 우주 루프 성능 측정(계획 0063). 메뉴 Scrun → Space Loop → iOS 성능 측정 빌드로 만든 개발 빌드에서만 켜진다(SCRUN_PERF).
    // 앱과 같은 품질(ScrunQuality 자동 단계)과 가로 화면으로 잰다. 결과는 로그([SLPerf])와 Documents/slperf_latest.csv
    //  - 기본(sweep): 5구간 x 3곳을 순간이동하며 곳마다 2초 데우고 6초 기록 → 3바퀴(약 6분). 1바퀴와 3바퀴를 비교해 발열을 본다
    //  - 분석(SCRUN_PERF_ANALYZE, 약 5분): ① 60fps 상한으로 15곳(여유) ② 무거운 곳에서 하늘·후처리·반투명·MSAA·그림자·렌더 배율을
    //    하나씩 바꿔 GPU 시간 비교 ③ 앱 설정(30fps)에서 아바타 20명이 같이 달릴 때
    public class SpaceLoopPerfProbe : MonoBehaviour
    {
        const float ZoneLength = 600f, RunKmh = 10f;
        static readonly float[] Offsets = { 100f, 300f, 500f };
        static readonly string[] ZoneNames = { "구름 위", "화성", "숲 호수", "얼음", "달" };

        RunnerController runner;
        FakeSpeedSource speed;
        FollowCamera follow;
        string status = "준비";
        readonly List<string> summary = new List<string>();
        StringBuilder csv;
        float startTime;

        ProfilerRecorder drawCalls, setPass, tris, verts, sysMem, gfxMem;

        // 발열은 팀 Native 층(iOS ProcessInfo.thermalState, ScrunBridge "device.thermal")
        static int ThermalState()
        {
            try { return (int)Scrun.App.Native.Device.Thermal; } catch { return -1; }
        }
        static readonly string[] Thermal = { "보통", "조금 뜨거움", "뜨거움(시스템이 성능 줄임)", "매우 뜨거움" };
        static string ThermalName(int t) => t >= 0 && t < Thermal.Length ? Thermal[t] : "모름";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Spawn()
        {
            if (SceneManager.GetActiveScene().name != "SpaceLoop") return;
            new GameObject("SpaceLoopPerfProbe").AddComponent<SpaceLoopPerfProbe>();
        }

        void Start()
        {
            var r = GameObject.Find("Runner");
            runner = r ? r.GetComponent<RunnerController>() : null;
            speed = r ? r.GetComponent<FakeSpeedSource>() : null;
            follow = FindFirstObjectByType<FollowCamera>();
            // 앱의 러닝 화면처럼 가로(SafeArea와 같은 설정). 세로로 재면 옆으로 보이는 물체가 적어 가볍게 나옴
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.orientation = ScreenOrientation.LandscapeLeft;
            // 앱의 섬 씬처럼 품질 단계를 자동으로(폰이면 Medium: 30fps, 렌더 배율 0.85, 그림자 40m, MSAA 4x)
            if (!FindFirstObjectByType<ScrunQuality>()) gameObject.AddComponent<ScrunQuality>();
            drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            verts = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
            sysMem = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
            gfxMem = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Gfx Used Memory");
            StartCoroutine(Main());
        }

        void OnDestroy()
        {
            drawCalls.Dispose(); setPass.Dispose(); tris.Dispose(); verts.Dispose(); sysMem.Dispose(); gfxMem.Dispose();
        }

        IEnumerator Main()
        {
            if (!runner || !speed)
            {
                status = "Runner·FakeSpeedSource를 못 찾음";
                Debug.LogError("[SLPerf] " + status);
                yield break;
            }
            yield return new WaitForSeconds(3f);   // 첫 장면 로드·화면 회전 안정
            Log($"[SLPerf] 시작 {SystemInfo.deviceModel} / {SystemInfo.operatingSystem} / 메모리 {SystemInfo.systemMemorySize}MB / GPU {SystemInfo.graphicsDeviceName}");
            Log($"[SLPerf] 품질 {ScrunQuality.Current}, 목표 {Application.targetFrameRate}fps, 화면 {Screen.width}x{Screen.height}, 렌더 배율 {Urp()?.renderScale ?? 1f:0.00}");
            csv = new StringBuilder("phase,label,distance,thermal,fps_avg,ms_avg,ms_p95,ms_max,cpu_main_ms,gpu_ms,draw_calls,setpass,tris,verts,sys_mem_mb,gfx_mem_mb\n");
            startTime = Time.realtimeSinceStartup;
#if SCRUN_PERF_ANALYZE
            yield return Analyze();
#else
            for (int pass = 1; pass <= 3; pass++)
            {
                yield return Sweep($"{pass}바퀴", 2f, 6f);
                Save();
            }
#endif
            speed.Stop();
            Save();
            status = "끝";
            Log("[SLPerf] 끝");
        }

        // 5구간 x 3곳
        IEnumerator Sweep(string phase, float warm, float sample)
        {
            for (int z = 0; z < 5; z++)
                foreach (var off in Offsets)
                    yield return MeasureAt(phase, ZoneNames[z], z * ZoneLength + off, warm, sample);
        }

        IEnumerator MeasureAt(string phase, string label, float d, float warm, float sample)
        {
            status = $"{phase}  {label} {d:0}m  ({Time.realtimeSinceStartup - startTime:0}초)";
            runner.Teleport(d - warm * RunKmh / 3.6f);
            if (follow) follow.Snap();
            speed.Stop(); speed.Add(RunKmh);
            yield return new WaitForSeconds(warm);
            yield return Measure(phase, label, d, sample);
        }

        struct Frame { public float dt, cpu, gpu; public long dc, sp, tri, vtx; }

        IEnumerator Measure(string phase, string label, float d, float sample)
        {
            var frames = new List<Frame>();
            var ft = new FrameTiming[1];
            float until = Time.realtimeSinceStartup + sample;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                FrameTimingManager.CaptureFrameTimings();
                uint n = FrameTimingManager.GetLatestTimings(1, ft);
                frames.Add(new Frame
                {
                    dt = Time.unscaledDeltaTime * 1000f,
                    cpu = n > 0 ? (float)ft[0].cpuMainThreadFrameTime : -1f,
                    gpu = n > 0 ? (float)ft[0].gpuFrameTime : -1f,
                    dc = drawCalls.LastValue, sp = setPass.LastValue, tri = tris.LastValue, vtx = verts.LastValue,
                });
            }
            int th = ThermalState();
            var dts = frames.Select(f => f.dt).OrderBy(x => x).ToList();
            float avg = dts.Average(), p95 = dts[Mathf.Clamp((int)(dts.Count * 0.95f), 0, dts.Count - 1)], max = dts[dts.Count - 1];
            float cpu = Avg(frames.Select(f => f.cpu)), gpu = Avg(frames.Select(f => f.gpu));
            long dc = (long)frames.Average(f => f.dc), sp = (long)frames.Average(f => f.sp), tri = (long)frames.Average(f => f.tri), vtx = (long)frames.Average(f => f.vtx);
            float sm = sysMem.LastValue / 1048576f, gm = gfxMem.LastValue / 1048576f;
            csv.AppendLine($"{phase},{label},{d:0},{th},{1000f / avg:0.0},{avg:0.0},{p95:0.0},{max:0.0},{cpu:0.0},{gpu:0.0},{dc},{sp},{tri},{vtx},{sm:0},{gm:0}");
            Log($"[SLPerf] {phase} {label} {d:0}m  {1000f / avg:0.0}fps  평균 {avg:0.0}ms p95 {p95:0.0} 최대 {max:0.0}  CPU {cpu:0.0} GPU {gpu:0.0}ms  호출 {dc} SetPass {sp}  삼각형 {tri / 1000f:0}천  메모리 {sm:0}/{gm:0}MB  발열 {ThermalName(th)}");
        }

#if SCRUN_PERF_ANALYZE
        // 하나씩 바꿔 보는 것(바꾼 뒤 되돌림)
        struct Toggle { public string name; public Action on, off; }

        IEnumerator Analyze()
        {
            var cam = Camera.main;
            var camData = cam ? cam.GetUniversalAdditionalCameraData() : null;
            var urp = Urp();
            int appFps = Application.targetFrameRate;

            // ① 60fps 상한으로 15곳: 30fps 상한에선 GPU가 클럭을 낮춰 시간이 부풀 수 있어, 최대로 돌려 실제 여유를 본다
            Application.targetFrameRate = 60;
            yield return Sweep("60fps", 2f, 5f);
            Save();

            // ② 무거운 곳에서 하나씩 끄기(60fps 상한)
            var transparent = FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(r => r.sharedMaterials.Any(m => m && m.renderQueue >= 2501)).ToList();
            var clear = cam ? cam.clearFlags : CameraClearFlags.Skybox;
            bool post = camData && camData.renderPostProcessing;
            int msaa = urp ? urp.msaaSampleCount : 1;
            float shadow = urp ? urp.shadowDistance : 0f, scale = urp ? urp.renderScale : 1f;
            var states = new Dictionary<Renderer, bool>();
            var toggles = new List<Toggle>
            {
                new Toggle { name = "기본" , on = () => { }, off = () => { } },
                new Toggle { name = "하늘 단색", on = () => { if (cam) cam.clearFlags = CameraClearFlags.SolidColor; }, off = () => { if (cam) cam.clearFlags = clear; } },
                new Toggle { name = "후처리 끔", on = () => { if (camData) camData.renderPostProcessing = false; }, off = () => { if (camData) camData.renderPostProcessing = post; } },
                new Toggle { name = "반투명 끔", on = () => { states.Clear(); foreach (var r in transparent) if (r) { states[r] = r.enabled; r.enabled = false; } }, off = () => { foreach (var kv in states) if (kv.Key) kv.Key.enabled = kv.Value; } },
                new Toggle { name = "MSAA 끔", on = () => { if (urp) urp.msaaSampleCount = 1; }, off = () => { if (urp) urp.msaaSampleCount = msaa; } },
                new Toggle { name = "그림자 끔", on = () => { if (urp) urp.shadowDistance = 0f; }, off = () => { if (urp) urp.shadowDistance = shadow; } },
                new Toggle { name = "렌더 배율 0.7", on = () => { if (urp) urp.renderScale = 0.7f; }, off = () => { if (urp) urp.renderScale = scale; } },
            };
            Log($"[SLPerf] 반투명 렌더러 {transparent.Count}개");
            foreach (var d in new[] { 100f, 300f, 1100f, 2900f })
            {
                int z = Mathf.Clamp((int)(d / ZoneLength), 0, 4);
                foreach (var t in toggles)
                {
                    t.on();
                    yield return MeasureAt("끄기", $"{ZoneNames[z]}·{t.name}", d, 1.5f, 4f);
                    t.off();
                }
            }
            Save();

            // ③ 앱 설정(30fps)에서 아바타 20명(내 아바타 + 19명). 러너 둘레 앞뒤 2–30m, 옆 ±3m에 두고 같은 속도로 달림
            Application.targetFrameRate = appFps;
            var mine = runner.GetComponentInChildren<Animator>(false);
            if (mine)
            {
                var rnd = new System.Random(7);
                for (int i = 0; i < 19; i++)
                {
                    var c = Instantiate(mine.gameObject, runner.transform);
                    c.name = "PerfCrowd" + i;
                    float ahead = (float)(rnd.NextDouble() * 28 + 2) * (i % 3 == 0 ? -1 : 1);
                    float side = (float)(rnd.NextDouble() * 6 - 3);
                    c.transform.localPosition = mine.transform.localPosition + new Vector3(side, 0, ahead);
                    c.transform.localRotation = mine.transform.localRotation;
                }
                Log("[SLPerf] 아바타 20명");
                for (int z = 0; z < 5; z++)
                    yield return MeasureAt("20명", ZoneNames[z], z * ZoneLength + 300f, 2f, 5f);
            }
        }
#endif

        static float Avg(IEnumerable<float> xs)
        {
            var v = xs.Where(x => x >= 0f).ToList();
            return v.Count > 0 ? v.Average() : -1f;
        }

        static UniversalRenderPipelineAsset Urp() => GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

        void Save()
        {
            string path = Path.Combine(Application.persistentDataPath, "slperf_latest.csv");
            File.WriteAllText(path, csv.ToString());
            Debug.Log($"[SLPerf] 저장 {path}");
        }

        void Log(string s)
        {
            Debug.Log(s);
            summary.Add(s.Replace("[SLPerf] ", ""));
            if (summary.Count > 18) summary.RemoveAt(0);
        }

        // 측정 중에는 한 줄만(그리기 호출을 거의 안 늘림), 끝나면 최근 결과를 크게
        void OnGUI()
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(14, Screen.height / 40) };
            st.normal.textColor = Color.white;
            float h = st.fontSize * 1.35f;
            if (status != "끝")
            {
                GUI.Label(new Rect(12, Screen.height - h * 1.5f, Screen.width - 24, h), "성능 측정: " + status, st);
                return;
            }
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            float y = 12f;
            GUI.Label(new Rect(12, y, Screen.width - 24, h), "성능 측정 끝 (맥에 결과가 저장됨)", st);
            foreach (var line in summary.Skip(Mathf.Max(0, summary.Count - 15)))
                GUI.Label(new Rect(12, y += h, Screen.width - 24, h), line, st);
        }
    }
}
#endif
