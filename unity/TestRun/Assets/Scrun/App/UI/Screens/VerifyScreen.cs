using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Scrun.App
{
    // 계기판 인증(계획 0038, Figma E-02·E-04–E-10·I-06). D-22 "계기판 촬영하기"에서 끝낸 기록 ID로 연다.
    // 흐름 판단은 VerifyFlow, 이 화면은 그리기·카메라(Native.Camera, E-03은 Swift)·서버 호출만.
    // 끝나면(인증 완료 "다음", 미인증으로 저장) onDone → 코스 상세가 이 화면을 내리고 다시 불러옴
    public sealed class VerifyScreen : AppScreen
    {
        readonly string runId;
        readonly RunStatsBody sensor;
        readonly Action onDone;
        readonly VerifyFlow flow = new VerifyFlow();

        CameraShot shot;           // 지금 들고 있는 사진(보내거나 버리면 null)
        Texture2D preview;          // E-04 미리보기
        IVisualElementScheduledItem ticker;
        bool busy, finished;
        VisualElement guide, leave, confirm, sending, result;
        Label navTitle, chip, resultTitle, resultLead, confirmError, guideError, resultError;
        Button back, primary, secondary;
        VisualElement resultIcon, resultBody;
        Image photo;

        public VerifyScreen(string runId, RunStatsBody sensor, Action onDone)
        {
            this.runId = runId;
            this.sensor = sensor;
            this.onDone = onDone;
        }

        protected override string Uxml => "UI/Verify";
        public override bool HidesTabBar => true;

        protected override void OnCreate()
        {
            back = Q<Button>("back");
            back.clicked += () => Back();
            navTitle = Q<Label>("nav-title");
            guide = Q<VisualElement>("guide");
            leave = Q<VisualElement>("leave");
            confirm = Q<VisualElement>("confirm");
            sending = Q<VisualElement>("sending");
            result = Q<VisualElement>("result");
            photo = new Image { scaleMode = ScaleMode.ScaleAndCrop };
            photo.AddToClassList("verify-photo-img");
            Q<VisualElement>("photo").Add(photo);
            confirmError = Q<Label>("confirm-error");
            guideError = Q<Label>("guide-error");
            resultError = Q<Label>("result-error");
            resultIcon = Q<VisualElement>("result-icon");
            resultTitle = Q<Label>("result-title");
            resultLead = Q<Label>("result-lead");
            resultBody = Q<VisualElement>("result-body");
            chip = Q<Label>("result-chip");
            primary = Q<Button>("primary");
            secondary = Q<Button>("secondary");
            primary.clicked += () => OnButton(true);
            secondary.clicked += () => OnButton(false);
            Q<Button>("open-camera").clicked += OpenCamera;
            Q<Button>("send").clicked += Send;
            Q<Button>("retake").clicked += OpenCamera;
            Q<Button>("leave-save").clicked += SaveUnverified;
            Q<Button>("leave-stay").clicked += () => Back();
            Render();
        }

        protected internal override void OnShow()
        {
            ticker ??= Root.schedule.Execute(Tick).Every(500);
            ticker.Resume();
            Native.Permissions.Changed += OnPermissionChanged;
        }

        protected internal override void OnHide()
        {
            ticker?.Pause();
            Native.Permissions.Changed -= OnPermissionChanged;
        }

        protected internal override void OnDestroy()
        {
            OnHide();
            DropPhoto();
        }

        protected internal override bool OnBack()
        {
            Back();
            return true;
        }

        void Back()
        {
            if (busy) return;
            if (flow.Step == VerifyStep.Confirm) DropPhoto();
            flow.Back();
            Render();
        }

        void Tick()
        {
            var before = flow.Step;
            flow.Tick(DateTime.UtcNow);
            if (flow.Step != before)
            {
                DropPhoto();
                Render();
            }
            else if (chip.IsVisible()) UpdateChip(); // 남은 시간만 바뀜(화면 전체를 다시 그리지 않음)
        }

        // I-06에서 설정 앱으로 가서 카메라를 켜고 돌아오면 안내로(다시 "카메라 열기")
        void OnPermissionChanged(PermissionKind kind, PermissionStatus status)
        {
            if (kind == PermissionKind.Camera && status == PermissionStatus.Granted && flow.Step == VerifyStep.NoCamera)
            {
                flow.Back();
                Render();
            }
        }

        // ---------- 카메라(E-03, Swift) ----------
        async void OpenCamera()
        {
            if (busy) return;
            if (flow.Step == VerifyStep.Confirm) DropPhoto(); // E-04 "다시 촬영": 지금 사진은 버림
            if (!flow.OpenCamera(DateTime.UtcNow))
            {
                Render();
                return;
            }
            busy = true;
            Render();
            CameraShot got;
            try
            {
                got = await Native.Camera.ShootAsync(new CameraShootOptions());
            }
            catch (Exception e)
            {
                Debug.LogWarning("[인증] 카메라를 열지 못함: " + e.Message);
                got = new CameraShot { Outcome = CameraOutcome.Unavailable };
            }
            busy = false;
            if (Root.panel == null) // 그사이 화면을 떠남
            {
                Native.Camera.Discard(got);
                return;
            }
            if (got.Outcome == CameraOutcome.Taken && got.Photos.Count > 0) SetPhoto(got);
            else Native.Camera.Discard(got);
            flow.CameraDone(got.Outcome == CameraOutcome.Taken && got.Photos.Count == 0 ? CameraOutcome.Unavailable : got.Outcome);
            Render();
        }

        void SetPhoto(CameraShot s)
        {
            DropPhoto();
            shot = s;
            try
            {
                preview = new Texture2D(2, 2);
                preview.LoadImage(File.ReadAllBytes(s.Photos[0].Path)); // 자동 노출 장(브래킷 첫 장)
                photo.image = preview;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[인증] 미리보기를 못 읽음: " + e.Message);
            }
        }

        void DropPhoto()
        {
            if (shot != null) Native.Camera.Discard(shot); // 사진은 앱 임시 폴더에만, 끝나면 지움(BL-17 ③과 같은 방향)
            shot = null;
            photo.image = null;
            if (preview != null) UnityEngine.Object.Destroy(preview);
            preview = null;
        }

        // ---------- 서버 ----------
        async void Send()
        {
            if (busy || shot == null || !flow.Send()) return;
            busy = true;
            Render();
            try
            {
                var parts = await VerifyPhotos.PrepareAsync(shot, sensor, Application.isEditor ? FakeNative.Config.cameraOcr : (FakeOcrResult?)null);
                var r = await Api.Runs.VerifyPhotoAsync(runId, parts);
                DropPhoto();
                flow.Answer(r);
            }
            catch (ApiException e)
            {
                flow.Failed(ErrorOf(e));
                if (flow.Step == VerifyStep.TimeOver) DropPhoto();
                if (e.Failure == ApiFailure.NotLoggedIn) Feedback.Handle(e);
            }
            catch (Exception e) // 사진 파일을 못 읽음 등
            {
                Debug.LogWarning("[인증] 사진을 보내지 못함: " + e.Message);
                flow.Failed(VerifyError.Other);
            }
            finally
            {
                busy = false;
            }
            if (Root.panel != null) Render();
            if (flow.Step == VerifyStep.Done) Finish();
        }

        async void SaveUnverified()
        {
            if (busy) return;
            if (!flow.SaveUnverified())
            {
                if (flow.Step == VerifyStep.Done) Finish();
                return;
            }
            busy = true;
            Render();
            try
            {
                flow.Answer(await Api.Runs.SaveUnverifiedAsync(runId, flow.SavingReason));
            }
            catch (ApiException e)
            {
                flow.Failed(ErrorOf(e));
                if (e.Failure == ApiFailure.NotLoggedIn) Feedback.Handle(e);
                else Feedback.ShowError("미인증으로 저장하지 못했어요", Feedback.Describe(e).body, e.RequestId, SaveUnverified);
            }
            finally
            {
                busy = false;
            }
            if (Root.panel != null) Render();
            if (flow.Step == VerifyStep.Done) Finish();
        }

        static VerifyError ErrorOf(ApiException e)
        {
            if (e.Failure == ApiFailure.Network) return VerifyError.Network;
            switch (e.Code)
            {
                case "VERIFICATION_CLOSED": return VerifyError.Closed;
                case "SERVICE_UNAVAILABLE": return VerifyError.Busy;
                case "RUN_NOT_FOUND": return VerifyError.NotFound;
                default: return VerifyError.Other;
            }
        }

        void Finish()
        {
            if (finished || Nav.Current != this) return;
            finished = true;
            DropPhoto();
            onDone?.Invoke();
        }

        // 결과 화면 버튼(위 = primary, 아래 = secondary). 단계마다 뜻이 다름(Figma)
        void OnButton(bool top)
        {
            switch (flow.Step)
            {
                case VerifyStep.Verified:
                    flow.Finish();
                    Finish();
                    break;
                case VerifyStep.Unreadable:
                case VerifyStep.Mismatch:
                    if (top && flow.CanRetake) OpenCamera();   // E-07·08: 다시 촬영 / 미인증으로 저장
                    else SaveUnverified();
                    break;
                case VerifyStep.Unsupported:
                    if (!top && flow.CanRetake) OpenCamera();  // E-09: 미인증으로 저장 / 다시 촬영
                    else SaveUnverified();
                    break;
                case VerifyStep.TimeOver:
                    SaveUnverified();
                    break;
                case VerifyStep.NoCamera:
                    if (top) Native.Permissions.OpenSettings(); // I-06: 설정 열기 / 미인증으로 저장
                    else SaveUnverified();
                    break;
            }
        }

        // ---------- 그리기 ----------
        void Render()
        {
            var s = flow.Step;
            SetEnabled(!busy);
            // 카메라(Swift가 위를 덮음)·미인증 저장 중에는 직전 화면을 그대로 둠(버튼만 잠금)
            if (s == VerifyStep.Camera || s == VerifyStep.Saving || s == VerifyStep.Done) return;
            guide.SetVisible(s == VerifyStep.Guide);
            leave.SetVisible(s == VerifyStep.ConfirmLeave);
            confirm.SetVisible(s == VerifyStep.Confirm);
            sending.SetVisible(s == VerifyStep.Sending);
            bool isResult = s >= VerifyStep.Verified && s <= VerifyStep.NoCamera;
            result.SetVisible(isResult);
            back.SetVisible(s == VerifyStep.Guide || s == VerifyStep.ConfirmLeave || s == VerifyStep.Confirm || s == VerifyStep.NoCamera);
            navTitle.text = s switch
            {
                VerifyStep.Confirm => "촬영 확인",
                VerifyStep.Sending => "인증 중",
                VerifyStep.Verified => "인증 결과",
                VerifyStep.Mismatch => flow.CanRetake ? "재촬영 필요" : "인증 결과",
                VerifyStep.Unreadable => "재촬영 필요",
                VerifyStep.Unsupported => "인증 불가",
                VerifyStep.TimeOver => "시간 초과",
                VerifyStep.NoCamera => "카메라 권한 거부",
                _ => "계기판 인증",
            };
            // 카메라를 못 엶: 안내(E-02)나 다시 찍기 화면(E-07·08·09)으로 돌아와 문구를 보여 줌
            const string cameraFailed = "카메라를 열지 못했어요. 다시 눌러 주세요.";
            guideError.SetVisible(s == VerifyStep.Guide && flow.Error == VerifyError.Camera);
            guideError.text = cameraFailed;
            resultError.SetVisible(isResult && flow.Error == VerifyError.Camera);
            resultError.text = cameraFailed;
            confirmError.SetVisible(s == VerifyStep.Confirm && flow.Error != VerifyError.None);
            confirmError.text = flow.Error switch
            {
                VerifyError.Network => "인터넷 연결을 확인하고 다시 보내 주세요. 사진은 그대로 있어요.",
                VerifyError.Busy => "계기판을 읽는 곳이 바빠요. 잠시 뒤 다시 보내 주세요.",
                _ => "사진을 보내지 못했어요. 다시 보내 주세요.",
            };
            if (isResult) RenderResult(s);
        }

        void SetEnabled(bool on)
        {
            foreach (var b in Root.Query<Button>().ToList()) b.SetEnabled(on);
        }

        void RenderResult(VerifyStep s)
        {
            var r = flow.Result;
            resultBody.Clear();
            resultIcon.ClearClassList();
            resultIcon.AddToClassList("verify-icon");
            UpdateChip();
            secondary.SetVisible(true);
            switch (s)
            {
                case VerifyStep.Verified:
                    Head("verify-icon--ok", "인증 완료", "계기판과 센서 기록이 일치해요");
                    resultBody.Add(Stats(r?.Console));
                    Buttons("다음", null);
                    break;
                case VerifyStep.Unreadable:
                    Head("verify-icon--warn", "숫자를 읽지 못했어요", "다시 한번 촬영해 주세요");
                    resultBody.Add(Tips("화면 반사가 없는 각도로 찍어요", "계기판 전체가 프레임 안에 들어오게 해요", "손떨림 없이 가까이에서 찍어요"));
                    Buttons(flow.CanRetake ? $"다시 촬영 (남은 {r?.RetakesLeft ?? 0}번)" : "확인", flow.CanRetake ? "미인증으로 저장" : null);
                    break;
                case VerifyStep.Mismatch:
                    // 다시 찍을 수 있으면 아직 미인증이 아님(Figma E-08 제목은 다 쓴 뒤). 문구는 상준 확인
                    if (flow.CanRetake) Head("verify-icon--bad", "기록이 서로 달라요", "계기판을 다시 찍거나 미인증으로 저장할 수 있어요");
                    else Head("verify-icon--bad", "미인증 기록", "계기판과 센서 기록이 달라요");
                    resultBody.Add(Table(r));
                    resultBody.Add(Notice());
                    Buttons(flow.CanRetake ? $"다시 촬영 (남은 {r?.RetakesLeft ?? 0}번)" : "확인", flow.CanRetake ? "미인증으로 저장" : null);
                    break;
                case VerifyStep.Unsupported:
                    Head("verify-icon--warn", "이 계기판은\n자동으로 읽을 수 없어요", "아날로그 계기판이거나 숫자가 표시되지 않는 기종이에요");
                    resultBody.Add(Character());
                    Buttons(flow.CanRetake ? "미인증으로 저장" : "확인", flow.CanRetake ? $"다시 촬영 (남은 {r?.RetakesLeft ?? 0}번)" : null);
                    break;
                case VerifyStep.TimeOver:
                    Head("verify-icon--clock", "촬영 시간이 지났어요", "제한 시간 안에 촬영하지 못해 미인증 기록으로 저장돼요");
                    Buttons(flow.Saved ? "확인" : "미인증으로 저장", null);
                    break;
                case VerifyStep.NoCamera:
                    Head("verify-icon--camera", "카메라 권한이 필요해요", "계기판을 촬영해야 기록을 인증할 수 있어요 (갤러리 사진은 사용할 수 없어요)");
                    resultBody.Add(Steps("설정 앱을 열어요", "SCRUN을 선택해요", "카메라를 켜요"));
                    Buttons("설정 열기", "미인증으로 저장");
                    break;
            }
        }

        // 다시 찍기 화면(E-07·08·09)의 남은 시간 칩
        void UpdateChip()
        {
            var s = flow.Step;
            int? left = flow.SecondsLeft(DateTime.UtcNow);
            chip.SetVisible(flow.CanRetake && left.HasValue && (s == VerifyStep.Unreadable || s == VerifyStep.Mismatch || s == VerifyStep.Unsupported));
            if (left.HasValue) chip.text = $"남은 시간 {left.Value / 60}:{left.Value % 60:00}";
        }

        void Head(string iconClass, string title, string lead)
        {
            resultIcon.AddToClassList(iconClass);
            resultTitle.text = title;
            resultLead.text = lead;
        }

        void Buttons(string top, string bottom)
        {
            primary.text = top;
            secondary.SetVisible(bottom != null);
            if (bottom != null) secondary.text = bottom;
        }

        // E-06: 계기판 거리·시간 카드 두 개
        static VisualElement Stats(RunStatsBody console)
        {
            var row = new VisualElement();
            row.AddToClassList("verify-stats");
            row.Add(Stat("계기판 거리", console == null ? "-" : FormatKm(console.DistanceM)));
            var second = Stat("계기판 시간", console == null ? "-" : FormatTime(console.DurationS));
            second.AddToClassList("verify-stat--second");
            row.Add(second);
            return row;
        }

        static VisualElement Stat(string label, string value)
        {
            var card = new VisualElement();
            card.AddToClassList("verify-stat");
            card.Add(UiExtensions.Text(label, "verify-stat-label"));
            card.Add(UiExtensions.Text(value, "verify-stat-value"));
            return card;
        }

        // E-08: 항목·센서 추정·계기판·차이(거리·시간 두 줄. 종료 시각 줄은 뺌 — 계기판에 안 뜸, 11/17 결정)
        static VisualElement Table(VerificationResult r)
        {
            var t = new VisualElement();
            t.AddToClassList("verify-table");
            t.Add(Row("항목", "센서 추정", "계기판", "차이", "verify-row--head"));
            foreach (var c in r?.Comparison ?? new List<ComparisonRow>())
            {
                bool dist = c.Item == "distance";
                string sensorText = dist ? FormatKm((int)c.Sensor) : FormatTime((int)c.Sensor);
                string consoleText = c.Console == null ? "-" : dist ? FormatKm((int)c.Console) : FormatTime((int)c.Console);
                string diff = c.Console == null ? "-" : dist ? FormatKm(Math.Abs((int)c.Console - (int)c.Sensor))
                                                              : FormatDiffSec(Math.Abs((int)c.Console - (int)c.Sensor));
                t.Add(Row(dist ? "거리" : "시간", sensorText, consoleText, diff, c.Ok ? null : "verify-row--bad"));
            }
            return t;
        }

        static VisualElement Row(string a, string b, string c, string d, string cls)
        {
            var row = new VisualElement();
            row.AddToClassList("verify-row");
            if (cls != null) row.AddToClassList(cls);
            foreach (var s in new[] { a, b, c }) row.Add(UiExtensions.Text(s, "verify-cell"));
            var diff = UiExtensions.Text(d, "verify-cell");
            diff.AddToClassList("verify-cell--diff");
            row.Add(diff);
            return row;
        }

        static VisualElement Notice()
        {
            var box = new VisualElement();
            box.AddToClassList("verify-notice");
            box.Add(UiExtensions.Text("미인증으로 저장하면", "verify-notice-title"));
            box.Add(UiExtensions.Text("기록은 남지만 주간 목표와 인증 마크에는 반영되지 않아요.", "verify-notice-text"));
            return box;
        }

        static VisualElement Tips(params string[] lines)
        {
            var box = new VisualElement();
            box.AddToClassList("verify-tips");
            foreach (var l in lines)
            {
                var row = new VisualElement();
                row.AddToClassList("verify-tip");
                var check = new VisualElement();
                check.AddToClassList("verify-check");
                row.Add(check);
                row.Add(UiExtensions.Text(l, "verify-tip-text"));
                box.Add(row);
            }
            return box;
        }

        static VisualElement Steps(params string[] lines)
        {
            var box = new VisualElement();
            box.AddToClassList("steps");
            for (int i = 0; i < lines.Length; i++)
            {
                var row = new VisualElement();
                row.AddToClassList("step");
                if (i > 0) row.AddToClassList("step--next");
                row.Add(UiExtensions.Text((i + 1).ToString(), "step-num"));
                row.Add(UiExtensions.Text(lines[i], "step-text"));
                box.Add(row);
            }
            return box;
        }

        static VisualElement Character()
        {
            var box = new VisualElement();
            box.AddToClassList("verify-character-box");
            var img = new VisualElement();
            img.AddToClassList("verify-character");
            box.Add(img);
            return box;
        }

        // 달리기 HUD·결과와 같은 글자(10 m 아래 버림, 로케일과 무관하게 점)
        public static string FormatKm(int meters) => RunSession.DistanceText(meters) + "km";
        public static string FormatTime(int sec) => sec >= 3600 ? $"{sec / 3600}:{sec / 60 % 60:00}:{sec % 60:00}" : $"{sec / 60}:{sec % 60:00}";
        static string FormatDiffSec(int sec) => sec < 60 ? sec + "초" : FormatTime(sec);
    }
}
