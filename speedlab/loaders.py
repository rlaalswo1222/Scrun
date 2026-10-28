"""기록 파일 불러오기: phyphox 내보내기(zip/csv), 카메라 추출 결과, 구간 기록표."""
import io
import zipfile
from pathlib import Path

import pandas as pd

SEGMENT_COLUMNS = ["start_s", "end_s", "speed_kmh", "set"]


def _read_text(path):
    path = Path(path)
    if path.suffix.lower() == ".zip":
        with zipfile.ZipFile(path) as zf:
            names = [n for n in zf.namelist()
                     if n.lower().endswith(".csv") and not n.startswith("meta/")]
            if not names:
                raise ValueError(f"{path}: zip 안에 데이터 CSV가 없습니다")
            # phyphox는 센서 원본을 'Raw Data.csv'에 저장한다
            name = next((n for n in names if "raw" in n.lower()), names[0])
            return zf.read(name).decode("utf-8-sig")
    return path.read_text(encoding="utf-8-sig")


def _parse_csv(text):
    # phyphox 내보내기 옵션에 따라 구분자(, ; 탭)와 소수점(. ,)이 달라진다
    header, first_row = text.splitlines()[:2]
    sep = max([",", ";", "\t"], key=header.count)
    decimal = "," if sep != "," and "," in first_row else "."
    return pd.read_csv(io.StringIO(text), sep=sep, decimal=decimal)


def load_track(path):
    """센서/카메라 기록을 DataFrame(t, 값 열...)으로 반환한다.

    - 't' 열이 있으면 이 프로젝트 형식(카메라 추출 결과 등)으로 읽는다.
    - 없으면 phyphox 형식으로 보고 첫 열=시간(s), 다음 세 열=x, y, z로 읽는다.
      (phyphox 언어 설정에 따라 열 이름이 바뀌어도 위치로 읽으므로 상관없음)
    """
    df = _parse_csv(_read_text(path))
    if "t" not in df.columns:
        if df.shape[1] < 4:
            raise ValueError(f"{path}: 시간 + x, y, z 열이 필요합니다 (현재 {df.shape[1]}열)")
        df = df.iloc[:, :4].copy()
        df.columns = ["t", "x", "y", "z"]
    return df.dropna(subset=["t"]).sort_values("t").reset_index(drop=True)


def load_segments(path):
    """구간 기록표(start_s, end_s, speed_kmh, set[, true_cadence_spm])를 읽는다."""
    df = pd.read_csv(path, comment="#", skipinitialspace=True)
    missing = [c for c in SEGMENT_COLUMNS if c not in df.columns]
    if missing:
        raise ValueError(f"{path}: 필요한 열이 없습니다: {missing}")
    df["set"] = df["set"].astype(str).str.strip().str.lower()
    bad = df.loc[~df["set"].isin(["calib", "test"]), "set"].unique()
    if len(bad):
        raise ValueError(f"{path}: set 열은 calib 또는 test만 가능합니다 (잘못된 값: {list(bad)})")
    return df
