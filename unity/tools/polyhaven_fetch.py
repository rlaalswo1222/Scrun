"""Poly Haven 에셋(CC0)을 저장소 밖에서 받아 Assets/Scrun/ThirdParty/PolyHaven에 채운다.

용량(약 230MB) 때문에 git에는 .meta와 목록(polyhaven_manifest.txt)만 올리고, 파일은 이 스크립트로 받는다.
.meta가 그대로 있어야 머티리얼·씬의 참조(GUID)가 안 깨지므로 Unity를 열기 전에 실행한다.

    python unity/tools/polyhaven_fetch.py            # 없는 파일만 받기
    python unity/tools/polyhaven_fetch.py --manifest  # 지금 있는 파일로 목록 다시 만들기
"""
import hashlib
import json
import sys
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / "TestRun" / "Assets" / "Scrun" / "ThirdParty" / "PolyHaven"
MANIFEST = Path(__file__).with_name("polyhaven_manifest.txt")
UA = {"User-Agent": "ScrunDev/1.0"}


def get(url):
    return urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=60).read()


def urls_of(asset):
    """API files/<asset>의 모든 다운로드 주소를 파일 이름 → 주소로 (fbx에 딸린 텍스처 포함)"""
    out = {}

    def walk(d):
        if isinstance(d, dict):
            if "url" in d and isinstance(d["url"], str):
                out.setdefault(d["url"].rsplit("/", 1)[1], d["url"])
            for v in d.values():
                walk(v)

    walk(json.loads(get("https://api.polyhaven.com/files/" + asset)))
    return out


def make_manifest():
    rows = []
    for f in sorted(ROOT.rglob("*")):
        if f.is_file() and f.suffix not in (".meta", ".md"):
            rows.append(f"{f.relative_to(ROOT).as_posix()}\t{f.stat().st_size}\t{hashlib.md5(f.read_bytes()).hexdigest()}")
    MANIFEST.write_text("\n".join(rows) + "\n", encoding="utf-8")
    print(f"목록 {len(rows)}개 → {MANIFEST}")


def fetch(root=ROOT):
    rows = [r.split("\t") for r in MANIFEST.read_text(encoding="utf-8").split("\n") if r]
    by_asset = {}
    for rel, size, md5 in rows:
        by_asset.setdefault(rel.split("/")[1], []).append((rel, int(size), md5))
    bad = 0
    for asset, files in by_asset.items():
        todo = [f for f in files if not (root / f[0]).exists()]
        if not todo:
            continue
        urls = urls_of(asset)
        for rel, size, md5 in todo:
            name = rel.rsplit("/", 1)[1]
            # 받을 때 이름을 바꾼 것: <에셋>_1k.fbx → <에셋>.fbx, _metal_ → _Metal_ (fbx에 딸린 텍스처 이름)
            low = {k.lower(): v for k, v in urls.items()}
            if name not in urls:
                alt = name[:-4] + "_1k.fbx" if name.endswith(".fbx") else name.lower()
                if alt.lower() in low:
                    urls[name] = low[alt.lower()]
            if name not in urls:
                print("주소 없음", rel)
                bad += 1
                continue
            data = get(urls[name])
            ok = hashlib.md5(data).hexdigest() == md5
            if not ok:
                print(f"내용이 다름(Poly Haven이 파일을 바꿨을 수 있음) {rel}")
                bad += 1
            p = root / rel
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_bytes(data)
        print(f"{asset}: {len(todo)}개 받음")
    print("끝" + (f", 문제 {bad}개" if bad else ", 모두 같은 내용"))
    return bad


if __name__ == "__main__":
    if "--manifest" in sys.argv:
        make_manifest()
    else:
        sys.exit(1 if fetch(Path(sys.argv[sys.argv.index("--to") + 1]) if "--to" in sys.argv else ROOT) else 0)
