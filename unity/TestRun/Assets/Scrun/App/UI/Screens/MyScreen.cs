using System;
using System.Threading.Tasks;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // H-01 마이 메인(계획 0049, Figma 610:1894). 아래 탭 "마이"의 첫 화면.
    // 닉네임·Lv·인증 마크·다음 레벨까지(GET /users/me·/users/me/progress), 누적 거리·인증·레벨 세 칸, 메뉴.
    // 보일 때마다 다시 받음(인증 직후 레벨이 바로 보이게): 처음만 로딩 덮개, 그다음은 조용히(실패하면 지난 값 그대로).
    // 아직 없는 기능(인증 마크 H-06)은 메뉴는 보이고 누르면 "준비 중"(정할 것 1 나). 주간 목표 H-07은 계획 0054
    public sealed class MyScreen : AppScreen
    {
        Label name, levelChip, marksChip, next, statDistance, statVerified, statLevel;
        VisualElement head, menu, set;
        Label calibrationSub, methodValue;
        Me me;
        Progress progress;
        bool loading;
        StateView state;
        FriendParts.Sheet sheet;

        protected override string Uxml => "UI/My";

        protected override void OnCreate()
        {
            name = Q<Label>("name");
            levelChip = Q<Label>("level-chip");
            marksChip = Q<Label>("marks-chip");
            next = Q<Label>("next");
            statDistance = Q<Label>("stat-distance");
            statVerified = Q<Label>("stat-verified");
            statLevel = Q<Label>("stat-level");
            head = Q<VisualElement>("head");
            set = Q<VisualElement>("set");
            menu = Q<VisualElement>("menu");
            Q<Button>("profile").clicked += () => { if (me != null && CanPush) Nav.Push(new ProfileEditScreen(me)); };
            state = new StateView(Q<VisualElement>("state"), SkeletonKind.Profile, "내 정보를 불러오지 못했어요",
                                  Q<VisualElement>("profile"), Q<VisualElement>("stats"));

            menu.Add(MyParts.Row("러닝복 · 임팩트", "레벨별 러닝복과 해금한 임팩트", null,
                                 () => { if (progress != null && CanPush) Nav.Push(new LevelScreen(progress)); }));
            menu.Add(MyParts.Row("인증 마크", "인증 횟수와 임팩트 해금 현황", MyText.Soon, () => sheet = MyParts.Soon(this)));
            menu.Add(MyParts.Row("주간 목표", "이번 주 목표와 진행", null,
                                 () => { if (CanPush) Nav.Push(new WeeklyGoalStatusScreen()); })); // H-07(계획 0054)
            var recal = MyParts.Row("속도 재보정", MyText.LastCalibration(null), null,
                                    () => { if (CanPush) Nav.Push(new RecalibrateScreen()); });
            calibrationSub = recal.Q<Label>(className: "my-row-sub");
            menu.Add(recal);
            if (SpeedMethodPrefs.Available) // 계획 0060: 개발 빌드에서만, 달리기 속도 방식 비교 실험
            {
                var method = MyParts.Row(MyText.SpeedMethodTitle, MyText.SpeedMethodSub,
                                         SpeedMethodPrefs.Name(SpeedMethodPrefs.Current), OpenSpeedMethod);
                methodValue = method.Q<Label>(className: "my-row-value");
                menu.Add(method);
            }
            menu.Add(MyParts.Row("설정", null, null, () => { if (CanPush) Nav.Push(new SettingsScreen()); }));
            MyParts.MarkLast(menu);
            Render();
        }

        bool CanPush => Nav.Current == this && sheet?.Open != true;

        // 세 방식 중 고르기. 지금 것엔 ✓, 고르면 저장하고 켜진 센서에도 바로(SpeedMethodPrefs.Set)
        void OpenSpeedMethod()
        {
            if (!CanPush) return;
            var cur = SpeedMethodPrefs.Current;
            sheet = new FriendParts.Sheet(Root);
            sheet.Body.Add(UiExtensions.Text(MyText.SpeedMethodTitle, "fr-sheet-title"));
            sheet.Info(MyText.SpeedMethodInfo);
            var methods = (SpeedMethod[])Enum.GetValues(typeof(SpeedMethod));
            sheet.Info(string.Join("\n", Array.ConvertAll(methods, m => $"{SpeedMethodPrefs.Name(m)}: {SpeedMethodPrefs.Describe(m)}")));
            foreach (var m in methods)
            {
                var pick = m;
                sheet.Button((m == cur ? "✓ " : "") + SpeedMethodPrefs.Name(m), m == cur ? "btn-primary" : "btn-ghost", () =>
                {
                    SpeedMethodPrefs.Set(pick);
                    if (methodValue != null) methodValue.text = SpeedMethodPrefs.Name(pick);
                    sheet.Close();
                }, "speed-method-" + (int)m);
            }
            sheet.Button("닫기", "btn-ghost", sheet.Close, "speed-method-close");
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
            Me gotMe = null;
            Progress gotProgress = null;
            CalibrationRecord gotCal = null;
            Func<Task> work = async () =>
            {
                // 셋은 서로 기다릴 필요가 없어 같이 보냄
                var meTask = Api.Users.MeAsync();
                var progressTask = Api.Users.ProgressAsync();
                var calTask = Api.Users.GetCalibrationAsync();
                await Task.WhenAll(meTask, progressTask, calTask);
                (gotMe, gotProgress, gotCal) = (meTask.Result, progressTask.Result, calTask.Result);
            };
            // 처음만 스켈레톤(실패하면 그 자리에, 메뉴는 그대로), 다시 보일 때는 조용히(실패하면 지난 값 그대로)
            bool ok = await Feedback.Load(state, me != null, work, Load);
            loading = false;
            if (!ok || Root.panel == null) return;
            me = gotMe;
            progress = gotProgress;
            calibrationSub.text = MyText.LastCalibration(gotCal?.CreatedAt);
            Render();
        }

        void Render()
        {
            name.text = me?.Nickname ?? "";
            head.Clear();
            if (me != null) head.Add(FriendParts.Head(me.Nickname, 64));
            var p = progress;
            levelChip.text = MyText.Level(p?.Level ?? 1);
            marksChip.text = MyText.Marks(p?.VerifiedCount ?? 0);
            next.text = p == null ? "" : MyText.NextLevel(p.NextLevelRemainingM);
            statDistance.text = MyText.Km(p?.XpM ?? 0);
            statVerified.text = MyText.Count(p?.VerifiedCount ?? 0);
            statLevel.text = MyText.Level(p?.Level ?? 1);
            MyParts.SetOutfit(set, p?.OutfitStage ?? 1);
        }
    }

    // 마이 화면들이 같이 쓰는 줄·스위치·"준비 중" 시트. 모양은 My.uss(.my-…), 시트는 Friends.uss(FriendParts.Sheet)
    static class MyParts
    {
        // 제목 + 설명(없으면 null) + 오른쪽 값(없으면 꺾쇠). 누르면 onTap
        public static Button Row(string title, string sub, string value, Action onTap, bool chevron = true)
        {
            var row = new Button(() => onTap?.Invoke());
            row.AddToClassList("my-row");
            var text = new VisualElement { pickingMode = PickingMode.Ignore };
            text.AddToClassList("my-row-text");
            text.Add(Label(title, "my-row-title"));
            if (sub != null) text.Add(Label(sub, "my-row-sub"));
            row.Add(text);
            if (value != null) row.Add(Label(value, "my-row-value"));
            if (chevron && onTap != null)
            {
                var chev = new VisualElement { pickingMode = PickingMode.Ignore };
                chev.AddToClassList("ui-chev"); // 흰 선 꺾쇠(Space.uss)
                chev.AddToClassList("my-row-chevron");
                row.Add(chev);
            }
            return row;
        }

        // 러닝복 그림(우주 디자인, My.uss .my-set--N). 단계 1–6
        public static void SetOutfit(VisualElement art, int stage)
        {
            if (art == null) return;
            int s = Math.Max(1, Math.Min(MyText.LevelStartKm.Length, stage));
            for (int i = 1; i <= MyText.LevelStartKm.Length; i++) art.EnableInClassList("my-set--" + i, i == s);
        }

        // 켜기·끄기 줄. locked면 스위치를 흐리게 하고 누르면 onLocked("준비 중")
        public static Button SwitchRow(string title, string sub, bool on, Action<bool> onChange, bool locked = false, Action onLocked = null)
        {
            var sw = Switch(on);
            if (locked) sw.AddToClassList("my-switch--locked");
            var row = Row(title, sub, locked ? MyText.Soon : null, () =>
            {
                if (locked) { onLocked?.Invoke(); return; }
                onChange?.Invoke(!sw.ClassListContains("my-switch--on"));
            }, chevron: false);
            row.Add(sw);
            return row;
        }

        public static VisualElement Switch(bool on)
        {
            var sw = new VisualElement { name = "switch", pickingMode = PickingMode.Ignore };
            sw.AddToClassList("my-switch");
            var knob = new VisualElement { pickingMode = PickingMode.Ignore };
            knob.AddToClassList("my-knob");
            sw.Add(knob);
            SetSwitch(sw, on);
            return sw;
        }

        public static void SetSwitch(VisualElement sw, bool on) => sw.EnableInClassList("my-switch--on", on);

        public static void MarkLast(VisualElement group)
        {
            for (int i = 0; i < group.childCount; i++) group[i].EnableInClassList("my-row--last", i == group.childCount - 1);
        }

        public static VisualElement Group(VisualElement parent, string section = null)
        {
            if (section != null) parent.Add(Label(section, "my-section"));
            var g = new VisualElement();
            g.AddToClassList("my-group");
            parent.Add(g);
            return g;
        }

        // 아직 없는 기능(계획 0049 정할 것 1 나)
        public static FriendParts.Sheet Soon(AppScreen screen)
        {
            var sheet = new FriendParts.Sheet(screen.Root);
            sheet.Body.Add(UiExtensions.Text(MyText.SoonTitle, "fr-sheet-title"));
            sheet.Info(MyText.SoonBody);
            sheet.Button("닫기", "btn-ghost", sheet.Close, "soon-close");
            return sheet;
        }

        static Label Label(string text, string cls)
        {
            var l = UiExtensions.Text(text, cls);
            l.pickingMode = PickingMode.Ignore;
            return l;
        }
    }
}
