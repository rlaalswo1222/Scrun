using System.Collections.Generic;

namespace Scrun.App
{
    // 같이 달리기(계획 0047): 3·2·1이 끝날 때 내가 설 자리. 섬을 불러오고 러닝 설정·센서 준비·3·2·1을 하는 동안 친구는 계속 달려서
    // 입장 응답 때 위치로는 친구가 멀리 앞에 있다 → 실시간 방에서 받은 친구의 지금 위치 BehindM 뒤로 다시 맞춘다(정할 것 2 가).
    // 그때까지 섬은 흐린 정지 그림이라 옮기는 게 안 보이고, 3·2·1이 끝나며 섬이 처음 움직일 때 친구가 바로 앞에 있다
    public static class NearFriendStart
    {
        // 친구가 방에 있으면 그 뒤 behindM(한 바퀴 안, m), 없으면 null(친구가 그새 끝냄 → 그 자리에서 시작, 정할 것 3 가)
        public static float? Target(IReadOnlyList<RemoteRunner> runners, string friendUserId, float behindM, float lapM, double now)
        {
            if (runners == null || string.IsNullOrEmpty(friendUserId) || lapM <= 0f) return null;
            foreach (var r in runners)
                if (r.UserId == friendUserId && !r.IsBot)
                    return LoopMath.Wrap(r.PositionAt(now) - behindM, lapM);
            return null;
        }
    }
}
