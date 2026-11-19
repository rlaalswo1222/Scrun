using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // G-10 친구 관리(계획 0044 4번). 목록(GET /friends: 달리는 친구 먼저, 최근에 달린 순)·"친구 N/50명".
    // 삭제는 줄 오른쪽 버튼이나 왼쪽으로 밀기(Figma 노트 "삭제는 스와이프") → G-11 확인 시트. 줄을 누르면 프로필.
    public sealed class FriendManageScreen : AppScreen
    {
        const float SwipeOpen = 60f; // 왼쪽으로 이만큼(패널 px) 밀면 삭제 확인

        VisualElement list;
        Label navTitle;
        StateView state;
        List<Friend> friends;
        FriendParts.Sheet sheet;
        bool loading;
        int edits; // 받는 동안 지운 친구가 있으면 그 전에 받은 목록으로 되살리지 않게

        protected override string Uxml => "UI/FriendManage";
        public override bool HidesTabBar => true;

        protected override void OnCreate()
        {
            Q<Button>("back").clicked += () => Nav.Pop();
            Q<Button>("add").clicked += () => { if (Nav.Current == this) Nav.Push(new FriendAddScreen()); };
            list = Q<VisualElement>("list");
            navTitle = Q<Label>("nav-title");
            state = new StateView(Q<VisualElement>("state"), SkeletonKind.List, "친구 목록을 불러오지 못했어요");
        }

        protected internal override void OnShow() => Load();

        protected internal override bool OnBack()
        {
            if (sheet?.Open != true) return false;
            sheet.Close();
            return true;
        }

        async void Load()
        {
            if (loading) return;
            loading = true;
            List<Friend> got = null;
            int editsAtStart = edits;
            // 처음만 스켈레톤(실패하면 그 자리에), 프로필에서 돌아오면 지금 목록을 둔 채 조용히(그동안 밀어서 지울 수 있음)
            bool ok = await Feedback.Load(state, friends != null, async () => got = await Api.Friends.ListAsync(), Load);
            loading = false;
            if (!ok || Root.panel == null) return;
            if (edits != editsAtStart)
            {
                Load(); // 받는 사이 지웠음 → 지운 뒤 목록으로 다시
                return;
            }
            friends = got ?? new List<Friend>();
            FriendDirectory.Set(friends);
            Build();
        }

        void Build()
        {
            navTitle.text = $"친구 {friends.Count}/{FriendText.MaxFriends}명";
            list.Clear();
            var now = DateTime.UtcNow;
            foreach (var f in friends)
            {
                var friend = f;
                var row = FriendParts.Row(f.Nickname, FriendText.StatusLine(f, now), f.RunningNow,
                    onTap: () =>
                    {
                        if (suppressTap) { suppressTap = false; return; } // 민 뒤 손을 뗀 것(누른 게 아님)
                        if (Nav.Current == this && sheet?.Open != true) Nav.Push(new FriendProfileScreen(friend.UserId));
                    },
                    action: FriendParts.Action("삭제", () => AskDelete(friend), danger: true));
                AddSwipe(row, friend);
                list.Add(row);
            }
            if (friends.Count == 0) state.Empty("아직 친구가 없어요");
            else state.Clear();
        }

        void AskDelete(Friend f)
        {
            if (sheet?.Open == true) return;
            sheet = FriendParts.ConfirmDelete(this, f.UserId, f.Nickname, friends.Count, () =>
            {
                edits++;
                friends.RemoveAll(x => x.UserId == f.UserId);
                Build();
            });
        }

        // 왼쪽으로 밀면 줄이 따라가고, 충분히 밀고 놓으면 삭제 확인. 위아래 스크롤로 보이면 그냥 둠
        void AddSwipe(Button row, Friend f)
        {
            int pointer = -1;
            UnityEngine.Vector2 start = default;
            bool swiping = false;
            row.RegisterCallback<PointerDownEvent>(e => { pointer = e.pointerId; start = e.position; swiping = false; suppressTap = false; }, TrickleDown.TrickleDown);
            row.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (e.pointerId != pointer) return;
                var d = (UnityEngine.Vector2)e.position - start;
                if (!swiping && Math.Abs(d.x) > 12f && Math.Abs(d.x) > Math.Abs(d.y) * 1.5f) swiping = true;
                if (!swiping) return;
                float x = Math.Min(0f, d.x);
                row.style.translate = new Translate(x, 0);
                row.EnableInClassList("fr-row--swiping", x <= -SwipeOpen);
            }, TrickleDown.TrickleDown);
            void End(bool released, float dx)
            {
                bool open = swiping && released && dx <= -SwipeOpen;
                bool wasSwiping = swiping;
                pointer = -1;
                swiping = false;
                row.style.translate = new Translate(0, 0);
                row.RemoveFromClassList("fr-row--swiping");
                if (open) AskDelete(f);
                if (wasSwiping) suppressTap = true; // 민 뒤 손을 뗄 때 줄 누르기(프로필)로 번지지 않게
            }
            row.RegisterCallback<PointerUpEvent>(e => { if (e.pointerId == pointer) End(true, e.position.x - start.x); }, TrickleDown.TrickleDown);
            row.RegisterCallback<PointerCancelEvent>(e => { if (e.pointerId == pointer) End(false, 0f); }, TrickleDown.TrickleDown);
        }

        bool suppressTap;
    }
}
