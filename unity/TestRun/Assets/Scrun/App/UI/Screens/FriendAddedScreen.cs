using UnityEngine.UIElements;

namespace Scrun.App
{
    // G-07 친구 추가 완료(계획 0044). "친구 보기"·뒤로 가기 모두 친구 탭 첫 화면(G-01)으로 — 다시 보일 때 새 친구가 반영됨
    public sealed class FriendAddedScreen : AppScreen
    {
        readonly Friend friend;

        public FriendAddedScreen(Friend friend) => this.friend = friend;

        protected override string Uxml => "UI/FriendAdded";
        public override bool HidesTabBar => true;

        protected override void OnCreate()
        {
            Q<VisualElement>("head").Add(FriendParts.Head(friend?.Nickname, 96));
            Q<Label>("title").text = $"{friend?.Nickname} 님과\n친구가 되었어요";
            Q<Button>("done").clicked += () => { if (Nav.Current == this) Nav.PopToRoot(); };
            // 우주 디자인(계획 0068, 미리보기 friendAdded): 머리는 튀며, 글은 아래에서 올라오며
            UiMotion.Pop(Root.Q<VisualElement>(className: "fr-done-av"));
            UiMotion.Rise(Q<Label>("title"));
            UiMotion.Rise(Root.Q<Label>(className: "fr-done-lead"), 450);
        }

        protected internal override bool OnBack()
        {
            Nav.PopToRoot();
            return true;
        }
    }
}
