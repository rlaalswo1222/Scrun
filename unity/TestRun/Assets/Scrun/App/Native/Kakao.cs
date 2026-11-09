using UnityEngine;

namespace Scrun.App
{
    // 카카오 로그인(카카오톡 앱 또는 카카오 계정 웹 로그인). 성공하면 카카오 access 토큰을 주고,
    // 우리 서버(Api.Auth.KakaoLoginAsync)가 그 토큰을 카카오에 확인한다.
    // 아이폰은 카카오 iOS SDK(계획 0018의 6번), 에디터는 가짜(Fake Native 창의 토큰, 로컬 서버의 가짜 카카오용).
    public interface IKakaoLogin
    {
        // 카카오 access 토큰. 사용자가 취소하면 null
        Awaitable<string> LoginAsync();
    }
}
