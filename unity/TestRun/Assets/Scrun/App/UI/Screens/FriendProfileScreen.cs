using System.Linq;
using System.Threading.Tasks;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // G-09 친구 프로필(계획 0044 4번). 프로필(GET /friends/{id})과 달리는 친구(GET /friends/running)를 같이 받아,
    // 달리는 중이면 "같이 달리기"(G-03) — 코스는 달리는 친구 쪽에 있음. 더보기 → 삭제(G-11) → 지우면 앞 화면으로.
    // 그 사이 친구가 끊겼으면(404) 화면 안에서 알리고 "친구 목록으로".
    public sealed class FriendProfileScreen : AppScreen
    {
        readonly string userId;
        FriendProfile profile;
        RunningFriend running;
        VisualElement content, head, chips, runs, loadFailed;
        Label name, notRunning, runsEmpty, loadFailedText;
        Button runTogether, more, reload;
        FriendParts.Sheet sheet;
        bool loading, gone;

        public FriendProfileScreen(string userId) => this.userId = userId;

        protected override string Uxml => "UI/FriendProfile";
        public override bool HidesTabBar => true;

        protected override void OnCreate()
        {
            Q<Button>("back").clicked += () => Nav.Pop();
            content = Q<VisualElement>("content");
            head = Q<VisualElement>("head");
            name = Q<Label>("name");
            chips = Q<VisualElement>("chips");
            runTogether = Q<Button>("run-together");
            notRunning = Q<Label>("not-running");
            runs = Q<VisualElement>("runs");
            runsEmpty = Q<Label>("runs-empty");
            loadFailed = Q<VisualElement>("load-failed");
            loadFailedText = Q<Label>("load-failed-text");
            reload = Q<Button>("reload");
            more = Q<Button>("more");
            more.clicked += OpenMore;
            runTogether.clicked += () => { if (running != null && sheet?.Open != true) sheet = FriendParts.RunTogether(this, running); };
            reload.clicked += () => { if (gone) Nav.Pop(); else Load(); };
            content.SetVisible(false);
            loadFailed.SetVisible(false);
            more.SetEnabled(false);
        }

        protected internal override void OnShow()
        {
            if (!gone) Load();
        }

        protected internal override bool OnBack()
        {
            if (sheet?.Open != true) return false;
            sheet.Close(); // 시트가 열려 있으면 시트만 닫음
            return true;
        }

        async void Load()
        {
            if (loading) return;
            loading = true;
            FriendProfile p = null;
            RunningFriend r = null;
            bool ok = await Feedback.Run(async () =>
            {
                var pt = Api.Friends.ProfileAsync(userId);
                var rt = Api.Friends.RunningAsync();
                await Task.WhenAll(pt, rt);
                p = pt.Result;
                r = rt.Result?.FirstOrDefault(f => f.UserId == userId);
            }, Load, OnLoadFailure);
            loading = false;
            if (Root.panel == null) return;
            if (gone)
            {
                ShowGone();
                return;
            }
            if (!ok)
            {
                if (profile == null) loadFailed.SetVisible(true);
                return;
            }
            profile = p;
            running = p.RunningNow ? r : null; // 방금 멈췄으면 목록엔 아직 남아 있을 수 있음 → 프로필 기준
            Render();
        }

        void Render()
        {
            content.SetVisible(true);
            loadFailed.SetVisible(false);
            more.SetEnabled(true);
            head.Clear();
            head.Add(FriendParts.Head(profile.Nickname, 88));
            name.text = profile.Nickname;
            chips.Clear();
            if (profile.RunningNow) chips.Add(FriendParts.Chip("달리는 중", FriendParts.ChipKind.Live));
            chips.Add(FriendParts.Chip($"인증 마크 {profile.VerifiedCount}개", FriendParts.ChipKind.Success));
            if (profile.Level is int lv) chips.Add(FriendParts.Chip($"Lv.{lv}", FriendParts.ChipKind.Gray));
            runTogether.SetEnabled(running != null);
            notRunning.SetVisible(running == null);
            runs.Clear();
            foreach (var run in profile.RecentRuns ?? Enumerable.Empty<FriendRun>()) runs.Add(FriendParts.RecordRow(run));
            runsEmpty.SetVisible(runs.childCount == 0);
        }

        // 그 사이 끊긴 친구(친구가 아니면 404): 화면 안에서 알림
        bool OnLoadFailure(ApiException e)
        {
            if (e.Failure != ApiFailure.Server || e.Code != "USER_NOT_FOUND") return false;
            gone = true;
            return true;
        }

        void ShowGone()
        {
            FriendDirectory.Remove(userId);
            content.SetVisible(false);
            more.SetEnabled(false);
            loadFailedText.text = "더 이상 친구가 아니에요"; // 임시 문구(상준)
            reload.text = "친구 목록으로";
            loadFailed.SetVisible(true);
        }

        // 더보기(G-09 노트 "더보기: 친구 삭제") → 바로 G-11 확인 시트
        void OpenMore()
        {
            if (profile == null || sheet?.Open == true) return;
            sheet = FriendParts.ConfirmDelete(this, userId, profile.Nickname, FriendDirectory.Count, () =>
            {
                if (Nav.Current == this) Nav.Pop();
            });
        }
    }
}
