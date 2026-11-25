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

        VisualElement runningList, runningEmpty, feedList, noFriends;
        StateView state;
        ScrollView scroll;
        Label runningTitle, feedEmpty;
        Button feedMore, manage;
        PullToRefresh pull;
        IVisualElementScheduledItem poll;
        List<Friend> friends;
        List<RunningFriend> running = new List<RunningFriend>();
        string feedCursor;
        bool loading, loadingMore, polling;
        int loadSeq; // 끌어서 새로고침이 조용히 받는 중인 것을 이어받으면 늦게 온 옛 응답을 버림

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
            state = new StateView(Q<VisualElement>("state"), SkeletonKind.List, "친구 목록을 불러오지 못했어요").Padded();
            manage = Q<Button>("manage");
            manage.clicked += () => { if (Nav.Current == this) Nav.Push(new FriendManageScreen()); };
            Q<Button>("add").clicked += OpenAdd;
            Q<Button>("add-first").clicked += OpenAdd;
            feedMore.clicked += LoadMoreFeed;
            pull = new PullToRefresh(scroll, Q<Label>("pull"), () => Load(fromPull: true));
            ShowState(loaded: false);
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
            if (loading && !fromPull) return; // 끌어서 새로고침은 조용히 받는 중이어도 새로 받음(손으로 당겼는데 아무 표시 없이 무시되지 않게)
            loading = true;
            int seq = ++loadSeq;
            if (fromPull) pull.Busy(true);
            List<Friend> gotFriends = null;
            List<RunningFriend> gotRunning = null;
            FeedPage gotFeed = null;
            Func<Task> work = async () =>
            {
                var f = Api.Friends.ListAsync();
                var r = Api.Friends.RunningAsync();
                var p = Api.Friends.FeedAsync();
                await Task.WhenAll(f, r, p);
                gotFriends = f.Result;
                gotRunning = r.Result;
                gotFeed = p.Result;
            };
            // 앱을 다시 켰을 때: 지난 응답(캐시)으로 먼저 그림(계획 0074 A). 친구 목록이 있어야 그림(달리는 친구·피드는 비어도 됨)
            if (friends == null && !fromPull && Api.Friends.CachedList() is List<Friend> cachedFriends)
            {
                Apply(cachedFriends, Api.Friends.CachedRunning(), Api.Friends.CachedFeed());
                state.Clear(); // 공통 칸의 스켈레톤·빈 상태 표시를 지움(아래 Feedback.Load가 조용히 받음)
            }
            // 처음(아직 보여 줄 게 없음)만 스켈레톤(실패하면 그 자리에), 탭에 다시 들어오면 지금 목록을 둔 채 조용히.
            // 끌어서 새로고침은 위 표시만 두고 실패는 안내하되, 그새 다른 화면으로 갔으면 그 화면 위에 띄우지 않음
            bool ok = fromPull && friends != null
                ? await Feedback.Run(work, () => Load(fromPull: true), e => e.Failure != ApiFailure.NotLoggedIn && Nav.Current != this, overlay: false)
                : await Feedback.Load(state, friends != null, work, () => Load(), null, () => seq == loadSeq);
            if (seq != loadSeq) return; // 더 새로 받는 중
            loading = false;
            pull.Busy(false);
            if (Root.panel == null) return; // 기다리는 사이 화면을 떠남
            if (!ok) return; // 처음이면 공통 칸이 실패를 보임, 아니면 지난 목록 그대로
            Apply(gotFriends, gotRunning, gotFeed);
        }

        void Apply(List<Friend> gotFriends, List<RunningFriend> gotRunning, FeedPage gotFeed)
        {
            friends = gotFriends ?? new List<Friend>();
            FriendDirectory.Set(friends);
            running = gotRunning ?? new List<RunningFriend>();
            ShowState(loaded: true);
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

        // loaded: 받은 값이 있음(없으면 받는 중·실패 — 그때 보이는 건 공통 칸 state)
        void ShowState(bool loaded)
        {
            bool none = loaded && friends.Count == 0;
            scroll.SetVisible(loaded && !none);
            noFriends.SetVisible(none);
            manage.SetVisible(loaded && !none); // G-04엔 "관리" 없음
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
