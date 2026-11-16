using System.Collections.Generic;
using UnityEngine;

namespace Scrun.App
{
    // 계기판 촬영 카메라(Figma E-03, 계획 0033, ADR-0011).
    // 아이폰은 E-03 화면을 Swift가 통째로 띄우고(미리보기·겹친 UI·노출 제어), 찍으면 닫고 사진 파일 경로를 돌려준다.
    // 에디터는 가짜(Fake Native 창에서 결과를 고름, 노출만 다른 예시 사진 3장).
    //   var shot = await Native.Camera.ShootAsync(new CameraShootOptions { TimeLimitSec = 45 });
    //   if (shot.Outcome == CameraOutcome.Taken) { 업로드(shot.Photos) ... } else if (TimedOut) → E-10, NoPermission → I-06
    //   Native.Camera.Discard(shot); // 업로드가 끝났거나 화면을 떠날 때. 사진은 이 기기에 남기지 않음(BL-17 ③과 같은 방향)
    public interface ICamera
    {
        // E-03을 띄우고 사용자가 찍거나 닫을 때까지 기다린다. 이미 떠 있으면 NativeException.
        Awaitable<CameraShot> ShootAsync(CameraShootOptions options = null);

        // 찍은 사진 파일을 지운다(없는 파일은 무시).
        void Discard(CameraShot shot);
    }

    public enum CameraOutcome
    {
        Taken,        // 찍음: Photos에 1장 이상
        Cancelled,    // 왼쪽 위 X
        TimedOut,     // 남은 시간이 0이 됨 → E-10
        NoPermission, // 카메라 권한 거부·제한 → I-06 (안 물어봤으면 Swift가 먼저 물어봄)
        Unavailable,  // 카메라가 없음(시뮬레이터)·다른 앱이 쓰는 중
    }

    // 셔터 한 번에 노출만 다르게 찍은 사진들(브래킷). 서버 OCR이 가장 잘 읽히는 장을 고른다(계획 0033 정할 것 2).
    public sealed class CameraShot
    {
        public CameraOutcome Outcome;
        public List<CameraPhoto> Photos = new List<CameraPhoto>();
    }

    public sealed class CameraPhoto
    {
        public string Path;        // JPEG, 앱 임시 폴더
        public float ExposureBias; // 자동 노출 기준 EV(0 = 자동, 음수 = 어둡게)
    }

    public sealed class CameraShootOptions
    {
        public int TimeLimitSec = 45; // 백로그 Q-02 미정, Figma 예시 0:45. 0이면 제한 없음(남은 시간 칸 숨김)
        // 화면 문구(상준이 고치기 쉽게 C#에 둠)
        public string GuideLabel = "계기판이 이 안에 들어오게";
        public string Hint = "숫자가 선명하게 보이도록 맞춰 주세요";
        // 노출(EV). 7세그먼트 LED가 하얗게 타지 않게 어두운 장을 함께 찍는다(console_ocr.md, 값은 첫 실기기 시험에서 정함)
        public float[] ExposureBiases = { 0f, -1.5f, -3f };
    }
}
