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
        }

        protected internal override bool OnBack()
        {
            Nav.PopToRoot();
            return true;
        }
    }
}
