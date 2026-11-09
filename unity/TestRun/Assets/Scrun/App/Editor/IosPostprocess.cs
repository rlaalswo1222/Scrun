#if UNITY_IOS
using System.IO;
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

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS) return;
            string root = report.summary.outputPath;
            var local = ScrunLocalSettings.Load();
            EditPlist(Path.Combine(root, "Info.plist"));
            EditProject(PBXProject.GetPBXProjectPath(root), local);
            Debug.Log("[iOS] Info.plist·Xcode 설정 채움" + (string.IsNullOrEmpty(local.appleTeamId) ? " (서명 팀 없음: Xcode에서 고르기)" : ""));
        }

        static void EditPlist(string path)
        {
            var plist = new PlistDocument();
            plist.ReadFromFile(path);
            var d = plist.root;
            d.SetString("NSMotionUsageDescription", MotionText); // 헤드폰 모션도 이 문구를 씀
            d.SetString("NSCameraUsageDescription", CameraText);
            d.SetString("CFBundleDevelopmentRegion", "ko");
            d.SetBoolean("ITSAppUsesNonExemptEncryption", false); // HTTPS(운영체제 제공 암호화)만 씀
            plist.WriteToFile(path);
        }

        static void EditProject(string path, ScrunLocalSettings local)
        {
            var proj = new PBXProject();
            proj.ReadFromFile(path);
            string fw = proj.GetUnityFrameworkTargetGuid(); // Plugins/iOS의 Swift 파일이 들어가는 쪽
            string app = proj.GetUnityMainTargetGuid();

            proj.SetBuildProperty(fw, "SWIFT_VERSION", "5.0");
            foreach (var f in new[] { "CoreMotion.framework", "AVFoundation.framework", "UserNotifications.framework", "Security.framework" })
                proj.AddFrameworkToProject(fw, f, false);

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
