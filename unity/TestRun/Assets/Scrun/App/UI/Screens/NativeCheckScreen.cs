using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // 개발 빌드 전용 점검 화면: 권한 4종 요청·설정 앱 열기, 이어폰 연결 상태, 키체인 저장·읽기·지우기를 눌러 본다.
    // 로그인 화면 아래 "네이티브 점검" 버튼(개발 빌드에서만 보임)으로 들어온다. 계획 0018의 5번 확인용,
    // 지환의 권한·에어팟 화면이 나오면 그 화면으로 같은 것을 확인하고 이 화면은 개발 도구로만 남긴다.
    public sealed class NativeCheckScreen : AppScreen
    {
        protected override string Uxml => "UI/NativeCheck";
        public override bool HidesTabBar => true;

        readonly Dictionary<PermissionKind, Label> statusLabels = new Dictionary<PermissionKind, Label>();
        Label headphones, log;
        readonly List<string> lines = new List<string>();

        protected override void OnCreate()
        {
            Q<Button>("back").clicked += () => Nav.Pop();
            var perms = Q<VisualElement>("perms");
            foreach (PermissionKind k in Enum.GetValues(typeof(PermissionKind)))
            {
                var row = new VisualElement();
                row.AddToClassList("field-row");
                var label = new Label();
                label.AddToClassList("body-text");
                label.style.flexGrow = 1;
                var ask = new Button(() => Request(k)) { text = "요청" };
                ask.AddToClassList("btn");
                ask.AddToClassList("btn-secondary");
                row.Add(label);
                row.Add(ask);
                perms.Add(row);
                statusLabels[k] = label;
            }
            headphones = Q<Label>("headphones");
            log = Q<Label>("log");
            Q<Button>("settings").clicked += () =>
            {
                Native.Permissions.OpenSettings();
                Log("설정 앱 열기");
            };
            Q<Button>("keychain").clicked += CheckKeychain;
        }

        protected internal override void OnShow()
        {
            Native.Permissions.Changed += OnPermission;
            Native.Headphones.Changed += OnHeadphones;
            RefreshAll();
        }

        protected internal override void OnHide()
        {
            Native.Permissions.Changed -= OnPermission;
            Native.Headphones.Changed -= OnHeadphones;
        }

        void RefreshAll()
        {
            foreach (var kv in statusLabels) kv.Value.text = $"{kv.Key}: {Native.Permissions.GetStatus(kv.Key)}";
            var h = Native.Headphones;
            headphones.text = $"이어폰: {h.Status}" + (h.DeviceName != null ? $" ({h.DeviceName})" : "");
        }

        async void Request(PermissionKind k)
        {
            Log($"{k} 요청…");
            try
            {
                var s = await Native.Permissions.RequestAsync(k);
                Log($"{k} 요청 결과: {s}");
            }
            catch (Exception e)
            {
                Log($"{k} 요청 실패: {e.Message}");
            }
            if (Root.panel != null) RefreshAll();
        }

        void OnPermission(PermissionKind k, PermissionStatus s)
        {
            Log($"이벤트 권한 {k} → {s}");
            RefreshAll();
        }

        void OnHeadphones(HeadphoneStatus s)
        {
            Log($"이벤트 이어폰 → {s}");
            RefreshAll();
        }

        void CheckKeychain()
        {
            const string key = "native-check";
            try
            {
                var store = Native.SecureStore;
                string before = store.Get(key);
                string value = "확인 " + DateTime.Now.ToString("HH:mm:ss");
                store.Set(key, value);
                string read = store.Get(key);
                store.Delete(key);
                string after = store.Get(key);
                bool ok = read == value && after == null;
                Log($"키체인 {(ok ? "정상" : "이상")}: 전 {before ?? "없음"} / 읽음 {read} / 지운 뒤 {after ?? "없음"}");
            }
            catch (Exception e)
            {
                Log("키체인 실패: " + e.Message);
            }
        }

        void Log(string line)
        {
            Debug.Log("[NativeCheck] " + line);
            lines.Insert(0, $"{DateTime.Now:HH:mm:ss} {line}");
            if (lines.Count > 30) lines.RemoveAt(lines.Count - 1);
            if (log != null) log.text = string.Join("\n", lines);
        }
    }
}
