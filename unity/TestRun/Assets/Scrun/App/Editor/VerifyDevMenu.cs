using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Scrun.App.Editor
{
    // 달리기 없이 계기판 인증 화면(E-02, 계획 0038)을 여는 메뉴. 로컬 서버(OCR_ENGINE=fake)에 붙은 에디터에서.
    // Play 중이 아니면 앱 씬을 열어 Play하고, 앱이 뜨면 이어서 연다.
    // 서버에 짧은 끝난 기록을 만든다: 로그인(Fake Native 카카오 토큰) → 첫 코스 입장(보정이 없으면 예시 보정을 올림)
    // → 4분 50초 전 시작 → 지금 종료(1 km · 4:40, 서버는 기기 시계 5분 어긋남까지 받음) → 인증 화면.
    // 결과는 Fake Native 창 "서버 가짜 OCR"로 고른다
    static class VerifyDevMenu
    {
        const string Menu = "Scrun/개발/계기판 인증 화면 열기";
        const string AppScene = "Assets/Scrun/Scenes/App/App.unity";
        const string OpenAfterPlay = "Scrun.VerifyDevMenu.OpenAfterPlay";
        static double openAt;

        [MenuItem(Menu)]
        static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                Open();
                return;
            }
            if (EditorSceneManager.GetActiveScene().path != AppScene)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(AppScene, OpenSceneMode.Single);
            }
            SessionState.SetBool(OpenAfterPlay, true);
            openAt = 0;
            EditorApplication.isPlaying = true;
        }

        [InitializeOnLoadMethod]
        static void Hook() => EditorApplication.update += OpenWhenAppReady;

        // Play 뒤 앱(AppRoot)이 뜨고 2초 지나면(처음 화면이 자리 잡게) 연다
        static void OpenWhenAppReady()
        {
            if (!SessionState.GetBool(OpenAfterPlay, false)) return;
            if (!EditorApplication.isPlaying)
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode) SessionState.SetBool(OpenAfterPlay, false); // Play가 취소됨
                return;
            }
            if (AppRoot.Nav == null) return;
            if (openAt == 0) openAt = EditorApplication.timeSinceStartup + 2;
            if (EditorApplication.timeSinceStartup < openAt) return;
            SessionState.SetBool(OpenAfterPlay, false);
            Open();
        }

        static async Task<bool> LogIn()
        {
            var login = await Api.Auth.KakaoLoginAsync(FakeNative.Config.kakaoToken);
            if (!login.IsNewUser) return true;
            Debug.LogWarning($"[인증] 카카오 토큰 {FakeNative.Config.kakaoToken}은 로컬 서버에 없는 사람입니다. " +
                             "앱에서 가입을 끝내거나 Fake Native 창의 토큰을 가입한 사람으로 바꾸세요.");
            return false;
        }

        static async void Open()
        {
            if (AppRoot.Nav == null)
            {
                Debug.LogWarning("[인증] 앱 씬(" + AppScene + ")을 Play한 뒤에 여세요.");
                return;
            }
            try
            {
                if (!Api.Session.IsLoggedIn && !await LogIn()) return;
                List<Course> courses;
                try
                {
                    courses = await Api.Courses.ListAsync();
                }
                catch (ApiException e) when (e.Failure == ApiFailure.NotLoggedIn)
                {
                    // 저장된 로그인이 서버에서 이미 무효(다른 곳에서 같은 기기로 로그인 등) → 가짜 토큰으로 다시 로그인하고 한 번 더
                    Api.ClearSavedLogin();
                    if (!await LogIn()) return;
                    courses = await Api.Courses.ListAsync();
                }
                if (courses == null || courses.Count == 0)
                {
                    Debug.LogWarning("[인증] 서버에 코스가 없습니다.");
                    return;
                }
                MapEntry entry;
                try
                {
                    entry = await Api.Courses.EnterAsync(courses[0].CourseId);
                }
                catch (ApiException e) when (e.Code == CourseText.CalibrationRequired)
                {
                    await Api.Users.PutCalibrationAsync(CalibrationStore.ToUpload(FakeSpeedSensor.SampleModel().ToJson()));
                    Debug.Log("[인증] 보정이 없어 예시 보정을 올렸습니다(로컬 서버).");
                    entry = await Api.Courses.EnterAsync(courses[0].CourseId);
                }
                var now = DateTime.UtcNow;
                var started = await Api.Runs.StartAsync(new RunStartBody
                {
                    ClientRunId = Guid.NewGuid().ToString(),
                    MapInstanceId = entry.MapInstanceId,
                    Goal = new RunGoalBody { Type = "NONE" },
                    StartedAt = now.AddSeconds(-290),
                });
                var sensor = new RunStatsBody { DistanceM = 1000, DurationS = 280 };
                await Api.Runs.FinishAsync(started.RunId, new RunFinishBody { EndedAt = now, Sensor = sensor });
                Debug.Log($"[인증] 기록 {started.RunId} (센서 1.00 km · 4:40) → 인증 화면. 결과는 Fake Native 창 '서버 가짜 OCR'");
                AppRoot.Nav.Push(new VerifyScreen(started.RunId, sensor, _ =>
                {
                    if (AppRoot.Nav.Current is VerifyScreen) AppRoot.Nav.Pop();
                    Debug.Log("[인증] 끝");
                }));
            }
            catch (Exception e)
            {
                Debug.LogError("[인증] 기록을 만들지 못했습니다: " + e.Message + " (서버 " + ApiConfig.BaseUrl + ")");
            }
        }
    }
}
