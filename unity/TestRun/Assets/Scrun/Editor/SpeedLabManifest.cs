using System.IO;
using UnityEditor.Android;

// 고속 센서 권한: 안드로이드 12+는 이게 없으면 가속도가 200Hz로 제한된다(일반 권한이라 사용자 확인 없음).
public class SpeedLabManifest : IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 0;

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        string file = Path.Combine(path, "src", "main", "AndroidManifest.xml");
        if (!File.Exists(file)) return;
        string xml = File.ReadAllText(file);
        const string perm = "android.permission.HIGH_SAMPLING_RATE_SENSORS";
        if (xml.Contains(perm)) return;
        int i = xml.IndexOf("<application");
        if (i < 0) return;
        xml = xml.Insert(i, "<uses-permission android:name=\"" + perm + "\" />\n  ");
        File.WriteAllText(file, xml);
    }
}
