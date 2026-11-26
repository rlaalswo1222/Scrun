# 계획 0085: 개발 빌드에서 인증 사진 남기기 — 앱이 보낸 사진으로 OCR 채점 (BL-17 후속, 지환)

- 상태: **승인(11/27 지환: 정할 것 1–3 추천대로) · 구현 · PR #158**(브랜치 `feat/dev-verify-photos`, 리뷰 대기). 촬영 Swift는 안 바꿈 → 민재는 리뷰
- 담당: 지환(`VerifyScreen`과 새 클래스, 둘 다 지환 코드). 리뷰 민재
- 이어서: 계획 0084(7세그먼트 다시 학습 실패 — 다음은 앱 사진), 0033(촬영 Swift·노출), 0039(`RunEventLog` 개발 빌드 기록 선례), `docs/product/console_ocr.md`(11/27 실사진 첫 채점)

## 왜
- 11/27 실사진 첫 채점은 **폰 기본 카메라 사진**(자동 노출)으로 맞음 1/13. 지환: "앱 인증에선 잘 읽던데" → 남은 차이는 앱 촬영의 **노출**(가이드 가운데 기준, 0·−1.5·−3 EV 3장)과 3장 중 두 장 일치일 것(가설, 0084)
- 그런데 앱이 보낸 사진은 앱(`VerifyScreen.DropPhoto` → `Camera.Discard`)도 서버(BL-17 ③)도 지워서 **한 번도 채점해 본 적이 없음**. 찍은 사진을 나중에 어둡게 만들어 흉내 내는 것도 안 됨(0084: 이미 번진 숫자가 같이 어두워질 뿐)
- → 앱이 실제로 서버에 보낸 사진을 모아야 엔진을 고칠지(규칙·학습) 정할 수 있음

## 하는 것
1. **`VerifyPhotoLog`**(새, `RunEventLog`와 같은 방식): **개발 빌드 아이폰에서만**(`Debug.isDebugBuild && !Application.isEditor`). 인증 보내기 직전 서버로 가는 바이트 그대로(`VerifyPhotos.PrepareAsync` 결과)를 `Documents/verify-photos/<UTC 시각>_<기록 ID 앞 8자>/`에 보낸 이름 그대로(`console_0_ev0.0.jpg`…, 0 EV에 가까운 것부터), `meta.json`에 시각·기기 모델·장마다 노출(EV)·크기·해상도·센서 거리·시간
2. 서버 답이 오면 `meta.json`에 덧붙임: 결과(읽음·못 읽음·불일치 등), 읽은 거리·시간, 확인 단계 "맞아요/틀렸어요". **"맞아요"면 그 값이 정답**(라벨 작업이 거의 없음), "틀렸어요"는 사람이 사진을 보고 적음
3. **꺼내기**(맥): `xcrun devicectl device copy from --device <UDID> --domain-type appDataContainer --domain-identifier com.scrun.app --source Documents/verify-photos --destination speedlab/ocr/data/app_photos/` — `run-log`와 같은 길. 명령은 `speedlab/ocr/README.md`·`docs/unity/unity_map.md`에
4. **채점 잇기**: 꺼낸 폴더의 `meta.json` → `truth.json`(맞아요만 정답) 바꾸는 작은 스크립트, 그다음은 기존 `bench_det.py`·`eval_console.py`. 3장 묶음 그대로 서버 합치기 규칙(두 장 일치)까지 재현
5. 테스트(EditMode): 저장 폴더를 바꿔 끼워 사진·meta가 생기는지, 개발 빌드가 아니면 아무것도 안 쓰는지, 저장이 실패해도 인증은 계속되는지

## 지키는 것
- **출시 빌드는 그대로**: 사진을 남기지 않음(BL-17 ③). 개발 빌드 사진도 폰 앱 폴더에만
- 꺼낸 사진은 git 밖(`speedlab/ocr/data/`). 계기판 유리에 얼굴이 비칠 수 있어 저장소에 안 올림
- 저장 실패(공간 부족 등)는 경고 로그만, 인증 흐름은 막지 않음. 인증 1번에 사진 3장 약 2–3 MB(11/19 자른 사진 497–1051 KB)

## 실험 (다음 헬스장)
- 러닝머신 A에서 같은 판을 (1) 앱 인증 (2) 기본 카메라 같은 위치로 나란히 → 같은 엔진으로 채점해 노출 차이를 실제로 봄. 3명 각자 앱 인증 → 계획 0069 허용 오차 자료도 같이

## 정할 것
1. 남길 사진: **보낸 사진만(추천: 결과·정답과 짝지어짐)** / "다시 촬영"으로 버린 사진까지
2. 꺼내는 길: **devicectl(추천: `run-log` 선례, 빌드 설정 그대로)** / 파일 앱 공유도 켜기(개발 빌드 Info.plist `UIFileSharingEnabled` — 헬스장에서 바로 AirDrop, 빌드 후처리를 바꿈)
3. 지우기: **꺼낸 뒤 손으로(추천)** / 앱이 14일 지난 것 자동 삭제

## 진행
- 11/27 구현(`feat/dev-verify-photos`, develop 9037a3d에서): `Run/VerifyPhotoLog.cs`(개발 빌드 아이폰만, `Begin` → 서버로 가는 `FilePart` 바이트 그대로·`meta.json`, `Answer`·`Failed`·`Confirm`으로 덧붙임, 저장 실패는 경고만), `VerifyScreen`의 보내기·실패·맞아요/틀렸어요에 연결(출시 빌드는 늘 null). 촬영 Swift·서버는 안 바꿈
- `speedlab/ocr/app_photos_truth.py`: 꺼낸 폴더의 meta → `truth.json`("맞아요"인 묶음만, 확인 직전 `CONFIRM_REQUIRED`의 읽은 값) · `todo.json`. 가짜 폴더로 확인
- 테스트 EditMode `VerifyPhotoLogTests` 6개(보낸 바이트·meta, 답·확인, 보내기 실패, 출시 빌드·빈 사진은 안 씀, 같은 초 두 번, 저장 실패해도 예외 없음). EditMode 전체 **588 통과**(Unity 배치, `Scrun-s4`)
- 문서: `speedlab/ocr/README.md`(꺼내기·채점 명령), `docs/unity/unity_map.md` 계기판 인증 줄
- 11/27 `/code-review`(Standards·Spec) 반영: 보내기를 시작할 때 이전 기록을 비움(이전 폴더에 실패가 붙던 길), 사진 읽기 실패·확인 보내기 실패도 meta에 남김, 장마다 EV를 meta에(`SendOrder`와 같은 순서), 시각 문자열 함수 하나로·`StatsJson` 이름, 꺼내는 명령은 README 한 곳에만. `app_photos_truth.py`: argparse·UTF-8(팀 Windows)·정답이 없을 때 안내, **`--score`로 묶음 단위 채점**(서버 `read_job`·`combine`을 그대로 가져다 씀 — 계획의 "두 장 일치까지 재현"). 빌드가 바꾼 `ProjectSettings.asset`·폰트 에셋은 커밋에서 뺌
- 남은 것: 아이폰 개발 빌드로 실제 인증 한 번 → `devicectl`로 꺼내 사진·meta 확인(다음 헬스장 전에)
