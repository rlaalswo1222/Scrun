"""지도에서 만들어 둔 코스(run3d/data/courses/*.json)를 최신 코스 생성 코드로 다시 만든다.
코스 생성 규칙(표면, 땅 용도, 건물 종류 등)을 바꾼 뒤에 쓴다.

  python tools/rebuild_courses.py            # 전부
  python tools/rebuild_courses.py baf154648c # 하나만
"""
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
from coursegen.build import build_course  # noqa: E402

COURSES = Path(__file__).resolve().parent.parent / "run3d" / "data" / "courses"


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    ids = sys.argv[1:] or [p.name.split(".")[0] for p in COURSES.glob("*.meta.json")]
    for cid in ids:
        meta_path = COURSES / f"{cid}.meta.json"
        meta = json.loads(meta_path.read_text(encoding="utf-8"))
        # 예전 메타에는 요청 거리가 없으므로 실제 거리를 1km 단위로 반올림해 추정
        request = meta.get("request_m") or round(meta["length_m"] / 1000) * 1000
        mode = meta.get("mode", "loop" if meta["kind"] == "순환" else "out_and_back")
        print(f"[{cid}] {meta['name']} {request}m {mode}")
        course = build_course(meta["lat"], meta["lon"], request, mode, meta["name"],
                              progress=lambda m: print("   ", m))
        (COURSES / f"{cid}.json").write_text(json.dumps(course, ensure_ascii=False, separators=(",", ":")),
                                             encoding="utf-8")
        meta.update(length_m=course["length_m"], kind=course["kind"], request_m=request, mode=mode)
        meta_path.write_text(json.dumps(meta, ensure_ascii=False), encoding="utf-8")


if __name__ == "__main__":
    main()
