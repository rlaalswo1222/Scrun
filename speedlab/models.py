"""속도 추정 모델과 평가. 속도 = 케이던스 × 보폭 에서 보폭을 어떻게 정하느냐의 비교."""
import numpy as np
import pandas as pd
from sklearn.linear_model import RidgeCV
from sklearn.pipeline import make_pipeline
from sklearn.preprocessing import StandardScaler


def step_length_m(speed_kmh, cadence_spm):
    return (speed_kmh / 3.6) / (cadence_spm / 60)


def speed_kmh(cadence_spm, step_m):
    return cadence_spm / 60 * step_m * 3.6


class HeightOnly:
    """보정 없이 키로만 보폭 추정.

    보폭 = 키 × 0.60 은 '키 170cm가 10km/h에서 약 1m 보폭'이 되도록 잡은 임시값.
    성별 보정 등은 문헌값을 찾은 뒤 반영할 것.
    """
    name, label = "height_only", "키만 사용"
    RATIO = 0.60

    def __init__(self, height_cm):
        self.step = height_cm / 100 * self.RATIO

    def fit(self, df):
        return self

    def predict(self, df):
        return speed_kmh(df.cadence_spm.to_numpy(), self.step)


class SinglePoint:
    """한 가지 속도로만 보정: 그 속도에서 잰 보폭을 모든 속도에 그대로 쓴다."""
    name, label = "single_point", "1개 속도로 보정"

    def __init__(self, ref_kmh=10.0):
        self.ref = ref_kmh

    def fit(self, df):
        row = df.loc[(df.speed_kmh - self.ref).abs().idxmin()]
        self.step = step_length_m(row.speed_kmh, row.cadence_spm)
        return self

    def predict(self, df):
        return speed_kmh(df.cadence_spm.to_numpy(), self.step)


class MultiCadence:
    """여러 속도로 보정: 보폭 = a + b × 케이던스 (빨라질수록 보폭이 늘어나는 효과 반영)."""
    name, label = "multi_cadence", "여러 속도로 보정"

    def fit(self, df):
        step = step_length_m(df.speed_kmh, df.cadence_spm)
        self.b, self.a = np.polyfit(df.cadence_spm, step, 1)
        return self

    def predict(self, df):
        cad = df.cadence_spm.to_numpy()
        return speed_kmh(cad, self.a + self.b * cad)


class MultiFeatures:
    """여러 속도로 보정 + 보조 지표: 케이던스·진폭·충격 → 속도 (릿지 회귀, 강도는 자동 선택)."""
    name, label = "multi_features", "여러 속도 + 보조지표"
    FEATURES = ["cadence_spm", "amp_rms", "impact_p95"]

    def fit(self, df):
        self.model = make_pipeline(StandardScaler(), RidgeCV(alphas=np.logspace(-3, 1, 9)))
        self.model.fit(df[self.FEATURES], df.speed_kmh)
        return self

    def predict(self, df):
        return self.model.predict(df[self.FEATURES])


def all_models(height_cm):
    return [HeightOnly(height_cm), SinglePoint(), MultiCadence(), MultiFeatures()]


def evaluate(feats, models):
    """test 구간이 있으면 calib로 보정 후 test로 평가.

    test가 없으면 calib 구간을 하나씩 빼고 나머지로 보정해 뺀 구간을 맞히는 방식(leave-one-out).
    반환: (평가 구간별 예측 DataFrame, 평가 방식 이름)
    """
    calib = feats[feats.set == "calib"]
    test = feats[feats.set == "test"]
    if len(calib) < 3:
        raise ValueError("보정(calib) 구간이 최소 3개 필요합니다")
    if len(test):
        preds = test.copy()
        for m in models:
            preds[m.name] = m.fit(calib).predict(test)
        return preds, "calib로 보정 -> test로 평가"
    preds = calib.copy()
    for m in models:
        preds[m.name] = [m.fit(calib.drop(index=i)).predict(calib.loc[[i]])[0]
                         for i in calib.index]
    return preds, "test 구간 없음 -> leave-one-out"


def summarize(preds, models):
    rows = []
    for m in models:
        err = (preds[m.name] - preds.speed_kmh) / preds.speed_kmh * 100
        rows.append({"model": m.name, "설명": m.label,
                     "MAPE_%": err.abs().mean(), "최대오차_%": err.abs().max(),
                     "편향_%": err.mean()})
    return pd.DataFrame(rows)
