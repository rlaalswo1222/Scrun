"""우주 루프 맵의 큰 파일을 저장소 밖에서 받아 Assets/Scrun/SpaceLoop에 채운다.

메쉬두 모델·텍스처(Kit, 약 103MB)와 구간 메시(Models, 약 40MB)는 용량 때문에 git에 올리지 않는다.
git에는 .meta·재질과 목록(spaceloop_manifest.txt: 경로, 크기, SHA-256)만 있고, 파일은 GitHub Release의 zip 하나로 받는다.
.meta가 그대로 있어야 장면·재질의 연결(GUID)이 안 깨지므로 Unity를 열기 전에 실행한다.

    python unity/tools/spaceloop_fetch.py                 # 없거나 다른 파일이 있으면 zip을 받아 채우기
    python unity/tools/spaceloop_fetch.py --zip <경로>     # 이미 받은 zip으로 채우기(인터넷 없이)
    python unity/tools/spaceloop_fetch.py --check         # 확인만(받지 않음)

관리용(파일을 바꾼 사람만):
    python unity/tools/spaceloop_fetch.py --manifest      # 지금 있는 파일로 목록 다시 만들기
    python unity/tools/spaceloop_fetch.py --pack <zip>    # 목록대로 zip 만들기 → Release에 새 이름으로 올리고 아래 ZIP_URL 바꾸기
"""
import hashlib
import shutil
import sys
import tempfile
import urllib.request
import zipfile
from pathlib import Path, PurePosixPath

ROOT = Path(__file__).resolve().parents[1] / "TestRun" / "Assets" / "Scrun" / "SpaceLoop"
MANIFEST = Path(__file__).with_name("spaceloop_manifest.txt")
ZIP_URL = "https://github.com/rlaalswo1222/Scrun/releases/download/spaceloop-assets-v1/spaceloop_assets_v1.zip"
UA = {"User-Agent": "ScrunDev/1.0"}
# .gitignore와 같은 규칙: 저장소 밖에서 받는 파일
PATTERNS = ("Kit/**/*.fbx", "Kit/**/*.png", "Kit/**/*.jpg", "Models/*.fbx", "Models/*.asset")


def sha256_of(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def read_manifest():
    rows = []
    for line in MANIFEST.read_text(encoding="utf-8").splitlines():
        if line and not line.startswith("#"):
            rel, size, digest = line.split("\t")
            rows.append((rel, int(size), digest))
    return rows


def safe_rel(rel):
    """목록의 경로가 SpaceLoop 폴더 밖을 가리키지 않는지(.., 절대 경로 금지)"""
    p = PurePosixPath(rel)
    return not p.is_absolute() and ".." not in p.parts and rel == p.as_posix()


def problems(rows):
    """없거나 크기·내용이 다른 파일 목록"""
    bad = []
    for rel, size, digest in rows:
        f = ROOT / rel
        if not f.is_file() or f.stat().st_size != size or sha256_of(f) != digest:
            bad.append(rel)
    return bad


def make_manifest():
    files = sorted({f for pat in PATTERNS for f in ROOT.glob(pat) if f.is_file()})
    rows = [f"{f.relative_to(ROOT).as_posix()}\t{f.stat().st_size}\t{sha256_of(f)}" for f in files]
    head = "# 우주 루프 큰 파일 목록: 경로(SpaceLoop 기준)\t크기\tSHA-256. spaceloop_fetch.py --manifest로 만듦\n"
    MANIFEST.write_text(head + "\n".join(rows) + "\n", encoding="utf-8")
    total = sum(f.stat().st_size for f in files)
    print(f"목록 {len(rows)}개, {total / 1e6:.1f}MB → {MANIFEST}")


def pack(out):
    rows = read_manifest()
    missing = problems(rows)
    if missing:
        print(f"목록과 다른 파일 {len(missing)}개라 zip을 만들지 않음(먼저 --manifest): {missing[:3]}")
        return 1
    with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for rel, _, _ in rows:
            z.write(ROOT / rel, rel)
    print(f"zip {len(rows)}개, {Path(out).stat().st_size / 1e6:.1f}MB → {out}  SHA-256 {sha256_of(out)}")
    return 0


def download(url, dest):
    req = urllib.request.Request(url, headers=UA)
    with urllib.request.urlopen(req, timeout=60) as r, open(dest, "wb") as f:
        total = int(r.headers.get("Content-Length") or 0)
        done = 0
        while True:
            chunk = r.read(1 << 20)
            if not chunk:
                break
            f.write(chunk)
            done += len(chunk)
            if total:
                print(f"\r받는 중 {done / 1e6:.0f}/{total / 1e6:.0f}MB", end="", flush=True)
    print()


def fill_from_zip(zpath, rows, todo):
    want = {rel: (size, digest) for rel, size, digest in rows if rel in todo}
    bad = 0
    with zipfile.ZipFile(zpath) as z:
        names = set(z.namelist())
        for rel, (size, digest) in want.items():
            if not safe_rel(rel):
                print("이상한 경로라 건너뜀", rel)
                bad += 1
                continue
            if rel not in names:
                print("zip에 없음", rel)
                bad += 1
                continue
            dest = ROOT / rel
            dest.parent.mkdir(parents=True, exist_ok=True)
            tmp = dest.with_name(dest.name + ".part")
            with z.open(rel) as src, open(tmp, "wb") as dst:
                shutil.copyfileobj(src, dst, 1 << 20)
            if tmp.stat().st_size != size or sha256_of(tmp) != digest:
                print("내용이 다름(zip 판이 목록과 안 맞음)", rel)
                tmp.unlink()
                bad += 1
                continue
            tmp.replace(dest)
    return bad


def main(argv):
    if "--manifest" in argv:
        make_manifest()
        return 0
    if "--pack" in argv:
        return pack(argv[argv.index("--pack") + 1])
    rows = read_manifest()
    todo = problems(rows)
    if not todo:
        print(f"끝, 모두 같은 내용({len(rows)}개)")
        return 0
    print(f"없거나 다른 파일 {len(todo)}개 / {len(rows)}개")
    if "--check" in argv:
        return 1
    if "--zip" in argv:
        bad = fill_from_zip(argv[argv.index("--zip") + 1], rows, set(todo))
    else:
        with tempfile.TemporaryDirectory() as d:
            zpath = Path(d) / "spaceloop_assets.zip"
            print("받기", ZIP_URL)
            download(ZIP_URL, zpath)
            bad = fill_from_zip(zpath, rows, set(todo))
    left = problems(rows)
    print("끝" + (f", 문제 {len(left)}개" if left or bad else f", 모두 같은 내용({len(rows)}개)"))
    return 1 if left or bad else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
