using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // List: 동그라미 + 두 줄 ×5(친구·기록), Cards: 카드 ×3(코스·주간 목표), Detail: 큰 그림 + 줄(상세), Profile: 프로필 한 줄 + 숫자 칸(마이), Rows: 짧은 줄 ×2(설정 값)
    public enum SkeletonKind { List, Cards, Detail, Profile, Rows }

    // 공통 상태 한 칸(계획 0056, BL-38 ③). 화면 UXML의 빈 칸("state")에 UI/StateView 틀을 펼치고, 한 번에 하나만 보인다:
    //   Loading(스켈레톤) · Failed(불러오기 실패, 다시 시도) · Offline(기기에 망 없음, 망이 생기면 저절로 다시) · Message(화면이 정한 안내) · Empty(빈 상태)
    // 처음 불러오기는 Feedback.Load(state, …)가 이 칸을 움직인다. content를 주면 상태가 보이는 동안 그것을 숨김.
    // 문구는 임시(상준, Figma I-07–I-09 확인 뒤), 모양은 States.uss
    public sealed class StateView
    {
        public enum Mode { None, Loading, Failed, Offline, Message, Empty }

        internal const string OfflineTitle = "인터넷에 연결되어 있지 않아요";
        internal const string OfflineBody = "연결되면 자동으로 다시 불러올게요.";
        internal const string RetryText = "다시 시도";
        internal const float NetCheckSec = 2f;

        // 기기에 망이 있는지(테스트가 바꿈). 망이 있어도 서버가 안 닿을 수 있어 "없음"일 때만 믿는다
        internal static Func<bool> HasNetwork = () => Application.internetReachability != NetworkReachability.NotReachable;

        public VisualElement Root { get; }
        public Mode Current { get; private set; }

        readonly VisualElement tree, skeleton, message, icon;
        readonly Label title, body;
        readonly Button action, alt;
        readonly VisualElement[] content;
        readonly string failTitle;
        Action onAction, netRetry, onAlt;
        string altText;
        IVisualElementScheduledItem pulse, netWatch;

        // host: 화면 UXML의 빈 칸. failTitle: "코스를 불러오지 못했어요"처럼 그 화면 말
        public StateView(VisualElement host, SkeletonKind kind, string failTitle, params VisualElement[] content)
        {
            this.failTitle = failTitle;
            this.content = content ?? Array.Empty<VisualElement>();
            host.Clear();
            tree = Resources.Load<VisualTreeAsset>("UI/StateView").Instantiate();
            tree.style.flexGrow = 1;
            host.Add(tree);
            Root = host;
            skeleton = tree.Q("state-skeleton");
            message = tree.Q("state-message");
            icon = tree.Q("state-icon");
            title = tree.Q<Label>("state-title");
            body = tree.Q<Label>("state-body");
            action = tree.Q<Button>("state-action");
            action.clicked += PressAction;
            alt = tree.Q<Button>("state-alt");
            alt.clicked += PressAlt;
            BuildSkeleton(skeleton, kind);
            Show(Mode.None);
        }

        // 칸이 화면 여백 밖(스크롤 바깥)에 있을 때 좌우 여백
        public StateView Padded()
        {
            tree.AddToClassList("state--padded");
            return this;
        }

        // 실패했을 때만 보이는 두 번째 버튼(인증 반영 결과를 못 받아도 "운동 결과 보기"로 넘어가기 등)
        public StateView WithAlt(string text, Action act)
        {
            altText = text;
            onAlt = act;
            return this;
        }

        // 버튼(테스트는 패널 없이 이걸 부름)
        internal void PressAction() => onAction?.Invoke();
        internal void PressAlt() => onAlt?.Invoke();

        public void Loading() => Show(Mode.Loading);

        // 불러오기 실패. 기기에 망이 없으면 Offline(망이 생기면 retry를 저절로 한 번), 아니면 실패 종류별 설명 + 다시 시도
        public void Failed(ApiException e, Action retry)
        {
            if (e != null && e.Failure == ApiFailure.Network && !HasNetwork())
            {
                SetText(OfflineTitle, OfflineBody, retry != null ? RetryText : null, retry);
                netRetry = retry;
                Show(Mode.Offline);
                return;
            }
            string why;
            bool canRetry;
            if (e == null)
            {
                why = "잠시 후 다시 시도해 주세요.";
                canRetry = true;
            }
            else
            {
                var d = Feedback.Describe(e);
                why = d.body;
                canRetry = d.canRetry;
            }
            SetText(failTitle, why, canRetry && retry != null ? RetryText : null, retry);
            Show(Mode.Failed);
        }

        // 화면이 정한 안내(없어진 코스·친구 등). actionText가 없으면 버튼 없음
        public void Message(string titleText, string bodyText, string actionText = null, Action act = null)
        {
            SetText(titleText, bodyText, actionText, act);
            Show(Mode.Message);
        }

        public void Empty(string titleText, string bodyText = null, string actionText = null, Action act = null)
        {
            SetText(titleText, bodyText, actionText, act);
            Show(Mode.Empty);
        }

        public void Clear() => Show(Mode.None);

        void SetText(string t, string b, string a, Action act)
        {
            title.text = t ?? "";
            title.SetVisible(!string.IsNullOrEmpty(t));
            body.text = b ?? "";
            body.SetVisible(!string.IsNullOrEmpty(b));
            action.text = a ?? "";
            action.SetVisible(!string.IsNullOrEmpty(a) && act != null);
            onAction = act;
        }

        void Show(Mode m)
        {
            Current = m;
            if (m != Mode.Offline) netRetry = null;
            bool msg = m == Mode.Failed || m == Mode.Offline || m == Mode.Message || m == Mode.Empty;
            Root.SetVisible(m != Mode.None);
            skeleton.SetVisible(m == Mode.Loading);
            message.SetVisible(msg);
            bool showAlt = (m == Mode.Failed || m == Mode.Offline) && onAlt != null;
            alt.text = altText ?? "";
            alt.SetVisible(showAlt);
            icon.EnableInClassList("state-icon--network", m == Mode.Offline);
            icon.EnableInClassList("state-icon--empty", m == Mode.Empty);
            foreach (var c in content) c?.SetVisible(m == Mode.None);

            if (m == Mode.Loading)
            {
                pulse ??= Root.schedule.Execute(Pulse).Every(PulseMs);
                pulse.Resume();
            }
            else
            {
                pulse?.Pause();
                skeleton.RemoveFromClassList("skeleton--dim");
            }
            if (m == Mode.Offline)
            {
                netWatch ??= Root.schedule.Execute(() => CheckNetwork()).Every((long)(NetCheckSec * 1000));
                netWatch.Resume();
            }
            else netWatch?.Pause();
        }

        // 망이 생겼으면 한 번 다시 불러옴(정할 것 3 가). 쌓기 아래·다른 탭에 숨은 화면은 패널엔 붙어 있어 스케줄이 돌므로
        // 실제로 보일 때만(리뷰): 그 화면이 다시 보이면 OnShow가 불러오거나 그때 이 확인이 이어짐. 테스트가 직접 부름
        internal bool CheckNetwork()
        {
            if (Current != Mode.Offline || netRetry == null || !HasNetwork() || !Visible(Root)) return false;
            var r = netRetry;
            netRetry = null;
            r();
            return true;
        }

        // 깜빡임: 클래스만 0.8초마다 바꾸고 흐려지기는 USS transition이(코드가 매 프레임 스타일을 쓰지 않게, 리뷰)
        const long PulseMs = 800;
        void Pulse() => skeleton.ToggleInClassList("skeleton--dim");

        // 자신과 조상이 모두 display가 켜져 있고 패널에 붙어 있음(패널이 없는 테스트는 display만 봄)
        internal static bool Visible(VisualElement e)
        {
            for (var p = e; p != null; p = p.parent)
                if (p.style.display == DisplayStyle.None) return false;
            return true;
        }

        static void BuildSkeleton(VisualElement into, SkeletonKind kind)
        {
            VisualElement Bar(params string[] mods)
            {
                var b = new VisualElement();
                b.AddToClassList("skeleton-bar");
                foreach (var m in mods) b.AddToClassList(m);
                return b;
            }
            switch (kind)
            {
                case SkeletonKind.List:
                    for (int i = 0; i < 5; i++)
                    {
                        var row = new VisualElement();
                        row.AddToClassList("skeleton-row");
                        var dot = new VisualElement();
                        dot.AddToClassList("skeleton-dot");
                        var col = new VisualElement();
                        col.AddToClassList("skeleton-col");
                        col.Add(Bar("skeleton-bar--mid"));
                        col.Add(Bar("skeleton-bar--short"));
                        row.Add(dot);
                        row.Add(col);
                        into.Add(row);
                    }
                    break;
                case SkeletonKind.Cards:
                    for (int i = 0; i < 3; i++)
                    {
                        var card = new VisualElement();
                        card.AddToClassList("skeleton-card");
                        card.Add(Bar("skeleton-bar--title", "skeleton-bar--mid"));
                        card.Add(Bar());
                        card.Add(Bar("skeleton-bar--short"));
                        into.Add(card);
                    }
                    break;
                case SkeletonKind.Profile:
                    var me = new VisualElement();
                    me.AddToClassList("skeleton-row");
                    var face = new VisualElement();
                    face.AddToClassList("skeleton-dot");
                    face.AddToClassList("skeleton-dot--big");
                    var lines = new VisualElement();
                    lines.AddToClassList("skeleton-col");
                    lines.Add(Bar("skeleton-bar--title", "skeleton-bar--short"));
                    lines.Add(Bar("skeleton-bar--mid"));
                    me.Add(face);
                    me.Add(lines);
                    into.Add(me);
                    var stats = new VisualElement();
                    stats.AddToClassList("skeleton-card");
                    stats.Add(Bar("skeleton-bar--title"));
                    into.Add(stats);
                    break;
                case SkeletonKind.Rows:
                    var box = new VisualElement();
                    box.AddToClassList("skeleton-card");
                    box.Add(Bar("skeleton-bar--mid"));
                    box.Add(Bar("skeleton-bar--short"));
                    into.Add(box);
                    break;
                default:
                    var hero = new VisualElement();
                    hero.AddToClassList("skeleton-hero");
                    into.Add(hero);
                    into.Add(Bar("skeleton-bar--title", "skeleton-bar--mid"));
                    for (int i = 0; i < 4; i++) into.Add(i % 2 == 0 ? Bar() : Bar("skeleton-bar--short"));
                    break;
            }
        }
    }
}
