using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Scrun.App
{
    // 홈을 그린 뒤 기록·친구 탭 첫 화면을 뒤에서 받아 둠(계획 0074 B). 받은 응답은 ApiClient가 캐시에 넣고, 탭을 처음 열면
    // 그 캐시로 바로 그린 뒤 새로 받는다. 이번 실행에 한 번만, 둘씩 나눠 보냄(앱 켜는 순간 요청이 몰리지 않게).
    // 실패해도 아무것도 안 함(탭을 열면 평소처럼 받음). 셀룰러에서도 함(응답이 작음, 계획 0074 정한 것 3)
    public static class TabPrefetch
    {
        static bool started;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => started = false;

        public static async void Run()
        {
            if (started) return;
            started = true;
            try
            {
                string thisMonth = RecordsText.MonthChips(DateTime.UtcNow)[0].month; // 기록 탭은 이번 달 칩부터
                await Task.WhenAll(Api.Runs.ListAsync(), Api.Runs.ListAsync(month: thisMonth));
                await Task.WhenAll(Api.Users.GetWeeklyGoalAsync(), Api.Friends.ListAsync());
                await Task.WhenAll(Api.Friends.RunningAsync(), Api.Friends.FeedAsync());
            }
            catch (ApiException e)
            {
                Debug.Log("[미리 받기] " + e.Message); // 탭을 열면 다시 받음
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
