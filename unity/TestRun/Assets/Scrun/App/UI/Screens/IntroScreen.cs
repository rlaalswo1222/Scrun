using UnityEngine.UIElements;

namespace Scrun.App
{
    // A-02–A-04 서비스 소개(Figma 596:59·596:101·596:145, 계획 0031). 이 기기에서 처음 켰고 로그인이 없을 때 스플래시 다음.
    // 한 화면에서 그림·글·점·버튼만 바꾼다. "다음"·옆으로 밀기로 넘기고, "건너뛰기"·마지막 "시작하기"는 로그인(A-05)으로.
    // 끝까지 보거나 건너뛰면 다시 안 보여 준다(Onboarding.MarkIntroSeen).
    public sealed class IntroScreen : AppScreen
    {
        public readonly struct Page
        {
            public readonly string IllustClass, Title, Lead;

            public Page(string illustClass, string title, string lead)
            {
                IllustClass = illustClass;
                Title = title;
                Lead = lead;
            }
        }

        // Figma 문구 그대로(PP01–PP03)
        public static readonly Page[] Pages =
        {
            new Page("intro-run", "내 속도로 달려요", "폰 하나로 속도를 추정해 아바타가 내 속도로 달려요"),
            new Page("intro-together", "같은 코스를 함께 달려요", "실시간 러너, 친구, 어제의 나와 한 화면에서 달려요"),
            new Page("intro-record", "기록이 남고, 이어져요", "계기판을 인증하면 기록과 주간 목표가 차곡차곡 쌓여요"),
        };

        const float SwipePx = 60f; // 이만큼 옆으로 밀면 넘김(패널 좌표, 기준 390 폭)

        VisualElement illust, dots, pager;
        Label title, lead;
        Button skip, next;
        float downX;
        int downPointer = -1;
        bool left;

        public int PageIndex { get; private set; }

        protected override string Uxml => "UI/Intro";
        public override bool HidesTabBar => true;

        protected override void OnCreate()
        {
            pager = Q<VisualElement>("pager");
            illust = Q<VisualElement>("illust");
            dots = Q<VisualElement>("dots");
            title = Q<Label>("title");
            lead = Q<Label>("lead");
            skip = Q<Button>("skip");
            next = Q<Button>("next");
            skip.clicked += Leave;
            next.clicked += Next;
            pager.RegisterCallback<PointerDownEvent>(e =>
            {
                downX = e.position.x;
                downPointer = e.pointerId;
                pager.CapturePointer(e.pointerId);
            });
            pager.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.pointerId != downPointer) return;
                downPointer = -1;
                pager.ReleasePointer(e.pointerId);
                float dx = e.position.x - downX;
                if (dx <= -SwipePx) Next();
                else if (dx >= SwipePx) Show(PageIndex - 1);
            });
            pager.RegisterCallback<PointerCaptureOutEvent>(_ => downPointer = -1);
            Show(0);
        }

        // 뒤로 가기(에디터 Esc): 둘째 장부터는 앞 장으로
        protected internal override bool OnBack()
        {
            if (PageIndex == 0) return false;
            Show(PageIndex - 1);
            return true;
        }

        internal void Next()
        {
            if (PageIndex >= Pages.Length - 1) Leave();
            else Show(PageIndex + 1);
        }

        internal void Show(int index)
        {
            if (index < 0 || index >= Pages.Length) return;
            foreach (var p in Pages) illust.RemoveFromClassList(p.IllustClass);
            PageIndex = index;
            var page = Pages[index];
            illust.AddToClassList(page.IllustClass);
            title.text = page.Title;
            lead.text = page.Lead;
            for (int i = 0; i < dots.childCount; i++) dots[i].EnableInClassList("dot--on", i == index);
            bool last = index == Pages.Length - 1;
            next.text = last ? "시작하기" : "다음";
            skip.style.visibility = last ? Visibility.Hidden : Visibility.Visible; // Figma A-04는 건너뛰기 없음(자리는 둠)
        }

        void Leave()
        {
            if (left || Nav.Current != this) return;
            left = true;
            Onboarding.MarkIntroSeen();
            Nav.StartFlow(new LoginScreen());
        }
    }
}
