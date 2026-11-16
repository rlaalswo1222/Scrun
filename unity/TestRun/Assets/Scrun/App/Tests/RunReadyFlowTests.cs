using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Scrun.App.Tests
{
    // 센서 준비·달림 인식·카운트다운(계획 0035): 단계 판단, 줄 문구, POST /runs 다시 보내기
    public class RunReadyFlowTests
    {
        static ReadyInputs Ok(bool stepping = false) => new ReadyInputs
        {
            Phone = SensorFlow.Ok,
            AirPods = SensorFlow.Off,
            Headphones = HeadphoneStatus.NotConnected,
            Calibration = CalibrationState.Ready,
            Network = RunTogetherStatus.Connected,
            Stepping = stepping,
        };

        // 걷는 중(AvatarSpeed.Stepping)
        static ReadyInputs Walk() => Ok(true);

        static RunReadyFlow Begun()
        {
            var f = new RunReadyFlow();
            f.Begin();
            return f;
        }

        // 기다림(D-08)까지
        static RunReadyFlow AtWaiting()
        {
            var f = Begun();
            f.Update(0, Ok());
            f.Update(RunReadyFlow.AutoAdvanceSec, Ok());
            Assert.AreEqual(ReadyStep.Waiting, f.Step);
            return f;
        }

        // ---------- D-07 센서 확인 ----------
        [Test]
        public void AllGoodAdvancesAfterShowingRowsBriefly()
        {
            var f = Begun();
            Assert.AreEqual(ReadyStep.Checking, f.Update(0.2, Ok()));
            Assert.AreEqual(ReadyStep.Checking, f.Update(1.6, Ok()), "정상이 된 0.2초부터 1.5초를 보여 줌");
            Assert.AreEqual(ReadyStep.Waiting, f.Update(1.7, Ok()));
        }

        [Test]
        public void ReadyButtonSkipsTheWaitOnlyWhenReady()
        {
            var f = Begun();
            var waiting = Ok();
            waiting.Phone = SensorFlow.Waiting;
            Assert.IsFalse(f.Skip(waiting), "폰이 아직이면 누를 수 없음");
            Assert.IsTrue(f.Skip(Ok()));
            Assert.AreEqual(ReadyStep.Waiting, f.Step);
        }

        [Test]
        public void PhoneThatNeverArrivesFailsAfterTimeout()
        {
            var f = Begun();
            var i = Ok();
            i.Phone = SensorFlow.Waiting;
            f.Update(10, i); // 들어온 프레임부터 안 옴
            Assert.AreEqual(ReadyStep.Checking, f.Update(10 + RunReadyFlow.CheckTimeoutSec - 0.1, i));
            Assert.AreEqual(ReadyStep.SensorFailed, f.Update(10 + RunReadyFlow.CheckTimeoutSec, i));
        }

        [Test]
        public void LostPhoneFailsAtOnce()
        {
            var f = Begun();
            var i = Ok();
            i.Phone = SensorFlow.Lost;
            Assert.AreEqual(ReadyStep.SensorFailed, f.Update(0.1, i));
        }

        [Test]
        public void EarphoneAndNetworkNeverBlock()
        {
            var f = Begun();
            var i = Ok();
            i.Headphones = HeadphoneStatus.Connected;
            i.HeadphoneMotionGranted = true;
            i.AirPods = SensorFlow.Lost;
            i.Network = RunTogetherStatus.Stopped;
            f.Update(0, i);
            Assert.AreEqual(ReadyStep.Waiting, f.Update(RunReadyFlow.AutoAdvanceSec, i));

            var solo = Ok();
            solo.Network = null; // 실시간 방 없음
            var g = Begun();
            g.Update(0, solo);
            Assert.AreEqual(ReadyStep.Waiting, g.Update(RunReadyFlow.AutoAdvanceSec, solo));
        }

        [Test]
        public void WaitsForCalibrationAndRestartsTheShortPause()
        {
            var f = Begun();
            var loading = Ok();
            loading.Calibration = CalibrationState.Loading;
            f.Update(0, loading);
            Assert.AreEqual(ReadyStep.Checking, f.Update(20, loading), "보정은 시간 제한 없이 기다림(폰만 5초)");
            Assert.IsFalse(f.Skip(loading));
            f.Update(21, Ok());
            Assert.AreEqual(ReadyStep.Checking, f.Update(22, Ok()));
            Assert.AreEqual(ReadyStep.Waiting, f.Update(22.5, Ok()));
        }

        [Test]
        public void RetryFromFailureStartsOver()
        {
            var f = Begun();
            var lost = Ok();
            lost.Phone = SensorFlow.Lost;
            f.Update(0, lost);
            f.Retry();
            Assert.AreEqual(ReadyStep.Checking, f.Step);
            var waiting = Ok();
            waiting.Phone = SensorFlow.Waiting;
            Assert.AreEqual(ReadyStep.Checking, f.Update(30 + RunReadyFlow.CheckTimeoutSec - 0.1, waiting), "다시 시도한 시각부터 5초");
        }

        // ---------- D-08 → D-09 → 시작 ----------
        [Test]
        public void StepsStartCountdownThenRunAfterThreeSeconds()
        {
            var f = AtWaiting();
            Assert.AreEqual(ReadyStep.Waiting, f.Update(5, Ok()));
            Assert.AreEqual(ReadyStep.Countdown, f.Update(6, Walk()), "걷기 시작(BL-11)");
            Assert.AreEqual(3, f.CountdownNumber);
            f.Update(6.5, Walk());
            Assert.AreEqual(3, f.CountdownNumber);
            f.Update(7.2, Walk());
            Assert.AreEqual(2, f.CountdownNumber);
            f.Update(8.9, Walk());
            Assert.AreEqual(1, f.CountdownNumber);
            Assert.AreEqual(ReadyStep.Started, f.Update(9, Walk()));
            Assert.AreEqual(0, f.CountdownNumber);
            Assert.AreEqual(ReadyStep.Started, f.Update(20, Ok()), "시작한 뒤에는 걸음이 끊겨도 그대로(멈춤은 BL-13)");
        }

        [Test]
        public void SteppingOffDuringCountdownGoesBackToWaiting()
        {
            var f = AtWaiting();
            f.Update(5, Walk());
            Assert.AreEqual(ReadyStep.Waiting, f.Update(6, Ok()));
            Assert.AreEqual(0, f.CountdownNumber);
            f.Update(7, Walk());
            Assert.AreEqual(3, f.CountdownNumber, "다시 걸으면 처음부터 셈");
            Assert.AreEqual(ReadyStep.Countdown, f.Update(9.9, Walk()));
            Assert.AreEqual(ReadyStep.Started, f.Update(10, Walk()));
        }

        [Test]
        public void UnusableCalibrationSendsToCalibrationInsteadOfWaitingForever()
        {
            var f = Begun();
            var i = Ok();
            i.Calibration = CalibrationState.RetryLater; // 일시 실패: 기다리며 다시 시도
            Assert.AreEqual(ReadyStep.Checking, f.Update(10, i));
            i.Calibration = CalibrationState.NeedsCalibration;
            Assert.AreEqual(ReadyStep.NeedsCalibration, f.Update(11, i));
            Assert.IsFalse(f.Cancel(), "이미 맵을 나가는 중");
            Assert.AreEqual(RowTone.Failed, RunReadyText.Calibration(CalibrationState.NeedsCalibration).Tone);
        }

        [Test]
        public void BlurredStepsMatchFigma()
        {
            Assert.IsTrue(RunReadyFlow.Blurred(ReadyStep.Checking));
            Assert.IsTrue(RunReadyFlow.Blurred(ReadyStep.Waiting));
            Assert.IsTrue(RunReadyFlow.Blurred(ReadyStep.SensorFailed));
            Assert.IsFalse(RunReadyFlow.Blurred(ReadyStep.Countdown), "D-09는 흐림이 풀림");
        }

        // ---------- 리뷰 반영(11/16) ----------
        [Test]
        public void PhoneTimeoutCountsFromWhenPhoneWentBadNotFromEntering()
        {
            var f = Begun();
            var loading = Ok();
            loading.Calibration = CalibrationState.Loading; // 보정이 8초 늦게 옴, 그동안 폰은 정상
            f.Update(0, loading);
            Assert.AreEqual(ReadyStep.Checking, f.Update(8, loading));
            var blip = loading;
            blip.Phone = SensorFlow.Waiting;
            Assert.AreEqual(ReadyStep.Checking, f.Update(8.1, blip), "들어온 지 8초여도 폰이 나빠진 지 0초");
            Assert.AreEqual(ReadyStep.Checking, f.Update(8.1 + RunReadyFlow.CheckTimeoutSec - 0.05, blip));
            Assert.AreEqual(ReadyStep.SensorFailed, f.Update(8.1 + RunReadyFlow.CheckTimeoutSec, blip));

            var g = Begun();
            g.Update(0, blip);
            g.Update(3, loading); // 3초에 정상으로 → 다시 0부터
            g.Update(4, blip);
            Assert.AreEqual(ReadyStep.Checking, g.Update(4 + RunReadyFlow.CheckTimeoutSec - 0.05, blip));
        }

        [Test]
        public void CalibrationLoadResultsAndErrorsMapToRowStates()
        {
            Assert.AreEqual(CalibrationState.Ready, ReadyCalibration.From(CalibrationLoad.Loaded));
            Assert.AreEqual(CalibrationState.NeedsCalibration, ReadyCalibration.From(CalibrationLoad.NotOnServer));
            Assert.AreEqual(CalibrationState.NeedsCalibration, ReadyCalibration.From(CalibrationLoad.Unreadable));
            Assert.AreEqual(CalibrationState.Cancelled, ReadyCalibration.From(CalibrationLoad.Cancelled), "로그인이 바뀜 → 보정 안내로 보내지 않음");

            Assert.AreEqual(CalibrationState.RetryLater, ReadyCalibration.From(new ApiException(ApiFailure.Network, "끊김")));
            Assert.AreEqual(CalibrationState.RetryLater, ReadyCalibration.From(new ApiException(ApiFailure.Server, "서버", 503, null)));
            Assert.AreEqual(CalibrationState.RetryLater, ReadyCalibration.From(new ApiException(ApiFailure.BadResponse, "502 HTML", 502)));
            Assert.AreEqual(CalibrationState.Failed, ReadyCalibration.From(new ApiException(ApiFailure.Server, "없음", 404, "NOT_FOUND")));
            Assert.AreEqual(CalibrationState.Failed, ReadyCalibration.From(new ApiException(ApiFailure.Server, "틀림", 422, "VALIDATION_ERROR")));
            Assert.AreEqual(CalibrationState.Failed, ReadyCalibration.From(new ApiException(ApiFailure.BadResponse, "못 읽음", 404)), "본문 못 읽은 4xx도 다시 해도 같음");
            Assert.AreEqual(CalibrationState.Cancelled, ReadyCalibration.From(new ApiException(ApiFailure.NotLoggedIn, "만료", 401)));

            Assert.IsTrue(RunReadyFlow.CanRetryCalibration(CalibrationState.RetryLater));
            Assert.IsFalse(RunReadyFlow.CanRetryCalibration(CalibrationState.Failed), "눌러도 같은 실패라 버튼이 아님");
        }

        [Test]
        public void PermanentCalibrationFailureStaysButCanStillLeave()
        {
            var f = Begun();
            var i = Ok();
            i.Calibration = CalibrationState.Failed;
            Assert.AreEqual(ReadyStep.Checking, f.Update(30, i), "넘어가지도 않고 보정 안내로도 안 감");
            Assert.IsFalse(f.Skip(i));
            Assert.IsTrue(f.Cancel(), "X → D-01");

            var lost = Ok();
            lost.Phone = SensorFlow.Lost;
            var g = Begun();
            g.Update(0, lost);
            Assert.AreEqual(ReadyStep.SensorFailed, g.Step);
            Assert.IsTrue(g.Cancel(), "I-01에서도 X → D-01");
        }

        // #49 2차 리뷰: 센서가 꺼지면(Off) Lost가 되지 않아 D-08이 조용히 기다리기만 했음
        [Test]
        public void SensorTurnedOffWhileWaitingOrCountingFailsAfterTimeout()
        {
            var off = Ok();
            off.Phone = SensorFlow.Off;

            var f = AtWaiting();
            Assert.AreEqual(ReadyStep.Waiting, f.Update(2, off), "잠깐 꺼짐은 기다림");
            Assert.AreEqual(ReadyStep.Waiting, f.Update(3, Ok()), "다시 켜지면 시간도 처음부터");
            f.Update(4, off);
            Assert.AreEqual(ReadyStep.Waiting, f.Update(4 + RunReadyFlow.CheckTimeoutSec - 0.1, off));
            Assert.AreEqual(ReadyStep.SensorFailed, f.Update(4 + RunReadyFlow.CheckTimeoutSec, off));

            var g = AtWaiting();
            g.Update(2, Walk());
            Assert.AreEqual(ReadyStep.Countdown, g.Step);
            var offWalking = Walk();
            offWalking.Phone = SensorFlow.Off;
            Assert.AreEqual(ReadyStep.Countdown, g.Update(2.1, offWalking));
            Assert.AreEqual(ReadyStep.SensorFailed, g.Update(2.1 + RunReadyFlow.CheckTimeoutSec, offWalking));
        }

        [Test]
        public void OnlyNetworkAndServerErrorsAreTransient()
        {
            Assert.IsTrue(new ApiException(ApiFailure.Network, "끊김").IsTransient);
            Assert.IsTrue(new ApiException(ApiFailure.Server, "서버", 500).IsTransient);
            Assert.IsTrue(new ApiException(ApiFailure.BadResponse, "502 HTML", 502).IsTransient);
            Assert.IsFalse(new ApiException(ApiFailure.BadResponse, "빈 runId").IsTransient, "답은 왔는데 내용이 이상함 → 다시 보내도 같음(#49 2차 리뷰)");
            Assert.IsFalse(new ApiException(ApiFailure.BadResponse, "못 읽은 4xx", 400).IsTransient);
            Assert.IsFalse(new ApiException(ApiFailure.Server, "틀림", 400).IsTransient);
            Assert.IsFalse(new ApiException(ApiFailure.NotLoggedIn, "만료").IsTransient);
        }

        [Test]
        public async Task RunStartRetriesUseTheRealtimeReconnectDelays()
        {
            var waited = new List<int>();
            var sender = new RunStartSender(_ => throw new ApiException(ApiFailure.Network, "끊김"), (ms, ct) =>
            {
                waited.Add(ms);
                return Task.CompletedTask;
            });
            Assert.IsNull(await sender.SendAsync(Body(), CancellationToken.None));
            Assert.AreEqual(new[] { 1000, 2000, 4000, 8000 }, waited.ToArray(), "같이 뛰기 다시 붙기(ReconnectPolicy)와 같은 간격");

            int calls = 0;
            var bad = new RunStartSender(_ => { calls++; throw new ApiException(ApiFailure.BadResponse, "못 읽은 400", 400); }, NoWait);
            Assert.IsNull(await bad.SendAsync(Body(), CancellationToken.None));
            Assert.AreEqual(1, calls, "본문 못 읽은 4xx는 다시 안 보냄");
        }

        // ---------- 아바타와 같은 걸음 판단(AvatarSpeed, PR #49 리뷰 3번) ----------
        // 러닝머신 흉내: 30 fps로 센서 값(누적 걸음)을 만들어 AvatarSpeed → RunReadyFlow 순서로 넣음(RunReadyView와 같은 순서)
        sealed class Treadmill
        {
            public readonly RunReadyFlow Flow = new RunReadyFlow();
            public readonly AvatarSpeed Walking = new AvatarSpeed();
            double t, stepAcc;
            int steps;

            public Treadmill()
            {
                Flow.Begin();
                Run(RunReadyFlow.AutoAdvanceSec + 0.1, 0); // D-08까지
                Assert.AreEqual(ReadyStep.Waiting, Flow.Step);
            }

            // sec초 동안 spm 빠르기로 걸음(0이면 서 있음). 지나간 단계들을 돌려줌
            public List<ReadyStep> Run(double sec, double spm)
            {
                var seen = new List<ReadyStep>();
                for (int n = (int)Math.Round(sec * 30); n > 0; n--)
                {
                    t += 1 / 30.0;
                    stepAcc += spm / 60.0 / 30.0;
                    while (stepAcc >= 1) { steps++; stepAcc -= 1; }
                    Walking.Update(new SpeedReading { Time = t, Steps = steps }, true);
                    var i = Ok(Walking.Stepping);
                    seen.Add(Flow.Update(t, i));
                }
                return seen;
            }
        }

        [Test]
        public void WalkingStartsCountdownLikeTheAvatar()
        {
            var tm = new Treadmill();
            var seen = tm.Run(1.2, 120); // 걸음 사이 0.5초, 두 번째 걸음에 출발
            Assert.Contains(ReadyStep.Countdown, seen);
            Assert.IsTrue(tm.Walking.Stepping);
            Assert.Contains(ReadyStep.Started, tm.Run(3.1, 120));
        }

        [Test]
        public void SteppingOffDuringCountdownWaitsWithoutFlicker()
        {
            var tm = new Treadmill();
            tm.Run(1.2, 120);
            Assert.AreEqual(ReadyStep.Countdown, tm.Flow.Step);
            tm.Run(AvatarSpeed.StopAfterSec + 0.2, 0);
            Assert.AreEqual(ReadyStep.Waiting, tm.Flow.Step, "1.2초 안에 D-08로(3초 카운트다운이 끝나기 전)");
            CollectionAssert.DoesNotContain(tm.Run(4, 0), ReadyStep.Countdown, "서 있는 동안 다시 세지 않음(깜박임 없음)");
            Assert.Contains(ReadyStep.Countdown, tm.Run(1.2, 120), "다시 걸으면 셈");
        }

        [Test]
        public void BeltVibrationOnTheSidePlatformNeverStartsCountdown()
        {
            var tm = new Treadmill();
            CollectionAssert.DoesNotContain(tm.Run(10, 222), ReadyStep.Countdown, "가만히 둔 폰이 잡은 222 spm(계획 0024)은 걸음이 아님");
            Assert.IsFalse(tm.Walking.Stepping, "아바타도 서 있음");
        }

        [Test]
        public void OneBumpDoesNotStartCountdown()
        {
            var tm = new Treadmill();
            var seen = tm.Run(0.6, 120); // 걸음 하나
            seen.AddRange(tm.Run(3, 0));
            CollectionAssert.DoesNotContain(seen, ReadyStep.Countdown, "걸음 2번이 이어져야 출발(아바타와 같음)");
        }

        [Test]
        public void LostPhoneWhileWaitingOrCountingFails()
        {
            var lost = Walk();
            lost.Phone = SensorFlow.Lost;
            var f = AtWaiting();
            Assert.AreEqual(ReadyStep.SensorFailed, f.Update(5, lost));

            var g = AtWaiting();
            g.Update(5, Walk());
            Assert.AreEqual(ReadyStep.SensorFailed, g.Update(6, lost));
        }

        [Test]
        public void CancelWorksUntilTheRunStarts()
        {
            var f = AtWaiting();
            f.Update(5, Walk());
            Assert.IsTrue(f.Cancel());
            Assert.AreEqual(ReadyStep.Cancelled, f.Step);
            Assert.AreEqual(ReadyStep.Cancelled, f.Update(10, Walk()), "취소한 뒤에는 걸어도 시작 안 함");

            var g = AtWaiting();
            g.Update(5, Walk());
            g.Update(8, Walk());
            Assert.IsFalse(g.Cancel(), "이미 시작함");
            Assert.AreEqual(ReadyStep.Started, g.Step);
        }

        // ---------- 줄 문구 ----------
        [Test]
        public void EarphoneRowShowsConnectedOnlyWithMotionData()
        {
            Assert.AreEqual(RowTone.Ok, RunReadyText.Earphone(HeadphoneStatus.Connected, true, SensorFlow.Ok).Tone);
            Assert.AreEqual(RowTone.Pending, RunReadyText.Earphone(HeadphoneStatus.Connected, true, SensorFlow.Waiting).Tone);
            Assert.AreEqual("이어폰 없이 진행", RunReadyText.Earphone(HeadphoneStatus.Connected, true, SensorFlow.Lost).Chip);
            Assert.AreEqual("이어폰 없이 진행", RunReadyText.Earphone(HeadphoneStatus.Connected, false, SensorFlow.Ok).Chip, "헤드폰 모션 권한 없음");
            Assert.AreEqual("이어폰 없이 진행", RunReadyText.Earphone(HeadphoneStatus.ConnectedNoMotion, true, SensorFlow.Off).Chip);
            Assert.AreEqual("이어폰 없이 진행", RunReadyText.Earphone(HeadphoneStatus.NotConnected, true, SensorFlow.Off).Chip);
        }

        [Test]
        public void RowsMatchFigmaWhenAllGood()
        {
            Assert.AreEqual("정상", RunReadyText.Phone(SensorFlow.Ok).Chip);
            Assert.AreEqual("걸음·발 충격 측정", RunReadyText.Phone(SensorFlow.Ok).Sub);
            Assert.AreEqual("연결됨", RunReadyText.Earphone(HeadphoneStatus.Connected, true, SensorFlow.Ok).Chip);
            Assert.AreEqual("헤드폰 모션 사용", RunReadyText.Earphone(HeadphoneStatus.Connected, true, SensorFlow.Ok).Sub);
            Assert.AreEqual("정상", RunReadyText.Calibration(CalibrationState.Ready).Chip);
            Assert.AreEqual("정상", RunReadyText.Network(RunTogetherStatus.Connected).Chip);
            Assert.AreEqual("공동 맵 연결", RunReadyText.Network(RunTogetherStatus.Connected).Sub);
            Assert.AreEqual(RowTone.Failed, RunReadyText.Calibration(CalibrationState.RetryLater).Tone);
            Assert.AreEqual(RowTone.Fallback, RunReadyText.Network(null).Tone);
            Assert.AreEqual(RowTone.Fallback, RunReadyText.Network(RunTogetherStatus.Stopped).Tone);
        }

        [Test]
        public void GoalChip()
        {
            Assert.AreEqual("목표 3.0km", RunReadyText.GoalChip(new RunGoal(GoalKind.Distance, 30)));
            Assert.AreEqual("목표 45분", RunReadyText.GoalChip(new RunGoal(GoalKind.Time, 45)));
        }

        // ---------- POST /runs ----------
        static RunStartBody Body() => new RunStartBody
        {
            ClientRunId = "c1",
            MapInstanceId = "map_crs_lake_loop",
            Goal = new RunGoalBody { Type = "DISTANCE", Value = 3000 },
            StartedAt = new DateTime(2026, 11, 16, 1, 2, 3, DateTimeKind.Utc),
        };

        static Task NoWait(int ms, CancellationToken ct) => Task.CompletedTask;

        [Test]
        public async Task RetriesNetworkFailuresWithTheSameBody()
        {
            var seen = new List<RunStartBody>();
            int calls = 0;
            var sender = new RunStartSender(b =>
            {
                seen.Add(b);
                if (++calls < 3) throw new ApiException(ApiFailure.Network, "끊김");
                return Task.FromResult(new RunStarted { RunId = "run_1", ClientRunId = b.ClientRunId });
            }, NoWait);
            var r = await sender.SendAsync(Body(), CancellationToken.None);
            Assert.AreEqual("run_1", r.RunId);
            Assert.AreEqual(3, seen.Count);
            Assert.IsTrue(seen.TrueForAll(b => b.ClientRunId == "c1"), "같은 clientRunId라 서버 기록은 하나");
        }

        [Test]
        public async Task GivesUpOnClientErrorsAndAfterLastRetry()
        {
            int calls = 0;
            var bad = new RunStartSender(_ => { calls++; throw new ApiException(ApiFailure.Server, "틀림", 400, "VALIDATION_ERROR"); }, NoWait);
            Assert.IsNull(await bad.SendAsync(Body(), CancellationToken.None));
            Assert.AreEqual(1, calls, "400은 다시 보내도 같음");

            calls = 0;
            var down = new RunStartSender(_ => { calls++; throw new ApiException(ApiFailure.Server, "서버", 503, null); }, NoWait);
            Assert.IsNull(await down.SendAsync(Body(), CancellationToken.None));
            Assert.AreEqual(1 + RunStartSender.MaxRetries, calls);
        }

        [Test]
        public void StopsWhenTheMapCloses()
        {
            using var cts = new CancellationTokenSource();
            var sender = new RunStartSender(_ => throw new ApiException(ApiFailure.Network, "끊김"), (ms, ct) =>
            {
                cts.Cancel();
                return Task.CompletedTask;
            });
            Assert.CatchAsync<OperationCanceledException>(() => sender.SendAsync(Body(), cts.Token));
        }

        [Test]
        public void SessionBuildsStartBodyOnlyAfterStart()
        {
            var s = new RunSession(new RunGoal(GoalKind.Distance, 30), () => 0);
            Assert.IsNull(s.StartBody("map_x"));
            s.Start(12f);
            var b = s.StartBody("map_x");
            Assert.AreEqual(s.ClientRunId, b.ClientRunId);
            Assert.AreEqual("map_x", b.MapInstanceId);
            Assert.AreEqual("DISTANCE", b.Goal.Type);
            Assert.AreEqual(3000, b.Goal.Value);
            Assert.AreEqual(DateTimeKind.Utc, b.StartedAt.Kind);
            Assert.AreNotEqual(s.ClientRunId, new RunSession(s.Goal).ClientRunId, "달리기마다 새 ID");
        }
    }
}
