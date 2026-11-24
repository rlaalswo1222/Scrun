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

            // 우주 디자인(계획 0068, 미리보기 levels): Lv 칩(하늘색) · 세트 이름 · 누적, 오른쪽에 러닝복 캐릭터
            var hero = new VisualElement();
            hero.AddToClassList("my-hero");
            var art = new VisualElement { pickingMode = PickingMode.Ignore };
            art.AddToClassList("my-hero-set");
            MyParts.SetOutfit(art, stage);
            hero.Add(art);
            hero.Add(FriendParts.Chip(MyText.Level(p.Level), FriendParts.ChipKind.Live));
            hero.Add(Styled(UiExtensions.Text(MyText.Outfit(stage), "my-hero-name"), "ui-heading"));
            hero.Add(Styled(UiExtensions.Text(MyText.LevelLead(p).Replace(" · ", "\n"), "my-hero-lead"), "ui-sm")); // 미리보기처럼 두 줄(그림 옆이 좁음)
            content.Add(hero);

            var levels = MyParts.Group(content, "레벨별 러닝복");
            levels.AddToClassList("my-group--levels");
            for (int lv = 1; lv <= MyText.LevelStartKm.Length; lv++)
            {
                var row = MyParts.Row(MyText.LevelRow(lv), lv == stage ? "현재 단계" : null, null, null, chevron: false);
                row.name = "level-" + lv;
                row.EnableInClassList("my-row--current", lv == stage);
                row.EnableInClassList("my-row--locked", lv > stage);
                row.Insert(0, Face(lv, done: lv < stage, now: lv == stage));
                levels.Add(row);
            }
            MyParts.MarkLast(levels);

            var more = MyParts.Group(content);
            more.Add(MyParts.Row("임팩트", "인증 횟수로 해금하는 달리기 효과", MyText.Soon, () => { if (sheet?.Open != true) sheet = MyParts.Soon(this); }));
            MyParts.MarkLast(more);
        }

        static Label Styled(Label l, string cls)
        {
            l.AddToClassList(cls);
            return l;
        }

        // 레벨 줄 얼굴 40: 지난 단계는 초록 체크, 지금 단계는 하늘색 테(미리보기 levels)
        static VisualElement Face(int level, bool done, bool now)
        {
            var face = new VisualElement { pickingMode = PickingMode.Ignore };
            face.AddToClassList("my-level-face");
            face.AddToClassList("my-level-face--" + level); // 그 단계 러닝복 얼굴
            if (now)
            {
                var ring = new VisualElement { pickingMode = PickingMode.Ignore };
                ring.AddToClassList("my-level-ring");
                face.Add(ring);
            }
            if (done)
            {
                var mark = new VisualElement { pickingMode = PickingMode.Ignore };
                mark.AddToClassList("my-level-done");
                face.Add(mark);
            }
            return face;
        }

        protected internal override bool OnBack()
        {
            if (sheet?.Open != true) return false;
            sheet.Close();
            return true;
        }
    }
}
