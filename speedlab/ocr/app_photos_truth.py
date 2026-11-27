"""앱이 보낸 인증 사진(개발 빌드, 계획 0085) → 채점용 truth.json, 그리고 서버처럼 묶음 단위 채점.

    python app_photos_truth.py data/app_photos            # truth.json(맞아요만)·todo.json
    python app_photos_truth.py data/app_photos --score    # + 묶음마다 서버 합치기 규칙(두 장 일치)으로 판정
    python bench_det.py data/app_photos --long 0 --configs 960   # 사진 한 장씩(앱이 이미 긴 변 2048로 보냄 → 키우지 않음)

꺼내는 명령은 README "앱이 보낸 인증 사진으로 채점". 보내기 한 번 = 폴더 하나(사진 2–3장 + meta.json).
확인 단계에서 "맞아요"(confirmed: true)를 누른 폴더만 정답으로 쓴다(그때 서버가 읽은 거리·시간 = 계기판 값).
"틀렸어요"·확인 전·실패 폴더는 todo.json에 모아 사람이 사진을 보고 적는다.
--score는 서버 코드(`console_ocr.pool.read_job`: 0 EV부터 읽고 같은 값 두 장이면 멈춤, `core.ocr.combine`: 읽힌 값이
모두 같을 때만 READ)를 그대로 써서 앱에서 일어난 판정을 재현한다. 사진은 git 밖(data/), 저장소에 안 올림(얼굴 비침).
"""
import argparse
import json
from pathlib import Path


def read_value(meta: dict):
    """확인 단계 직전 서버가 읽은 값(CONFIRM_REQUIRED의 console). 없으면 None."""
    for a in meta.get("answers", []):
        c = a.get("console")
        if a.get("status") == "CONFIRM_REQUIRED" and c:
            return c
    return None


def load(root: Path):
    """폴더마다 (상대 경로, meta, 사진 상대 경로 목록 — 보낸 순서)."""
    for meta_path in sorted(root.rglob("meta.json")):
        meta = json.loads(meta_path.read_text(encoding="utf-8"))
        folder = meta_path.parent.relative_to(root)
        yield folder, meta, [str(folder / p["file"]) for p in meta.get("photos", [])]


def write_truth(root: Path) -> dict:
    truth, todo = {}, {}
    for folder, meta, files in load(root):
        value = read_value(meta)
        if meta.get("confirmed") is True and value:
            for f in files:
                truth[f] = {"distance_m": value["distance_m"], "duration_s": value["duration_s"]}
        else:
            last = (meta.get("answers") or [{}])[-1]
            todo[str(folder)] = {"files": files, "confirmed": meta.get("confirmed"),
                                 "last": last.get("status") or last.get("error"), "read": value, "sensor": meta.get("sensor")}
    (root / "truth.json").write_text(json.dumps(truth, ensure_ascii=False, indent=1), encoding="utf-8")
    (root / "todo.json").write_text(json.dumps(todo, ensure_ascii=False, indent=1), encoding="utf-8")
    print(f"정답(맞아요) 사진 {len(truth)}장, 사람이 볼 폴더 {len(todo)}개 → {root / 'truth.json'}, {root / 'todo.json'}")
    if not truth:
        print("정답이 하나도 없음: 확인 단계에서 '맞아요'를 누른 인증이 없음 → todo.json을 보고 truth.json에 직접 적은 뒤 채점")
    return truth


def score(root: Path, truth: dict, onnx: str):
    """묶음마다 서버와 같은 순서·규칙으로 읽어 판정. 정답이 있는 묶음만 맞음·틀린 READ를 셈."""
    import _backend  # noqa: F401  서버 읽기 코드(backend/app)
    import cv2
    import numpy as np
    from app.console_ocr.pool import read_job
    from app.core.ocr import combine
    from field import ConsoleReader

    reader = ConsoleReader(onnx, threads=1)
    decode = lambda data: cv2.imdecode(np.frombuffer(data, np.uint8), cv2.IMREAD_COLOR)  # noqa: E731
    count = {"맞음": 0, "틀린 READ": 0, "못 읽음": 0, "정답 없음": 0}
    for folder, meta, files in load(root):
        job = [(root / f).read_bytes() for f in files]
        res = combine(read_job(job, decode, reader, log_line=lambda m: None))
        want = truth.get(files[0]) if files else None
        got = (res.distance_m, res.duration_s)
        if want is None:
            kind = "정답 없음"
        elif res.status.value != "READ":
            kind = "못 읽음"
        else:
            kind = "맞음" if got == (want["distance_m"], want["duration_s"]) else "틀린 READ"
        count[kind] += 1
        app = (meta.get("answers") or [{}])[0].get("status")
        print(f"{folder}: {res.status.value} {got if res.status.value == 'READ' else ''} → {kind} (앱 그때: {app})")
    print("묶음:", count)


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("folder", help="devicectl로 꺼낸 폴더(안에 */meta.json)")
    ap.add_argument("--score", action="store_true", help="서버 합치기 규칙으로 묶음 단위 채점")
    ap.add_argument("--onnx", default="../../backend/app/console_ocr/models/yolox_nano_7seg.onnx")
    a = ap.parse_args()
    root = Path(a.folder)
    truth = write_truth(root)
    if a.score:
        score(root, truth, a.onnx)


if __name__ == "__main__":
    main()
