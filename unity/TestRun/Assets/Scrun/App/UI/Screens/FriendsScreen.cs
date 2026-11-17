using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // 친구 탭 첫 화면(G-01·G-02·G-04, 계획 0044 3번). 보일 때마다 친구 목록·달리는 친구·피드 첫 쪽을 새로 받고,
    // 보이는 동안 달리는 친구는 15초마다 조용히 다시 받는다(누가 막 달리기 시작했을 때 탭을 다시 열지 않아도 되게).
    // 맨 위에서 아래로 끌어 놓아도 새로 받음. 친구가 0명이면 G-04.
    public sealed class FriendsScreen : AppScreen
    {
        const long RunningRefreshMs = 15000;

        VisualElement runningList, runningEmpty, feedList, noFriends, loadFailed;
        ScrollView scroll;
        Label runningTitle, feedEmpty;
        Button feedMore, manage;
        PullToRefresh pull;
        IVisualElementScheduledItem poll;
        List<Friend> friends;
        List<RunningFriend> running = new List<RunningFriend>();
        string feedCursor;
        bool loading, loadingMore, polling;

        protected override string Uxml => "UI/Friends";

        protected override void OnCreate()
        {
            scroll = Q<ScrollView>("scroll");
            runningTitle = Q<Label>("running-title");
            runningList = Q<VisualElement>("running-list");
            runningEmpty = Q<VisualElement>("running-empty");
            feedList = Q<VisualElement>("feed-list");
            feedEmpty = Q<Label>("feed-empty");
            feedMore = Q<Button>("feed-more");
            noFriends = Q<VisualElement>("no-friends");
            loadFailed = Q<VisualElement>("load-failed");
            manage = Q<Button>("manage");
            manage.clicked += () => { if (Nav.Current == this) Nav.Push(new FriendManageScreen()); };
            Q<Button>("add").clicked += OpenAdd;
            Q<Button>("add-first").clicked += OpenAdd;
            Q<Button>("reload").clicked += () => Load();
            feedMore.clicked += LoadMoreFeed;
            pull = new PullToRefresh(scroll, Q<Label>("pull"), () => Load(fromPull: true));
            ShowState(null);
        }

        protected internal override void OnShow()
        {
            Load();
            poll ??= Root.schedule.Execute(PollRunning).Every(RunningRefreshMs);
            poll.Resume();
        }

        protected internal override void OnHide() => poll?.Pause();

        protected internal override void OnDestroy() => poll?.Pause();

        void OpenAdd()
        {
            if (Nav.Current == this) Nav.Push(new FriendAddScreen());
        }

        async void Load(bool fromPull = false)
        {
            if (loading) return;
            loading = true;
            if (fromPull) pull.Busy(true);
            List<Friend> gotFriends = null;
            List<RunningFriend> gotRunning = null;
            FeedPage gotFeed = null;
            bool ok = await Feedback.Run(async () =>
            {
                var f = Api.Friends.ListAsync();
                var r = Api.Friends.RunningAsync();
                var p = Api.Friends.FeedAsync();
                await Task.WhenAll(f, r, p);
                gotFriends = f.Result;
                gotRunning = r.Result;
                gotFeed = p.Result;
            }, () => Load());
            loading = false;
            pull.Busy(false);
            if (Root.panel == null) return; // 기다리는 사이 화면을 떠남
            if (!ok)
            {
                if (friends == null) ShowState(failed: true);
                return;
            }
            friends = gotFriends ?? new List<Friend>();
            FriendDirectory.Set(friends);
            running = gotRunning ?? new List<RunningFriend>();
            ShowState(failed: false);
            BuildRunning();
            feedList.Clear();
            feedCursor = null;
            AddFeed(gotFeed);
        }

        // 보이는 동안 달리는 친구만 조용히(실패해도 안내 없음, 다음 차례에 다시)
        async void PollRunning()
        {
            if (polling || loading || friends == null || friends.Count == 0 || Nav.Current != this) return;
            polling = true;
            try
            {
                var got = await Api.Friends.RunningAsync();
                if (Root.panel != null && got != null)
                {
                    running = got;
                    BuildRunning();
                }
            }
            catch (ApiException) { }
            finally { polling = false; }
        }

        async void LoadMoreFeed()
        {
            if (loadingMore || feedCursor == null) return;
            loadingMore = true;
            FeedPage got = null;
            string cursor = feedCursor;
            bool ok = await Feedback.Run(async () => got = await Api.Friends.FeedAsync(cursor), LoadMoreFeed);
            loadingMore = false;
            if (ok && Root.panel != null && cursor == feedCursor) AddFeed(got);
        }

        // failed: null이면 아직 모름(처음 불러오는 중)
        void ShowState(bool? failed)
        {
            bool none = failed == false && friends.Count == 0;
            scroll.SetVisible(failed == false && !none);
            noFriends.SetVisible(none);
            manage.SetVisible(failed == false && !none); // G-04엔 "관리" 없음
            loadFailed.SetVisible(failed == true);
        }

        void BuildRunning()
        {
            runningList.Clear();
            runningTitle.text = running.Count > 0 ? $"지금 달리는 친구 {running.Count}" : "지금 달리는 친구";
            foreach (var f in running)
            {
                var friend = f;
                runningList.Add(FriendParts.Row(f.Nickname, FriendText.RunningLine(f), live: true,
                    onTap: () => OpenProfile(friend.UserId),
                    action: FriendParts.Action("같이 달리기", () => FriendParts.RunTogether(this, friend))));
            }
            runningEmpty.SetVisible(running.Count == 0);
        }

        void AddFeed(FeedPage page)
        {
            var now = DateTime.UtcNow;
            foreach (var item in page?.Items ?? new List<FeedItem>())
            {
                string userId = item.UserId;
                feedList.Add(FriendParts.FeedCard(item, now, () => OpenProfile(userId)));
            }
            feedCursor = page?.NextCursor;
            feedEmpty.SetVisible(feedList.childCount == 0);
            feedMore.SetVisible(feedCursor != null);
        }

        void OpenProfile(string userId)
        {
            if (Nav.Current == this) Nav.Push(new FriendProfileScreen(userId));
        }
    }
}
