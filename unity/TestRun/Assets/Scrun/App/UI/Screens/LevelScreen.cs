using System;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // H-04 레벨·러닝복(계획 0049, Figma 610:2212). 지금 레벨·세트·누적·다음 레벨까지(서버 progress 그대로) + Lv.1–6 표.
    // 레벨은 인증 완료 기록의 누적 거리 기준이라 줄지 않음(BL-34 ①). 임팩트(H-02·H-03)는 11/24 이후라 "준비 중" 줄(정할 것 1 나)
    public sealed class LevelScreen : AppScreen
    {
        readonly Progress progress;
        FriendParts.Sheet sheet;

        public LevelScreen(Progress progress) => this.progress = progress ?? throw new ArgumentNullException(nameof(progress));

        protected override string Uxml => "UI/Level";
        public override bool HidesTabBar => true;

        protected override void OnCreate()
        {
            Q<Button>("back").clicked += () => { if (Nav.Current == this) Nav.Pop(); };
            var content = Q<VisualElement>("content");
            var p = progress;
            int stage = Math.Max(1, Math.Min(MyText.LevelStartKm.Length, p.OutfitStage));

            var hero = new VisualElement();
            hero.AddToClassList("my-hero");
            hero.Add(UiExtensions.Text(MyText.Level(p.Level), "my-hero-level"));
            hero.Add(UiExtensions.Text(MyText.Outfit(stage), "my-hero-name"));
            hero.Add(UiExtensions.Text(MyText.LevelLead(p), "my-hero-lead"));
            content.Add(hero);

            var levels = MyParts.Group(content, "레벨별 러닝복");
            for (int lv = 1; lv <= MyText.LevelStartKm.Length; lv++)
            {
                var row = MyParts.Row(MyText.LevelRow(lv), lv == stage ? "현재 단계" : null, null, null, chevron: false);
                row.name = "level-" + lv;
                row.EnableInClassList("my-row--current", lv == stage);
                row.EnableInClassList("my-row--locked", lv > stage);
                levels.Add(row);
            }
            MyParts.MarkLast(levels);

            var more = MyParts.Group(content);
            more.Add(MyParts.Row("임팩트", "인증 횟수로 해금하는 달리기 효과", MyText.Soon, () => { if (sheet?.Open != true) sheet = MyParts.Soon(this); }));
            MyParts.MarkLast(more);
        }

        protected internal override bool OnBack()
        {
            if (sheet?.Open != true) return false;
            sheet.Close();
            return true;
        }
    }
}
