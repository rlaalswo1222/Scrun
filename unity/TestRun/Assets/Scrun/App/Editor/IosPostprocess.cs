#if UNITY_IOS
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace Scrun.App.Editor
{
    // Unity가 Xcode 프로젝트를 만든 직후 우리 Swift 브리지(Assets/Plugins/iOS/Scrun)에 필요한 것을 채운다.
    // 빌드 대상이 iOS일 때만 컴파일된다(Windows 팀원은 iOS 모듈이 없어도 됨).
    sealed class IosPostprocess : IPostprocessBuildWithReport
    {
        public int callbackOrder => 100;

        // 권한 팝업에 그대로 보이는 문구. 앱 심사는 "무엇에 쓰는지"가 구체적이어야 통과한다.
        const string MotionText = "러닝머신에서 달리는 속도를 폰과 에어팟의 움직임으로 계산하는 데 사용합니다.";
        const string CameraText = "달리기를 마친 뒤 러닝머신 계기판을 촬영해 기록을 인증하는 데 사용합니다.";

        // 카카오 iOS SDK(SPM). 2.23.0부터 SPM만 지원. 버전을 고정해 빌드마다 같은 SDK가 들어가게 한다
        const string KakaoSdkUrl = "https://github.com/kakao/kakao-ios-sdk";
        const string KakaoSdkVersion = "2.30.0";
        static readonly string[] KakaoModules = { "KakaoSDKCommon", "KakaoSDKAuth", "KakaoSDKUser" };

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS) return;
            string root = report.summary.outputPath;
            var local = ScrunLocalSettings.Load();
            string kakaoKey = (local.kakaoNativeAppKey ?? "").Trim();
            var keyError = local.KakaoKeyError();
            if (keyError != null) throw new BuildFailedException(keyError);
            EditPlist(Path.Combine(root, "Info.plist"), kakaoKey);
            EditProject(PBXProject.GetPBXProjectPath(root), local, kakaoKey);
            Debug.Log("[iOS] Info.plist·Xcode 설정 채움" +
                      (string.IsNullOrEmpty(local.appleTeamId) ? " (서명 팀 없음: Xcode에서 고르기)" : "") +
                      (kakaoKey.Length == 0 ? " (카카오 앱 키 없음: SDK 없이 빌드, 로그인은 오류)" : $" (카카오 SDK {KakaoSdkVersion})"));
            if (kakaoKey.Length == 0)
                Debug.LogWarning("[iOS] ScrunLocal.json에 kakaoNativeAppKey가 없어 카카오 로그인이 빠진 빌드입니다.");
        }

        static void EditPlist(string path, string kakaoKey)
        {
            var plist = new PlistDocument();
            plist.ReadFromFile(path);
            var d = plist.root;
            d.SetString("NSMotionUsageDescription", MotionText); // 헤드폰 모션도 이 문구를 씀
            d.SetString("NSCameraUsageDescription", CameraText);
            d.SetString("CFBundleDevelopmentRegion", "ko");
            d.SetBoolean("ITSAppUsesNonExemptEncryption", false); // HTTPS(운영체제 제공 암호화)만 씀

            if (kakaoKey.Length > 0)
            {
                d.SetString("KAKAO_APP_KEY", kakaoKey); // ScrunKakao.swift가 읽어 SDK 초기화
                // 카카오톡에서 로그인 결과를 들고 돌아오는 주소
                // (이미 있으면 다시 넣지 않음: Player Settings의 URL 스킴·Append 빌드와 겹치지 않게)
                string kakaoScheme = "kakao" + kakaoKey;
                var types = d["CFBundleURLTypes"]?.AsArray() ?? d.CreateArray("CFBundleURLTypes");
                bool hasScheme = types.values.Any(v =>
                    ((v as PlistElementDict)?["CFBundleURLSchemes"] as PlistElementArray)?.values
                        .Any(x => (x as PlistElementString)?.value == kakaoScheme) == true);
                if (!hasScheme)
                {
                    var t = types.AddDict();
                    t.SetString("CFBundleURLName", "kakao");
                    t.CreateArray("CFBundleURLSchemes").AddString(kakaoScheme);
                }
                // 카카오톡이 깔려 있는지 확인하고 여는 데 필요(카카오 문서의 허용 목록)
                var q = d["LSApplicationQueriesSchemes"]?.AsArray() ?? d.CreateArray("LSApplicationQueriesSchemes");
                foreach (var scheme in new[] { "kakaokompassauth", "kakaolink" })
                    if (!q.values.Any(x => (x as PlistElementString)?.value == scheme)) q.AddString(scheme);
            }
            plist.WriteToFile(path);
        }

        static void EditProject(string path, ScrunLocalSettings local, string kakaoKey)
        {
            var proj = new PBXProject();
            proj.ReadFromFile(path);
            string fw = proj.GetUnityFrameworkTargetGuid(); // Plugins/iOS의 Swift 파일이 들어가는 쪽
            string app = proj.GetUnityMainTargetGuid();

            proj.SetBuildProperty(fw, "SWIFT_VERSION", "5.0");
            foreach (var f in new[] { "CoreMotion.framework", "AVFoundation.framework", "UserNotifications.framework", "Security.framework" })
                proj.AddFrameworkToProject(fw, f, false);

            if (kakaoKey.Length > 0)
            {
                // Swift 파일(Plugins/iOS)이 UnityFramework에 들어가므로 SDK도 거기에 붙인다
                string pkg = proj.AddRemotePackageReferenceAtVersion(KakaoSdkUrl, KakaoSdkVersion);
                foreach (var m in KakaoModules) proj.AddRemotePackageFrameworkToProject(fw, m, pkg, false);
            }

            if (!string.IsNullOrEmpty(local.appleTeamId))
            {
                foreach (var t in new[] { app, fw })
                {
                    proj.SetBuildProperty(t, "DEVELOPMENT_TEAM", local.appleTeamId);
                    proj.SetBuildProperty(t, "CODE_SIGN_STYLE", "Automatic");
                }
            }
            proj.WriteToFile(path);
        }
    }
}
#endif
