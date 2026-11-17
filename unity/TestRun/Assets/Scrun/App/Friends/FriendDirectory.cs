using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace Scrun.App
{
    // 내 친구 ID(러너 카드 D-12 "친구" 배지, 계획 0044 5번). 친구 탭이 목록을 받을 때·맵에 들어갈 때 채운다.
    // 로그아웃·다른 계정 로그인(Session.Cleared)이면 비운다(다음 사람이 앞사람 친구를 보지 않게).
    public static class FriendDirectory
    {
        static HashSet<string> ids = new HashSet<string>();
        static bool hooked;

        public static bool Contains(string userId) => userId != null && ids.Contains(userId);

        public static int Count => ids.Count;

        public static void Set(IEnumerable<Friend> friends)
        {
            Hook();
            ids = new HashSet<string>((friends ?? Enumerable.Empty<Friend>()).Where(f => f?.UserId != null).Select(f => f.UserId));
        }

        public static void Add(Friend f)
        {
            Hook();
            if (f?.UserId != null) ids.Add(f.UserId);
        }

        public static void Remove(string userId)
        {
            if (userId != null) ids.Remove(userId);
        }

        public static void Clear() => ids = new HashSet<string>();

        // 맵 입장 때: 실패해도 화면엔 안 알림(배지만 빠짐)
        public static async void RefreshQuietly()
        {
            try { Set(await Api.Friends.ListAsync()); }
            catch (ApiException e) { Debug.LogWarning("[Friends] 친구 목록을 못 받음(러너 카드 배지 없이): " + e.Failure); }
            catch (Exception e) { Debug.LogException(e); }
        }

        static void Hook()
        {
            if (hooked) return;
            hooked = true;
            Session.Cleared += Clear;
        }

        // Play마다 비움(이 프로젝트는 Play 때 도메인 리로드를 꺼 둠)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            ids = new HashSet<string>();
            if (hooked) Session.Cleared -= Clear;
            hooked = false;
        }
    }
}
