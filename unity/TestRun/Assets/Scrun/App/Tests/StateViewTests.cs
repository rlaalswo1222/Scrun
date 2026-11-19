using System;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Scrun.App.Tests
{
    // 공통 상태(계획 0056, BL-38): 첫 로딩 스켈레톤 → 내용 / 실패 → 다시 시도 / 네트워크 없음 → 망이 생기면 저절로 / 빈 상태
    public class StateViewTests
    {
        Func<bool> savedNet;

        [SetUp]
        public void Attach()
        {
            savedNet = StateView.HasNetwork;
            var root = new VisualElement();
            foreach (var n in new[] { "loading", "error" }) root.Add(new VisualElement { name = n });
            foreach (var n in new[] { "error-title", "error-body", "error-id" }) root.Add(new Label { name = n });
            foreach (var n in new[] { "error-retry", "error-close" }) root.Add(new Button { name = n });
            Feedback.Attach(root);
        }

        [TearDown]
        public void Detach()
        {
            StateView.HasNetwork = savedNet;
            StateView.GoHome = null;
            Feedback.Detach();
            Feedback.OnNotLoggedIn = null;
        }

        static bool Shown(VisualElement e) => e.style.display != DisplayStyle.None;

        static (StateView state, VisualElement content) Make(SkeletonKind kind = SkeletonKind.List)
        {
            var host = new VisualElement();
            var content = new VisualElement();
            return (new StateView(host, kind, "코스를 불러오지 못했어요", content), content);
        }

        static Task Fail(ApiFailure f, int status = 0, string code = null) =>
            Task.FromException(new ApiException(f, "실패", status, code));

        [Test]
        public void Starts_hidden_and_loading_hides_content_until_done()
        {
            var (s, content) = Make();
            Assert.AreEqual(StateView.Mode.None, s.Current);
            Assert.IsFalse(Shown(s.Root));
            Assert.IsTrue(Shown(content));

            bool coverDuring = true;
            var t = Feedback.Load(s, false, () =>
            {
                coverDuring = Feedback.IsBlocking;
                Assert.AreEqual(StateView.Mode.Loading, s.Current);
                Assert.IsTrue(Shown(s.Root.Q("state-skeleton")));
                Assert.IsFalse(Shown(content), "스켈레톤 동안 내용은 숨김");
                return Task.CompletedTask;
            }, null);
            Assert.IsTrue(t.IsCompleted && t.Result);
            Assert.IsFalse(coverDuring, "첫 로딩은 화면 덮개 없이 스켈레톤(정할 것 1 가)");
            Assert.AreEqual(StateView.Mode.None, s.Current);
            Assert.IsTrue(Shown(content));
        }

        [TestCase(SkeletonKind.List)]
        [TestCase(SkeletonKind.Cards)]
        [TestCase(SkeletonKind.Detail)]
        [TestCase(SkeletonKind.Profile)]
        [TestCase(SkeletonKind.Rows)]
        public void Every_skeleton_kind_has_bars(SkeletonKind kind)
        {
            var (s, _) = Make(kind);
            Assert.Greater(s.Root.Q("state-skeleton").childCount, 0);
        }

        [Test]
        public void Failure_shows_in_place_without_dialog_and_retries()
        {
            StateView.HasNetwork = () => true;
            var (s, content) = Make();
            int retried = 0;
            var t = Feedback.Load(s, false, () => Fail(ApiFailure.Server, 500, "INTERNAL_ERROR"), () => retried++);
            Assert.IsFalse(t.Result);
            Assert.AreEqual(StateView.Mode.Failed, s.Current);
            Assert.IsFalse(Feedback.IsBlocking, "안내창은 안 띄움(정할 것 2 가)");
            Assert.IsFalse(Shown(content));
            Assert.AreEqual(StateView.ErrorTitle, s.Root.Q<Label>("state-title").text, "Figma I-09");
            Assert.AreEqual(StateView.ErrorBody, s.Root.Q<Label>("state-body").text);
            Assert.IsTrue(s.Root.Q("state-icon").ClassListContains("state-icon--error"));
            var retry = s.Root.Q<Button>("state-action");
            Assert.IsTrue(Shown(retry));
            Assert.IsFalse(Shown(s.Root.Q<Button>("state-alt")), "두 번째 버튼은 준 화면만");
            s.PressAction();
            Assert.AreEqual(1, retried);
        }

        [Test]
        public void Bad_request_has_no_retry_button()
        {
            StateView.HasNetwork = () => true;
            var (s, _) = Make();
            Feedback.Load(s, false, () => Fail(ApiFailure.Server, 409, "RUN_NOT_FINISHED"), () => { });
            Assert.AreEqual(StateView.Mode.Failed, s.Current);
            Assert.IsFalse(Shown(s.Root.Q<Button>("state-action")), "다시 보내도 같은 실패엔 다시 시도 없음");
            Assert.AreEqual("코스를 불러오지 못했어요", s.Root.Q<Label>("state-title").text, "그 화면 말");
        }

        [Test]
        public void Offline_reloads_by_itself_once_the_network_is_back()
        {
            bool net = false;
            StateView.HasNetwork = () => net;
            var (s, _) = Make();
            int retried = 0;
            Feedback.Load(s, false, () => Fail(ApiFailure.Network), () => retried++);
            Assert.AreEqual(StateView.Mode.Offline, s.Current);
            Assert.AreEqual(StateView.OfflineTitle, s.Root.Q<Label>("state-title").text);
            Assert.IsFalse(s.CheckNetwork(), "망이 없으면 그대로");
            Assert.AreEqual(0, retried);
            net = true;
            Assert.IsTrue(s.CheckNetwork());
            Assert.AreEqual(1, retried);
            Assert.IsFalse(s.CheckNetwork(), "한 번만");
            Assert.AreEqual(1, retried);
        }

        // 리뷰: 쌓기 아래·다른 탭에 숨은 화면은 패널에 붙어 있어도 망이 돌아왔다고 저절로 다시 받지 않음
        [Test]
        public void Hidden_screen_does_not_reload_by_itself()
        {
            StateView.HasNetwork = () => false;
            var screenRoot = new VisualElement();
            var host = new VisualElement();
            screenRoot.Add(host);
            var s = new StateView(host, SkeletonKind.List, "x");
            int retried = 0;
            Feedback.Load(s, false, () => Fail(ApiFailure.Network), () => retried++);
            StateView.HasNetwork = () => true;
            screenRoot.style.display = DisplayStyle.None; // Navigator가 숨긴 화면
            Assert.IsFalse(s.CheckNetwork());
            Assert.AreEqual(0, retried);
            screenRoot.style.display = DisplayStyle.Flex;
            Assert.IsTrue(s.CheckNetwork());
            Assert.AreEqual(1, retried);
        }

        // 리뷰: 칩을 빨리 바꿔 버려진 옛 요청은 새 요청의 칸(스켈레톤·실패)을 건드리지 않음
        [Test]
        public void Stale_request_leaves_the_newer_state_alone()
        {
            StateView.HasNetwork = () => true;
            var (s, _) = Make();
            int seq = 1;
            var old = new TaskCompletionSource<bool>();
            var oldLoad = Feedback.Load(s, false, () => old.Task, () => { }, null, () => seq == 1);
            seq = 2;
            Feedback.Load(s, false, () => Fail(ApiFailure.Server, 500), () => { }, null, () => seq == 2);
            Assert.AreEqual(StateView.Mode.Failed, s.Current);
            old.SetResult(true); // 옛 요청이 늦게 성공
            Assert.IsTrue(oldLoad.Result);
            Assert.AreEqual(StateView.Mode.Failed, s.Current, "새 요청의 실패 칸이 남음(빈 화면에 갇히지 않게)");

            var old2 = new TaskCompletionSource<bool>();
            seq = 3;
            Feedback.Load(s, false, () => old2.Task, () => { }, null, () => seq == 3);
            seq = 4;
            Feedback.Load(s, false, () => new TaskCompletionSource<bool>().Task, () => { }, null, () => seq == 4);
            old2.SetException(new ApiException(ApiFailure.Server, "x", 500));
            Assert.AreEqual(StateView.Mode.Loading, s.Current, "옛 요청의 실패가 새 요청의 스켈레톤을 걷지 않음");
        }

        [Test]
        public void Network_failure_with_a_network_is_a_normal_failure()
        {
            StateView.HasNetwork = () => true; // 망은 있는데 서버가 안 닿음(시간 초과)
            var (s, _) = Make();
            Feedback.Load(s, false, () => Fail(ApiFailure.Network), () => { });
            Assert.AreEqual(StateView.Mode.Failed, s.Current);
        }

        [Test]
        public void Screen_handled_failure_keeps_its_own_message()
        {
            var (s, _) = Make();
            Feedback.Load(s, false, () => Fail(ApiFailure.Server, 404, "COURSE_NOT_FOUND"), () => { }, e =>
            {
                s.Message("없는 코스예요", "다른 코스를 골라 주세요", "다른 코스 보기", () => { });
                return true;
            });
            Assert.AreEqual(StateView.Mode.Message, s.Current);
            Assert.AreEqual("다른 코스 보기", s.Root.Q<Button>("state-action").text);

            var (q, _) = Make();
            Feedback.Load(q, false, () => Fail(ApiFailure.Server, 404, "X"), () => { }, e => true);
            Assert.AreEqual(StateView.Mode.None, q.Current, "화면이 안내를 안 띄웠으면 스켈레톤만 걷음");
        }

        [Test]
        public void Already_shown_screen_reloads_quietly_without_touching_the_state()
        {
            var (s, content) = Make();
            var t = Feedback.Load(s, true, () => Fail(ApiFailure.Server, 500), () => { });
            Assert.IsFalse(t.Result);
            Assert.AreEqual(StateView.Mode.None, s.Current, "지난 내용 그대로");
            Assert.IsTrue(Shown(content));
            Assert.IsFalse(Feedback.IsBlocking);
        }

        [Test]
        public void Not_logged_in_goes_to_login_and_clears()
        {
            string notice = null;
            Feedback.OnNotLoggedIn = n => notice = n;
            var (s, _) = Make();
            Feedback.Load(s, false, () => Fail(ApiFailure.NotLoggedIn), () => { });
            Assert.IsNotNull(notice);
            Assert.AreEqual(StateView.Mode.None, s.Current);
        }

        [Test]
        public void Empty_and_alt_button()
        {
            StateView.HasNetwork = () => true;
            var host = new VisualElement();
            int alt = 0;
            var s = new StateView(host, SkeletonKind.Cards, "반영 결과를 불러오지 못했어요").WithAlt("운동 결과 보기", () => alt++);
            s.Empty("아직 친구가 없어요");
            Assert.AreEqual(StateView.Mode.Empty, s.Current);
            Assert.IsFalse(Shown(s.Root.Q<Button>("state-action")), "버튼 없는 빈 상태");
            Assert.IsFalse(Shown(s.Root.Q<Button>("state-alt")), "두 번째 버튼은 실패 때만");
            s.Failed(new ApiException(ApiFailure.Server, "x", 503, "SERVICE_UNAVAILABLE"), () => { });
            var b = s.Root.Q<Button>("state-alt");
            Assert.IsTrue(Shown(b));
            Assert.AreEqual("운동 결과 보기", b.text);
            s.PressAlt();
            Assert.AreEqual(1, alt);
        }

        // Figma I-09: 실패엔 "홈으로"(화면이 다른 두 번째 버튼을 주면 그것), I-07 네트워크 없음엔 없음
        [Test]
        public void Failure_offers_home_but_offline_does_not()
        {
            int home = 0;
            StateView.GoHome = () => home++;
            StateView.HasNetwork = () => true;
            var (s, _) = Make();
            s.Failed(new ApiException(ApiFailure.Server, "x", 500), () => { });
            var b = s.Root.Q<Button>("state-alt");
            Assert.IsTrue(Shown(b));
            Assert.AreEqual(StateView.HomeText, b.text);
            s.PressAlt();
            Assert.AreEqual(1, home);
            StateView.HasNetwork = () => false;
            s.Failed(new ApiException(ApiFailure.Network, "x"), () => { });
            Assert.IsFalse(Shown(s.Root.Q<Button>("state-alt")), "I-07엔 다시 시도만");
            Assert.IsTrue(s.Root.Q("state-icon").ClassListContains("state-icon--network"));
        }

        // 옮긴 화면들이 UXML에 공통 칸을 갖고 예전 칸이 남지 않았는지(이름이 바뀌면 Q가 null을 줘 생성에서 예외)
        [Test]
        public void Moved_screens_create_with_a_state_slot()
        {
            AppScreen[] screens =
            {
                new CourseListScreen(), new CourseDetailScreen("crs"), new FriendsScreen(), new FriendManageScreen(),
                new FriendProfileScreen("u"), new MyScreen(), new RecalibrateScreen(), new RecordsScreen(),
                new RunResultScreen("r"), new WeeklyGoalStatusScreen(), new RunRewardsScreen("r"), new TermsScreen(),
            };
            foreach (var s in screens)
            {
                s.Create(null);
                Assert.IsNotNull(s.Root.Q("state-root"), s.GetType().Name);
                Assert.IsNull(s.Root.Q("load-failed"), s.GetType().Name + "에 옛 실패 칸");
            }
        }
    }
}
