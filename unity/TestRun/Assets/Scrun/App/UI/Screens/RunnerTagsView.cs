using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // 다른 러너 머리 위 이름표와 러너 정보 카드(BL-22, Figma D-12, 계획 0041 정할 것 5·6).
    // 이름표 위치는 섬이 매 프레임 RunLink.Heads에 넣고(화면 픽셀), 여기서 HUD 층에 붙여 누를 수 있게 한다.
    // 카드: 이름·나와의 간격·이번 러닝 거리·닫기(Figma "누적 거리"는 전체 누적으로 읽혀서 바꿈, 상준 확인). 봇은 간격만. 레벨·친구·인증 마크는 데이터가 생기면(S7·S8)
    public sealed class RunnerTagsView
    {
        public const string BotSubtitle = "일정한 페이스로 함께 달리는 페이스메이커예요"; // 임시 문구(상준)

        readonly IRunTogether together;
        readonly VisualElement layer, card;
        readonly Label cardName, cardBadge, cardSub, cardGap, cardDist;
        readonly VisualElement cardDistRow;
        readonly Dictionary<string, Label> tags = new Dictionary<string, Label>();
        readonly List<string> gone = new List<string>();
        readonly HashSet<string> present = new HashSet<string>();
        readonly List<Rect> placed = new List<Rect>();
        const float StackGap = 2f;
        string openId;

        public RunnerTagsView(VisualElement hudRoot, IRunTogether together)
        {
            this.together = together;
            layer = hudRoot.Q<VisualElement>("hud-tags");
            card = hudRoot.Q<VisualElement>("hud-runner-card");
            cardName = hudRoot.Q<Label>("hud-card-name");
            cardBadge = hudRoot.Q<Label>("hud-card-badge");
            cardSub = hudRoot.Q<Label>("hud-card-sub");
            cardGap = hudRoot.Q<Label>("hud-card-gap");
            cardDist = hudRoot.Q<Label>("hud-card-dist");
            cardDistRow = hudRoot.Q<VisualElement>("hud-card-dist-row");
            if (layer == null || card == null || cardName == null || cardGap == null || cardDist == null)
                throw new InvalidOperationException("RunHud.uxml에 이름표 층·러너 카드가 없음");
            hudRoot.Q<Button>("hud-card-close").clicked += Close;
            card.SetVisible(false);
        }

        public bool CardOpen => openId != null;

        // 매 프레임(HUD 스케줄). stale: 끊겨서 다른 러너 위치를 못 믿음 → 흐리게(D-16)
        public void Tick(bool stale)
        {
            layer.EnableInClassList("hud-dimmed", stale);
            var panel = layer.panel;
            RemoteRunner open = null;
            placed.Clear();
            present.Clear();
            foreach (var r in together?.Runners ?? (IReadOnlyList<RemoteRunner>)Array.Empty<RemoteRunner>())
            {
                present.Add(r.UserId);
                if (r.UserId == openId) open = r;
                if (panel == null || !RunLink.Heads.TryGetValue(r.UserId, out var screen))
                {
                    if (tags.TryGetValue(r.UserId, out var hidden)) Show(hidden, false);
                    continue;
                }
                var tag = TagFor(r);
                // 화면 픽셀(아래에서 위) → 패널 좌표(위에서 아래)
                var at = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
                // 다른 이름표와 겹치면 위로 쌓음(섬 RunnerLabels와 같게, 6칸까지). 크기는 지난 프레임 배치 값
                float w = tag.resolvedStyle.width, h = tag.resolvedStyle.height;
                if (w > 0f && h > 0f)
                {
                    var rect = new Rect(at.x - w / 2f, at.y - h, w, h);
                    for (int k = 0; k < 6 && Overlaps(rect); k++) rect.y -= h + StackGap;
                    placed.Add(rect);
                    at.y = rect.yMax;
                }
                tag.style.left = at.x;
                tag.style.top = at.y;
                Show(tag, true);
            }

            // 나간 사람의 이름표는 지움, 카드가 그 사람이면 닫음
            gone.Clear();
            foreach (var id in tags.Keys)
                if (!present.Contains(id)) gone.Add(id);
            foreach (var id in gone)
            {
                tags[id].RemoveFromHierarchy();
                tags.Remove(id);
            }
            if (openId != null)
            {
                if (open == null) Close();
                else FillCard(open);
            }
        }

        bool Overlaps(Rect rect)
        {
            for (int i = 0; i < placed.Count; i++)
                if (placed[i].Overlaps(rect)) return true;
            return false;
        }

        // display가 아니라 visibility로 숨김: 숨긴 동안에도 크기가 남아 다시 보일 때 첫 프레임부터 쌓기가 맞음
        static void Show(VisualElement e, bool on) =>
            e.style.visibility = on ? Visibility.Visible : Visibility.Hidden;

        Label TagFor(RemoteRunner r)
        {
            if (tags.TryGetValue(r.UserId, out var tag)) return tag;
            tag = new Label(r.DisplayName);
            tag.AddToClassList("hud-tag");
            if (r.IsBot) tag.AddToClassList("hud-tag--bot");
            tag.pickingMode = PickingMode.Position;
            string id = r.UserId;
            tag.RegisterCallback<ClickEvent>(_ => Open(id));
            layer.Add(tag);
            tags[id] = tag;
            return tag;
        }

        public void Open(string userId)
        {
            openId = userId;
            card.SetVisible(true);
            foreach (var r in together.Runners)
                if (r.UserId == userId) FillCard(r);
        }

        public void Close()
        {
            openId = null;
            card.SetVisible(false);
        }

        void FillCard(RemoteRunner r)
        {
            var (name, badge, sub, gap, dist) = CardText(r, RunLink.PlayerLapPositionM, RunLink.LapLengthM, RealtimeClock.Now());
            cardName.text = name;
            cardBadge.text = badge ?? "";
            cardBadge.SetVisible(badge != null);
            cardSub.text = sub ?? "";
            cardSub.SetVisible(sub != null);
            cardGap.text = gap;
            cardDistRow.SetVisible(dist != null);
            if (dist != null) cardDist.text = dist;
        }

        // 카드 글자: (이름, 배지, 설명, 나와의 간격, 누적 거리). 봇은 배지 "BOT"·설명, 거리 없음. 사람은 거리를 모르면 null
        public static (string name, string badge, string sub, string gap, string dist) CardText(
            RemoteRunner r, float myLapM, float lapM, double now)
        {
            float delta = LoopMath.Delta(myLapM, r.PositionAt(now), lapM);
            string gap = Math.Abs(delta) < 1f ? "나란히" : LoopMath.GapText(Math.Abs(delta), ahead: delta >= 0f);
            if (r.IsBot) return (r.Nickname, "BOT", BotSubtitle, gap, null);
            string dist = r.RunDistanceM is float m ? RunSession.DistanceText(m) + "km" : null;
            return (r.Nickname, null, null, gap, dist);
        }
    }
}
