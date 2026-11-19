using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Scrun.App
{
    // 화면 코드가 서버를 부르는 입구.
    //   var r = await Api.Auth.KakaoLoginAsync(kakaoToken);       // 처음이면 r.IsNewUser
    //   var n = await Api.Users.CheckNicknameAsync("민재_01");     // n.Nickname = 정규화된 값
    //   Api.Session.Expired += GoToLogin;
    // 실패는 ApiException(Failure·Code·RequestId). 서버 주소는 ApiConfig.
    public static class Api
    {
        static ApiClient client;
        static AuthApi auth;
        static UsersApi users;
        static CoursesApi courses;
        static RunsApi runs;
        static FriendsApi friends;

        public static ApiClient Client { get { Ensure(); return client; } }
        public static Session Session => Client.Session;
        public static AuthApi Auth { get { Ensure(); return auth; } }
        public static UsersApi Users { get { Ensure(); return users; } }
        public static CoursesApi Courses { get { Ensure(); return courses; } }
        public static RunsApi Runs { get { Ensure(); return runs; } }
        public static FriendsApi Friends { get { Ensure(); return friends; } }

        static void Ensure()
        {
            if (client != null) return;
            client = new ApiClient(new UnityHttpTransport(), new Session(Native.SecureStore), () => ApiConfig.BaseUrl);
            auth = new AuthApi(client);
            users = new UsersApi(client);
            courses = new CoursesApi(client);
            runs = new RunsApi(client);
            friends = new FriendsApi(client);
        }

        // Play마다 저장된 로그인부터 다시 읽는다(이 프로젝트는 Play 때 도메인 리로드를 꺼 둠).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => client = null;

        // 에디터 창의 "저장된 로그인 지우기": 지금 쓰는 로그인(메모리)과 저장소를 같이 지운다
        internal static void ClearSavedLogin()
        {
            if (client != null) client.Session.Clear();
            else new Session(Native.SecureStore).Clear();
        }

        // 이 기기를 가리키는 값(iOS는 identifierForVendor). 같은 기기의 예전 로그인을 서버가 폐기하는 데 씀.
        internal static string DeviceId
        {
            get
            {
                var id = SystemInfo.deviceUniqueIdentifier;
                return string.IsNullOrEmpty(id) || id == SystemInfo.unsupportedIdentifier ? null
                    : id.Length > 100 ? id.Substring(0, 100) : id;
            }
        }
    }

    public sealed class AuthApi
    {
        readonly ApiClient c;
        public AuthApi(ApiClient client) => c = client;

        // 카카오 SDK 토큰 → 이미 가입했으면 로그인 끝(Session.IsLoggedIn), 처음이면 IsNewUser + 가입용 토큰 보관(Session.IsSigningUp)
        public async Task<LoginResult> KakaoLoginAsync(string kakaoAccessToken)
        {
            var r = await c.PostAsync<LoginResult>("/v1/auth/kakao",
                new { kakaoAccessToken, deviceId = Api.DeviceId }, AuthMode.None);
            if (r == null) throw new ApiException(ApiFailure.BadResponse, "로그인 응답이 비었습니다.");
            if (r.IsNewUser) c.Session.BeginSignup(r.SignupToken);
            else c.Session.SetTokens(r.AccessToken, r.RefreshToken);
            return r;
        }

        public Task<List<Agreement>> AgreementsAsync() => c.GetAsync<List<Agreement>>("/v1/agreements", AuthMode.None);

        // 가입용 토큰으로 가입 → 로그인 끝. 같은 가입을 다시 보내도(응답 유실·두 번 누름) 서버가 같은 계정 토큰을 준다.
        public async Task SignupAsync(SignupRequest request)
        {
            request.DeviceId ??= Api.DeviceId;
            var pair = await c.PostAsync<TokenPair>("/v1/auth/signup", request, AuthMode.Signup);
            if (pair == null) throw new ApiException(ApiFailure.BadResponse, "가입 응답이 비었습니다.");
            c.Session.SetTokens(pair.AccessToken, pair.RefreshToken);
        }

        // 이 기기 로그인 끝내기. 서버에 못 알려도(네트워크 등) 이 기기의 토큰은 지운다.
        public async Task LogoutAsync()
        {
            var refresh = c.Session.RefreshToken;
            c.Session.LoggingOut = true; // 여기서 재발급이 거절돼도 "다시 로그인하세요"(Expired)를 띄우지 않음
            try
            {
                if (refresh != null && c.Session.AccessToken != null)
                    await c.PostAsync<object>("/v1/auth/logout", new { refreshToken = refresh }, AuthMode.User);
            }
            catch (ApiException e)
            {
                Debug.LogWarning($"[Api] 로그아웃을 서버에 알리지 못함(이 기기 토큰은 지움): {e.Failure} {e.Code}");
            }
            finally
            {
                c.Session.LoggingOut = false;
                c.Session.Clear();
            }
        }
    }

    public sealed class UsersApi
    {
        readonly ApiClient c;
        public UsersApi(ApiClient client) => c = client;

        // 가입 중(가입용 토큰)에도, 로그인 뒤(내 정보 수정)에도 된다. 로그인한 사람의 지금 닉네임은 사용 가능.
        public Task<NicknameCheck> CheckNicknameAsync(string nickname) =>
            c.GetAsync<NicknameCheck>("/v1/users/nickname/check?nickname=" + Uri.EscapeDataString(nickname ?? ""),
                                      AuthMode.SignupOrUser);

        public async Task<Me> MeAsync() =>
            await c.GetAsync<Me>("/v1/users/me", AuthMode.User) ?? throw new ApiException(ApiFailure.BadResponse, "내 정보 응답이 비었습니다.");

        // 내 정보 고치기(H-11, 계획 0049). 실패: 400 VALIDATION_ERROR·NICKNAME_NOT_ALLOWED / 409 NICKNAME_DUPLICATED
        public async Task<Me> UpdateMeAsync(MeUpdate body) =>
            await c.SendAsync<Me>("PATCH", "/v1/users/me", body, AuthMode.User) ?? throw new ApiException(ApiFailure.BadResponse, "내 정보 응답이 비었습니다.");

        public async Task<UserSettings> SettingsAsync() =>
            await c.GetAsync<UserSettings>("/v1/users/me/settings", AuthMode.User) ?? throw new ApiException(ApiFailure.BadResponse, "설정 응답이 비었습니다.");

        public async Task<UserSettings> UpdateSettingsAsync(bool feedShareDefault) =>
            await c.SendAsync<UserSettings>("PATCH", "/v1/users/me/settings", new { feedShareDefault }, AuthMode.User)
            ?? throw new ApiException(ApiFailure.BadResponse, "설정 응답이 비었습니다.");

        // 계정 삭제(H-10, 계획 0058). 서버가 카카오 연결을 끊은 뒤 모두 지움. 카카오가 응답하지 않으면 503(아무것도 안 지움)
        // 지운 뒤 응답만 못 받고 다시 보내면 재발급이 거절됨 → "다시 로그인하세요"(Expired)를 띄우지 않고 부른 쪽이 마무리(로그아웃과 같음)
        public async Task DeleteMeAsync()
        {
            c.Session.LoggingOut = true;
            try { await c.SendAsync<object>("DELETE", "/v1/users/me", null, AuthMode.User); }
            finally { c.Session.LoggingOut = false; }
        }

        // 홈 한 번에(B-01–B-04, 계획 0057)
        public async Task<HomeData> HomeAsync() =>
            await c.GetAsync<HomeData>("/v1/home", AuthMode.User) ?? throw new ApiException(ApiFailure.BadResponse, "홈 응답이 비었습니다.");

        // 레벨·인증 횟수(H-01·H-04, 계획 0048)
        public async Task<Progress> ProgressAsync() =>
            await c.GetAsync<Progress>("/v1/users/me/progress", AuthMode.User) ?? throw new ApiException(ApiFailure.BadResponse, "레벨 응답이 비었습니다.");

        // 내 보정(계획 0023). 없으면 null. 저장은 덮어쓰기(서버는 이력으로 쌓고 가장 최근 것이 지금 보정)
        public Task<CalibrationRecord> GetCalibrationAsync() =>
            c.GetAsync<CalibrationRecord>("/v1/users/me/calibration", AuthMode.User);

        public Task<CalibrationRecord> PutCalibrationAsync(CalibrationUpload body) =>
            c.SendAsync<CalibrationRecord>("PUT", "/v1/users/me/calibration", body, AuthMode.User);

        // 주간 목표 진행(D-01 "이번 주 목표까지 N회", 홈 B-01). 목표를 안 정했으면 TargetCount = null. DB 연결은 S6 BL-30
        public Task<WeeklyGoal> GetWeeklyGoalAsync() =>
            c.GetAsync<WeeklyGoal>("/v1/users/me/weekly-goal", AuthMode.User);

        // 주간 목표 정하기(A-16 온보딩, 계획 0031·H-08 마이). 1–7회. 서버 DB 연결은 지환 BL-30(그 전엔 예시 응답만)
        public Task<WeeklyGoal> PutWeeklyGoalAsync(WeeklyGoalUpdate body) =>
            c.SendAsync<WeeklyGoal>("PUT", "/v1/users/me/weekly-goal", body, AuthMode.User);
    }

    // 코스 목록·상세·맵 입장(계획 0021·PR #30). 셋 다 로그인 필요
    public sealed class CoursesApi
    {
        readonly ApiClient c;
        public CoursesApi(ApiClient client) => c = client;

        public Task<List<Course>> ListAsync() => c.GetAsync<List<Course>>("/v1/courses", AuthMode.User);

        public Task<CourseDetail> GetAsync(string courseId) =>
            c.GetAsync<CourseDetail>("/v1/courses/" + Uri.EscapeDataString(courseId), AuthMode.User);

        // 자리 잡기. 실패: 409 CALIBRATION_REQUIRED(보정 먼저) / 409 COURSE_FULL(다른 코스) / 404 COURSE_NOT_FOUND
        public async Task<MapEntry> EnterAsync(string courseId)
        {
            var e = await c.PostAsync<MapEntry>("/v1/courses/" + Uri.EscapeDataString(courseId) + "/entries", null, AuthMode.User);
            if (e == null) throw new ApiException(ApiFailure.BadResponse, "입장 응답이 비었습니다.");
            return e;
        }
    }

    public sealed class RunsApi
    {
        readonly ApiClient c;
        public RunsApi(ApiClient client) => c = client;

        // 카운트다운 끝(D-09, 계획 0035). 같은 clientRunId를 다시 보내면 서버가 처음 결과를 돌려줌(다시 보내도 기록 하나)
        public async Task<RunStarted> StartAsync(RunStartBody body)
        {
            var r = await c.PostAsync<RunStarted>("/v1/runs", body, AuthMode.User);
            if (r == null || string.IsNullOrEmpty(r.RunId)) throw new ApiException(ApiFailure.BadResponse, "러닝 시작 응답이 비었습니다.");
            return r;
        }

        // 내 끝난 기록(최신순 20개씩). cursor는 앞 쪽의 NextCursor, month(YYYY-MM)는 그 달만(F-01 달 칩, 서버가 사용자 시간대로 자름)
        public async Task<RunListPage> ListAsync(string cursor = null, string month = null)
        {
            var query = new List<string>();
            if (month != null) query.Add("month=" + Uri.EscapeDataString(month));
            if (cursor != null) query.Add("cursor=" + Uri.EscapeDataString(cursor));
            return await c.GetAsync<RunListPage>(query.Count == 0 ? "/v1/runs" : "/v1/runs?" + string.Join("&", query), AuthMode.User)
                   ?? new RunListPage();
        }

        // 기록 상세(E-12 결과·F-03, 계획 0051). 내 끝난 기록만
        public async Task<RunDetail> GetAsync(string runId) =>
            await c.GetAsync<RunDetail>("/v1/runs/" + Uri.EscapeDataString(runId), AuthMode.User)
            ?? throw new ApiException(ApiFailure.BadResponse, "기록 응답이 비었습니다.");

        // 친구 피드 공유 켜기·끄기(E-12). 기록 값은 못 바꿈
        public async Task<RunSummary> SetSharedAsync(string runId, bool shared) =>
            await c.SendAsync<RunSummary>("PATCH", "/v1/runs/" + Uri.EscapeDataString(runId), new { shared }, AuthMode.User)
            ?? throw new ApiException(ApiFailure.BadResponse, "공유 응답이 비었습니다.");

        // 이번 운동 반영 결과(E-11, 계획 0048). 정해지기 전이면 RUN_NOT_FINISHED
        public async Task<RunRewards> RewardsAsync(string runId) =>
            await c.GetAsync<RunRewards>("/v1/runs/" + Uri.EscapeDataString(runId) + "/rewards", AuthMode.User)
            ?? throw new ApiException(ApiFailure.BadResponse, "반영 결과 응답이 비었습니다.");

        // 고스트 재생용 시계열(계획 0050). 내 끝난 기록만
        public async Task<GhostTrackBody> GhostAsync(string runId) =>
            await c.GetAsync<GhostTrackBody>("/v1/runs/" + Uri.EscapeDataString(runId) + "/ghost", AuthMode.User)
            ?? throw new ApiException(ApiFailure.BadResponse, "고스트 응답이 비었습니다.");

        // 달리는 동안 60초마다(계획 0037). 같은 offsetS 묶음을 다시 보내면 덮어씀(다시 보내도 하나)
        public Task PutSamplesAsync(string runId, RunSamplesBody body) =>
            c.SendAsync<object>("PUT", $"/v1/runs/{Uri.EscapeDataString(runId)}/samples", body, AuthMode.User);

        // 앱이 꺼졌다 켜졌을 때 끝나지 않은 러닝(I-04, 계획 0052). 마지막 갱신 24시간 안의 가장 최근 하나, 없으면 Run = null
        public async Task<ActiveRun> GetActiveAsync() =>
            await c.GetAsync<ActiveRun>("/v1/runs/active", AuthMode.User) ?? new ActiveRun();

        // I-04 "버리기": 끝나지 않은 러닝만(두 번 해도 됨). 끝난 기록이면 RUN_ALREADY_FINISHED
        public Task DiscardAsync(string runId) =>
            c.PostAsync<object>($"/v1/runs/{Uri.EscapeDataString(runId)}/discard", null, AuthMode.User);

        // 끝(D-18·D-19 → D-22). 같은 endedAt·값으로 다시 보내면 처음 결과
        public async Task<RunSummary> FinishAsync(string runId, RunFinishBody body)
        {
            var r = await c.PostAsync<RunSummary>($"/v1/runs/{Uri.EscapeDataString(runId)}/finish", body, AuthMode.User);
            if (r == null || string.IsNullOrEmpty(r.RunId)) throw new ApiException(ApiFailure.BadResponse, "러닝 종료 응답이 비었습니다.");
            return r;
        }

        // D-22 "촬영 없이 마무리"(미인증으로 저장, 나중에 인증할 수 없음)
        public Task SkipVerificationAsync(string runId) =>
            c.PostAsync<object>($"/v1/runs/{Uri.EscapeDataString(runId)}/verification/skip", new { reason = "SKIPPED" }, AuthMode.User);

        // ---- 계기판 인증(계획 0038) ----
        public const int PhotoUploadTimeoutSeconds = 60; // 사진 세 장 + 서버 OCR(시간 제한 15초)

        // E-04 "이 사진으로 인증" → E-05. 브래킷 사진(1–3장, JPEG)을 photos로
        public async Task<VerificationResult> VerifyPhotoAsync(string runId, IReadOnlyList<MultipartBody.FilePart> photos)
        {
            var body = MultipartBody.Files("photos", photos);
            var r = await c.PostMultipartAsync<VerificationResult>($"/v1/runs/{Uri.EscapeDataString(runId)}/verification/photo",
                                                                   body, AuthMode.User, PhotoUploadTimeoutSeconds);
            return r ?? throw new ApiException(ApiFailure.BadResponse, "인증 응답이 비었습니다.");
        }

        // 읽은 값 확인(E-05b, 계획 0045): correct = 맞아요(지금 판정)·틀렸어요(다시 찍기, 횟수 안 씀)
        public async Task<VerificationResult> ConfirmReadAsync(string runId, string attemptId, bool correct)
        {
            var r = await c.PostAsync<VerificationResult>($"/v1/runs/{Uri.EscapeDataString(runId)}/verification/confirm",
                                                          new { attemptId, correct }, AuthMode.User);
            return r ?? throw new ApiException(ApiFailure.BadResponse, "인증 응답이 비었습니다.");
        }

        // 미인증으로 저장: SKIPPED(촬영 없이·카메라 권한 없음)·TIMEOUT(E-10)·RETAKE_GIVEN_UP(E-07·08·09에서 그만둠)
        public async Task<VerificationResult> SaveUnverifiedAsync(string runId, string reason)
        {
            var r = await c.PostAsync<VerificationResult>($"/v1/runs/{Uri.EscapeDataString(runId)}/verification/skip",
                                                          new { reason }, AuthMode.User);
            return r ?? throw new ApiException(ApiFailure.BadResponse, "인증 응답이 비었습니다.");
        }
    }

    // 친구(계획 0044). 친구가 아닌 사람의 ID → 404 USER_NOT_FOUND
    public sealed class FriendsApi
    {
        readonly ApiClient c;
        public FriendsApi(ApiClient client) => c = client;

        public Task<List<Friend>> ListAsync() => c.GetAsync<List<Friend>>("/v1/friends", AuthMode.User);

        public Task<List<RunningFriend>> RunningAsync() => c.GetAsync<List<RunningFriend>>("/v1/friends/running", AuthMode.User);

        // 같이 달리기(G-03, 계획 0047): 친구가 courseId에서 달리는 중이면 그 맵에 입장(응답은 맵 입장과 같음 + NearFriend).
        // 실패: 409 FRIEND_NOT_RUNNING(그새 끝냄·코스를 옮김)·404 USER_NOT_FOUND(친구 아님) → 앱은 그 코스로 일반 입장. 그 밖은 맵 입장과 같음
        public async Task<MapEntry> RunTogetherAsync(string userId, string courseId)
        {
            var e = await c.PostAsync<MapEntry>("/v1/friends/" + Uri.EscapeDataString(userId) + "/run-together", new { courseId }, AuthMode.User);
            return e ?? throw new ApiException(ApiFailure.BadResponse, "입장 응답이 비었습니다.");
        }

        // 받은 코드로 추가 → 서로 바로 친구. 실패: SELF_FRIEND_CODE·FRIEND_CODE_NOT_FOUND·ALREADY_FRIEND·
        // FRIEND_LIMIT_EXCEEDED(details.who ME/FRIEND)·RATE_LIMITED(틀린 코드 10분에 10번)
        public async Task<Friend> AddAsync(string friendCode)
        {
            var f = await c.PostAsync<Friend>("/v1/friends", new { friendCode }, AuthMode.User);
            return f ?? throw new ApiException(ApiFailure.BadResponse, "친구 추가 응답이 비었습니다.");
        }

        public async Task<FriendProfile> ProfileAsync(string userId)
        {
            var p = await c.GetAsync<FriendProfile>("/v1/friends/" + Uri.EscapeDataString(userId), AuthMode.User);
            return p ?? throw new ApiException(ApiFailure.BadResponse, "친구 프로필 응답이 비었습니다.");
        }

        public Task DeleteAsync(string userId) =>
            c.SendAsync<object>("DELETE", "/v1/friends/" + Uri.EscapeDataString(userId), null, AuthMode.User);

        // 친구 피드(끝난 시각 최신순, 20개씩). 다음 쪽은 앞 응답의 NextCursor
        public async Task<FeedPage> FeedAsync(string cursor = null)
        {
            var path = cursor == null ? "/v1/feed" : "/v1/feed?cursor=" + Uri.EscapeDataString(cursor);
            return await c.GetAsync<FeedPage>(path, AuthMode.User) ?? new FeedPage();
        }
    }
}
