using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // 친구 화면들(G-01–G-11, 계획 0044)이 같이 쓰는 조각. 모양은 Friends.uss.
    static class FriendParts
    {
        // 동그란 머리(Head). 프로필 그림이 아직 없어(아바타는 BL-33) 닉네임 첫 글자. live면 아래 LIVE(Live pill)
        public static VisualElement Head(string nickname, int size = 46, bool live = false)
        {
            var head = new VisualElement { pickingMode = PickingMode.Ignore };
            head.AddToClassList("fr-head");
            if (size != 46) head.AddToClassList("fr-head--" + size);
            head.Add(UiExtensions.Text(Initial(nickname), "fr-head-mark"));
            if (live) head.Add(UiExtensions.Text("LIVE", "fr-live-pill"));
            return head;
        }

        public static string Initial(string nickname) =>
            string.IsNullOrEmpty(nickname) ? "?" : char.IsSurrogate(nickname[0]) && nickname.Length > 1 ? nickname.Substring(0, 2) : nickname.Substring(0, 1);

        public enum ChipKind { Gray, Success, Live }

        public static VisualElement Chip(string text, ChipKind kind)
        {
            var chip = new VisualElement { pickingMode = PickingMode.Ignore };
            chip.AddToClassList("fr-chip");
            if (kind == ChipKind.Success) chip.AddToClassList("fr-chip--success");
            if (kind == ChipKind.Live)
            {
                chip.AddToClassList("fr-chip--live");
                var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.AddToClassList("fr-chip-dot");
                chip.Add(dot);
            }
            chip.Add(UiExtensions.Text(text, "fr-chip-text"));
            return chip;
        }

        // 인증 칩. 인증 대기는 칩 없음(null)
        public static VisualElement VerificationChip(string verification)
        {
            var c = FriendText.VerificationChip(verification);
            return c == null ? null : Chip(c.Value.text, c.Value.verified ? ChipKind.Success : ChipKind.Gray);
        }

        // 친구 줄(Friend Row): 머리 + 이름·설명 + 오른쪽 버튼(없으면 null). 줄을 누르면 onTap
        public static Button Row(string nickname, string sub, bool live, Action onTap, Button action = null)
        {
            var row = new Button(() => onTap?.Invoke());
            row.AddToClassList("fr-row");
            row.Add(Head(nickname, 46, live));
            var text = new VisualElement { pickingMode = PickingMode.Ignore };
            text.AddToClassList("fr-row-text");
            text.Add(UiExtensions.Text(nickname, "fr-name"));
            text.Add(UiExtensions.Text(sub, "fr-sub"));
            row.Add(text);
            if (action != null) row.Add(action);
            return row;
        }

        // 줄 오른쪽 버튼(같이 달리기·삭제). 누르기가 바깥 줄(Button)로 올라가 줄 누르기(프로필)까지 되지 않게 막음
        public static Button Action(string text, Action onClick, bool danger = false)
        {
            var b = new Button(() => onClick?.Invoke()) { text = text };
            b.AddToClassList("fr-action");
            if (danger) b.AddToClassList("fr-action--danger");
            b.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            b.RegisterCallback<PointerUpEvent>(e => e.StopPropagation());
            return b;
        }

        // G-01 피드 카드
        public static Button FeedCard(FeedItem f, DateTime nowUtc, Action onTap)
        {
            var card = new Button(() => onTap?.Invoke());
            card.AddToClassList("fr-feed-card");
            var top = new VisualElement { pickingMode = PickingMode.Ignore };
            top.AddToClassList("fr-feed-top");
            top.Add(Head(f.Nickname, 40));
            var who = new VisualElement { pickingMode = PickingMode.Ignore };
            who.AddToClassList("fr-row-text");
            who.Add(UiExtensions.Text(f.Nickname, "fr-name"));
            who.Add(UiExtensions.Text(FriendText.Ago(f.EndedAt, nowUtc), "fr-time"));
            top.Add(who);
            var chip = VerificationChip(f.Verification);
            if (chip != null) top.Add(chip);
            card.Add(top);
            var stats = new VisualElement { pickingMode = PickingMode.Ignore };
            stats.AddToClassList("fr-feed-stats");
            stats.Add(UiExtensions.Text(f.CourseName ?? "", "fr-feed-course"));
            stats.Add(UiExtensions.Text(FriendText.RunStats(f), "fr-feed-numbers"));
            card.Add(stats);
            return card;
        }

        // G-09 최근 러닝 줄(Record Row)
        public static VisualElement RecordRow(FriendRun r)
        {
            var row = new VisualElement();
            row.AddToClassList("fr-record");
            var text = new VisualElement { pickingMode = PickingMode.Ignore };
            text.AddToClassList("fr-row-text");
            text.style.marginLeft = 0;
            text.Add(UiExtensions.Text(FriendText.RunDate(r.StartedAt), "fr-record-date"));
            text.Add(UiExtensions.Text(FriendText.Km(r.DistanceM), "fr-record-km"));
            text.Add(UiExtensions.Text(FriendText.RunDetail(r), "fr-sub"));
            row.Add(text);
            var chip = VerificationChip(r.Verification);
            if (chip != null) row.Add(chip);
            return row;
        }

        // ---- 아래 시트 ----

        // 화면 위에 어둡게 덮고 아래 시트. 바깥(어두운 곳)을 누르거나 Close()로 닫음
        public sealed class Sheet
        {
            public readonly VisualElement Dim, Body;

            public Sheet(VisualElement screenRoot)
            {
                Dim = new VisualElement { name = "fr-sheet" };
                Dim.AddToClassList("fr"); // 화면 Root(TemplateContainer)에 바로 붙어 .fr 아래가 아니므로 글자 규칙(.fr .x)이 닿게
                Dim.AddToClassList("fr-dim");
                Body = new VisualElement();
                Body.AddToClassList("fr-sheet");
                var grab = new VisualElement { pickingMode = PickingMode.Ignore };
                grab.AddToClassList("fr-grabber");
                Body.Add(grab);
                Body.RegisterCallback<ClickEvent>(e => e.StopPropagation());
                Dim.RegisterCallback<ClickEvent>(_ => Close());
                Dim.Add(Body);
                screenRoot.Add(Dim);
            }

            public bool Open => Dim.parent != null;

            public void Close() => Dim.RemoveFromHierarchy();

            // 머리 + 제목·설명 + (오른쪽 칩)
            public void Head(string nickname, string title, string sub, VisualElement chip = null)
            {
                var row = new VisualElement();
                row.AddToClassList("fr-sheet-head");
                row.Add(FriendParts.Head(nickname, 56));
                var text = new VisualElement();
                text.AddToClassList("fr-row-text");
                text.Add(UiExtensions.Text(title, "fr-sheet-title"));
                if (sub != null) text.Add(UiExtensions.Text(sub, "fr-sub"));
                row.Add(text);
                if (chip != null) row.Add(chip);
                Body.Add(row);
            }

            public void Info(string text) => Body.Add(UiExtensions.Text(text, "fr-sheet-info"));

            public Button Button(string text, string kind, Action onClick, string name = null)
            {
                var b = new Button(() => onClick?.Invoke()) { text = text, name = name };
                b.AddToClassList("btn");
                b.AddToClassList(kind);
                Body.Add(b);
                return b;
            }
        }

        // G-03 같이 달리기 확인. 확인하면 친구가 달리는 맵에 친구 뒤로 입장(계획 0047, 친구가 그새 끝냈으면 그 코스로 일반 입장)
        public static Sheet RunTogether(AppScreen screen, RunningFriend f)
        {
            var sheet = new Sheet(screen.Root);
            sheet.Head(f.Nickname, $"{f.Nickname} 님이 달리는 중", FriendText.RunTogetherLine(f), Chip("LIVE", ChipKind.Live));
            sheet.Info("입장하면 화면이 가로로 전환돼요. 러닝머신에 폰을 거치해 주세요. 친구 근처에서 시작해요.");
            string courseId = f.CourseId;
            sheet.Button("같이 달리기", "btn-primary", () =>
            {
                sheet.Close();
                if (screen.Nav.Current == screen) screen.Nav.Push(new CourseDetailScreen(courseId, enterOnLoad: true, together: f));
            }, "run-together-go");
            sheet.Button("닫기", "btn-ghost", sheet.Close, "run-together-close");
            return sheet;
        }

        // G-11 삭제 확인. 지우면 FriendDirectory에서도 빼고 onDeleted
        public static Sheet ConfirmDelete(AppScreen screen, string userId, string nickname, int friendCount, Action onDeleted)
        {
            var sheet = new Sheet(screen.Root);
            sheet.Head(nickname, $"{nickname} 님을 삭제할까요?", friendCount > 0 ? $"친구 {friendCount}명 중 한 명" : null);
            sheet.Info("친구 피드와 같이 달리기 목록에서 더 이상 보이지 않아요. 다시 추가하려면 친구 코드가 필요해요.");
            bool busy = false;
            sheet.Button("친구 삭제", "btn-danger", async () =>
            {
                if (busy) return;
                busy = true;
                bool alreadyGone = false;
                bool ok = await Feedback.Run(() => Api.Friends.DeleteAsync(userId), onError: e =>
                {
                    // 그 사이 상대가 먼저 끊음 → 이미 친구가 아님: 지운 것과 같게
                    alreadyGone = e.Failure == ApiFailure.Server && e.Code == "USER_NOT_FOUND";
                    return alreadyGone;
                });
                busy = false;
                if (!ok && !alreadyGone) return;
                sheet.Close();
                FriendDirectory.Remove(userId);
                onDeleted?.Invoke();
            }, "delete-confirm");
            sheet.Button("취소", "btn-ghost", sheet.Close, "delete-cancel");
            return sheet;
        }
    }

    // 맨 위에서 아래로 끌어 놓으면 새로고침(G-01, 계획 0044 3번). ScrollView보다 먼저(TrickleDown) 손가락을 본다
    sealed class PullToRefresh
    {
        const float Threshold = 70f; // 이만큼(패널 px) 끌면 놓을 때 새로고침
        readonly ScrollView scroll;
        readonly Label hint;
        readonly Action refresh;
        int pointer = -1;
        float startY;
        bool armed;

        public PullToRefresh(ScrollView scroll, Label hint, Action refresh)
        {
            this.scroll = scroll;
            this.hint = hint;
            this.refresh = refresh;
            scroll.RegisterCallback<PointerDownEvent>(OnDown, TrickleDown.TrickleDown);
            scroll.RegisterCallback<PointerMoveEvent>(OnMove, TrickleDown.TrickleDown);
            scroll.RegisterCallback<PointerUpEvent>(OnUp, TrickleDown.TrickleDown);
            scroll.RegisterCallback<PointerCancelEvent>(_ => Reset(), TrickleDown.TrickleDown);
        }

        public void Busy(bool on)
        {
            hint.text = on ? "새로고침 중" : "";
            hint.EnableInClassList("fr-pull--on", on);
        }

        void OnDown(PointerDownEvent e)
        {
            if (scroll.scrollOffset.y > 1f) return; // 맨 위에서 시작할 때만
            pointer = e.pointerId;
            startY = e.position.y;
            armed = false;
        }

        void OnMove(PointerMoveEvent e)
        {
            if (e.pointerId != pointer) return;
            bool now = e.position.y - startY >= Threshold;
            if (now == armed) return;
            armed = now;
            hint.text = armed ? "놓으면 새로고침" : "";
            hint.EnableInClassList("fr-pull--on", armed);
        }

        void OnUp(PointerUpEvent e)
        {
            if (e.pointerId != pointer) return;
            bool go = armed;
            Reset();
            if (go) refresh();
        }

        void Reset()
        {
            pointer = -1;
            if (!armed) return;
            armed = false;
            hint.text = "";
            hint.EnableInClassList("fr-pull--on", false);
        }
    }
}
