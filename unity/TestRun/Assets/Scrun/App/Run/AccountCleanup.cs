namespace Scrun.App
{
    // 계정 삭제 뒤 이 기기에 남은 그 계정 것 지우기(계획 0058). 로그인 토큰·가입 직후 표시는 AppRoot.GoToLogin이 지움.
    // 다른 계정이 남긴 기록(같은 폰을 같이 쓴 경우)은 그대로 둔다
    public static class AccountCleanup
    {
        public static void Forget(string userId, PendingRunStore pending = null, ActiveRunStore active = null)
        {
            if (string.IsNullOrEmpty(userId)) return;
            (pending ?? PendingRunStore.Default).RemoveUser(userId);   // 못 보낸 기록(보내면 401뿐)
            active ??= ActiveRunStore.Default;
            if (active.Load()?.UserId == userId) active.Clear();         // 끝나지 않은 러닝(I-04로 묻지 않게)
        }
    }
}
