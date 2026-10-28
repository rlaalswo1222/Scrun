using Scrun;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static ScrunEditorUtil;

// 메뉴 Scrun → Build Test Track Scene : 1차 뼈대 씬을 처음부터 만든다(덮어씀)
public static class TestTrackSceneBuilder
{
    const string Root = "Assets/Scrun";
    const string ScenePath = Root + "/Scenes/TestTrack.unity";

    [MenuItem("Scrun/Build Test Track Scene")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        EnsureFolder(Root, "Scenes");
        EnsureFolder(Root, "Materials");

        // 클립 자르기(재임포트)가 있을 수 있어 씬 만들기 전에 Animator부터
        var runnerController = AssetDatabase.LoadAssetAtPath<GameObject>(RunnerRigBuilder.CharacterModelPath)
            ? RunnerAnimatorBuilder.Build() : null;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        EnsureFolder(Root, "Textures");

        // 발 미끄러짐을 눈으로 볼 수 있게 바닥에 1m 기준 무늬
        var grassMat = MakeMat("Grass", Color.white, MakeTexture("Grass_1m", 64, 64, GrassPixel), new Vector2(100f, 100f));
        var trackMat = MakeMat("Track", Color.white, MakeTexture("Track_1m", 512, 128, TrackPixel), Vector2.one);
        var lineMat = MakeMat("Line", Color.white);
        var postMatA = MakeMat("PostWhite", new Color(0.92f, 0.92f, 0.92f));
        var postMatB = MakeMat("PostOrange", new Color(0.95f, 0.45f, 0.15f));

        // 바닥
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(20f, 1f, 20f); // 200m x 200m, 무늬 1칸 = 1m
        ground.GetComponent<Renderer>().sharedMaterial = grassMat;

        // 트랙 (바닥보다 살짝 위)
        var trackGo = new GameObject("Track", typeof(MeshFilter), typeof(MeshRenderer), typeof(CircleTrack));
        trackGo.transform.position = new Vector3(0f, 0.02f, 0f);
        trackGo.GetComponent<MeshRenderer>().sharedMaterial = trackMat;
        var track = trackGo.GetComponent<CircleTrack>();
        track.BuildMesh();

        // 출발선 (거리 0 지점, 트랙을 가로지름)
        var start = GameObject.CreatePrimitive(PrimitiveType.Cube);
        start.name = "StartLine";
        Object.DestroyImmediate(start.GetComponent<Collider>());
        start.transform.SetParent(trackGo.transform, false);
        start.transform.position = track.GetPoint(0f) + Vector3.up * 0.01f;
        start.transform.rotation = Quaternion.LookRotation(track.GetForward(0f));
        start.transform.localScale = new Vector3(4.2f, 0.01f, 0.3f);
        start.GetComponent<Renderer>().sharedMaterial = lineMat;

        // 트랙 바깥 5m 간격 기둥 (흰색/주황 번갈아, 50m마다 높게)
        var posts = new GameObject("Posts").transform;
        posts.SetParent(trackGo.transform, false);
        for (int i = 0; i * 5f < track.LapLength; i++)
        {
            float d = i * 5f;
            bool tall = i % 10 == 0;
            var post = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            post.name = $"Post_{d:0}m";
            Object.DestroyImmediate(post.GetComponent<Collider>());
            post.transform.SetParent(posts, false);
            float h = tall ? 2.5f : 1.2f;
            var outward = Vector3.Cross(Vector3.up, track.GetForward(d)); // 반시계 진행 → 오른쪽 = 바깥
            post.transform.position = track.GetPoint(d) + outward * 2.6f + Vector3.up * (h / 2f);
            post.transform.localScale = new Vector3(0.12f, h / 2f, 0.12f);
            post.GetComponent<Renderer>().sharedMaterial = i % 2 == 0 ? postMatB : postMatA;
        }

        RunnerRigBuilder.Create(track, runnerController, trackGo.transform.position.y);

        EditorSceneManager.SaveScene(scene, ScenePath);
        AddToBuildSettings(ScenePath);
        Debug.Log($"[Scrun] {ScenePath} 생성 완료. Play 누르고 ↑↓로 속도 조절.");
    }

    static float Noise(float u, float v, float scale) => Mathf.PerlinNoise(u * scale + 13.1f, v * scale + 7.7f);

    // 잔디: 텍스처 1장 = 2m(타일링 100 / 200m), 체커 1칸 = 1m + 얕은 노이즈
    static Color GrassPixel(float u, float v)
    {
        bool odd = (u < 0.5f) ^ (v < 0.5f);
        var c = odd ? new Color(0.33f, 0.52f, 0.29f) : new Color(0.38f, 0.58f, 0.33f);
        return c * (0.93f + 0.07f * Noise(u, v, 8f));
    }

    // 트랙 가로(u) = 폭 4m, 세로(v) = 길이 1m: 아스팔트 + 양쪽 흰 차선 + 1m마다 가로줄
    static Color TrackPixel(float u, float v)
    {
        var c = new Color(0.42f, 0.42f, 0.44f) * (0.9f + 0.1f * Noise(u, v, 40f));
        if ((u > 0.03f && u < 0.06f) || (u > 0.94f && u < 0.97f)) c = new Color(0.95f, 0.95f, 0.95f);
        else if (v < 0.04f) c = new Color(0.78f, 0.78f, 0.8f);
        c.a = 1f;
        return c;
    }
}
