using System.Collections.Generic;
using UnityEngine;

namespace Scrun.App
{
    // 로그인 토큰처럼 남이 보면 안 되는 작은 값을 기기에 저장하는 곳.
    // 아이폰은 키체인(계획 0018의 5번, Swift), 에디터는 가짜(PlayerPrefs).
    public interface ISecureStore
    {
        string Get(string key); // 없으면 null
        void Set(string key, string value);
        void Delete(string key);
    }

    // 에디터 전용 가짜. PlayerPrefs는 암호화가 없으므로 빌드에서는 쓰지 않는다(Native.cs).
    public sealed class FakeSecureStore : ISecureStore
    {
        const string Prefix = "scrun.fake-secure.";
        public static readonly FakeSecureStore Instance = new FakeSecureStore();

        public string Get(string key) => PlayerPrefs.HasKey(Prefix + key) ? PlayerPrefs.GetString(Prefix + key) : null;

        public void Set(string key, string value)
        {
            PlayerPrefs.SetString(Prefix + key, value);
            PlayerPrefs.Save();
        }

        public void Delete(string key)
        {
            PlayerPrefs.DeleteKey(Prefix + key);
            PlayerPrefs.Save();
        }
    }

    // 테스트·임시용. 앱을 끄면 사라진다.
    public sealed class MemorySecureStore : ISecureStore
    {
        readonly Dictionary<string, string> values = new Dictionary<string, string>();
        public string Get(string key) => values.TryGetValue(key, out var v) ? v : null;
        public void Set(string key, string value) => values[key] = value;
        public void Delete(string key) => values.Remove(key);
    }
}
