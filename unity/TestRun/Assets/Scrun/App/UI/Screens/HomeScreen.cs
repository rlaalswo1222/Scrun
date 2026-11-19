using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // 홈 탭 첫 화면(B-01 자리). 지금은 코스 고르기로 가는 버튼만 — 목표·아바타·친구·최근 기록은 S6(BL-31)에 채운다.
    public sealed class HomeScreen : AppScreen
    {
        protected override string Uxml => "UI/Home";

        protected override void OnCreate() =>
            Q<Button>("choose-course").clicked += () => { if (Nav.Current == this) Nav.Push(new CourseListScreen()); }; // 두 번 눌러도 하나만

        // 앱을 켠 뒤 처음이면 끝나지 않은 러닝을 묻고(I-04, 계획 0052), 지난번에 서버에 못 보낸 달리기 기록(기기에 남긴 것)을
        // 뒤에서 보낸다(계획 0037). 보내기 실패는 화면에 안 알림(다음에 다시)
        protected internal override void OnShow() => RecoveryPrompt.CheckOnShow(this, FlushPendingRuns);

        static async void FlushPendingRuns()
        {
            try { await PendingRunStore.Default.FlushAsync(RunRecordUploader.TokenSubject(Api.Session.AccessToken)); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}
