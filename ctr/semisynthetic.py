"""Thiết kế bán mô phỏng trên lợi suất thật, chuyển từ ``causal_tail_risk_realdata_pilot.py``.

Đường lợi suất thật giữ nguyên; can thiệp "giảm nửa vị thế" có hiệu ứng biết trước:

    L(0) = -lợi suất tuần kế tiếp,   L(1) = 0,5 · L(0) + hedge_cost,

nên ΔES_α thật = hedge_cost - 0,5 · ES_α(L(0)) (ES dương thuần nhất và bất biến tịnh tiến).
Gán can thiệp theo propensity phụ thuộc rủi ro (``assign_treatment``), rồi so sánh V3 low-rank,
raw, oracle propensity và unadjusted qua nhiều lần gán (spec: 200 seed, pilot: 50).

Khác pilot (theo spec): purge 12 tuần, trạng thái căng thẳng = mkt_vol12 vượt phân vị 80%,
SE/KTC gom cụm theo tuần, cổng so với cả unadjusted và đòi độ phủ ≥ 0,90.
"""

from __future__ import annotations

from dataclasses import dataclass

import numpy as np
import pandas as pd

from .estimators import Learners, Representation, Settings, dml_estimates
from .inference import infer
from .risk import expected_shortfall


@dataclass(frozen=True)
class PilotConfig:
    alpha: float = 0.90
    hedge_cost: float = 0.0015
    exposure_after_hedge: float = 0.5
    pca_rank: int = 4
    n_assignment_seeds: int = 200
    stress_quantile: float = 0.80
    stress_window: str = "expanding"  # "expanding": phân vị tính đến t-1 (spec); "full": cả mẫu
    settings: Settings = Settings()


def build_features(returns: pd.DataFrame) -> pd.DataFrame:
    """Đặc trưng thị trường theo tuần (pilot, mục 1)."""
    feat = pd.DataFrame(index=returns.index)
    assets = list(returns.columns)
    cols = {}
    for lag in range(5):
        for a in assets:
            cols[f"{a}_ret_l{lag}"] = returns[a].shift(lag)
    for w in (4, 8):
        for a in assets:
            cols[f"{a}_mean{w}"] = returns[a].rolling(w).mean()
    for w in (4, 8, 12):
        for a in assets:
            cols[f"{a}_vol{w}"] = returns[a].rolling(w).std()
    market = returns.mean(axis=1)
    cols["mkt_ret"] = market
    for lag in (1, 2, 3, 4):
        cols[f"mkt_l{lag}"] = market.shift(lag)
    for w in (4, 8, 12):
        cols[f"mkt_mean{w}"] = market.rolling(w).mean()
        cols[f"mkt_vol{w}"] = market.rolling(w).std()
    return pd.concat([feat, pd.DataFrame(cols, index=returns.index)], axis=1)


def build_panel(returns: pd.DataFrame, cfg: PilotConfig = PilotConfig()) -> tuple[pd.DataFrame, list[str]]:
    """Bảng (tuần, mã) với đặc trưng tại t và lợi suất t+1; trả về (base, FEATURE_COLS)."""
    feat = build_features(returns)
    assets = list(returns.columns)
    own_vol8 = returns.rolling(8).std()
    next_ret = returns.shift(-1)
    rows = []
    for t in range(len(returns) - 1):
        f = feat.iloc[t]
        if f.isna().any():
            continue
        for asset_id, asset in enumerate(assets):
            nr = next_ret.iloc[t][asset]
            if pd.isna(nr):
                continue
            row = {
                "date": returns.index[t],
                "asset": asset,
                "asset_id": asset_id,
                "ownret": returns.iloc[t][asset],
                "ownvol8": own_vol8.iloc[t][asset],
                "nextret": nr,
            }
            row.update(f.to_dict())
            rows.append(row)
    base = pd.DataFrame(rows).reset_index(drop=True)
    feature_cols = list(feat.columns) + ["ownret", "ownvol8"]
    base["Y0"] = -base["nextret"]
    base["Y1"] = cfg.exposure_after_hedge * base["Y0"] + cfg.hedge_cost
    base["S"] = stress_state(base, cfg)
    base["week"] = pd.factorize(base["date"], sort=True)[0]
    return base, feature_cols


def stress_state(base: pd.DataFrame, cfg: PilotConfig) -> np.ndarray:
    """S = 1 khi mkt_vol12 vượt phân vị ``stress_quantile``; bản ``expanding`` chỉ dùng các tuần trước t."""
    weekly = base.groupby("date")["mkt_vol12"].first().sort_index()
    if cfg.stress_window == "full":
        threshold = pd.Series(weekly.quantile(cfg.stress_quantile), index=weekly.index)
    else:
        threshold = weekly.expanding().quantile(cfg.stress_quantile).shift(1)
    s = (weekly > threshold).astype(np.int8)
    return base["date"].map(s).fillna(0).to_numpy(np.int8)


def true_delta_es(base: pd.DataFrame, cfg: PilotConfig) -> float:
    return expected_shortfall(base["Y1"].to_numpy(), cfg.alpha) - expected_shortfall(base["Y0"].to_numpy(), cfg.alpha)


def assign_treatment(base: pd.DataFrame, seed: int) -> pd.DataFrame:
    """Gán can thiệp theo rủi ro (pilot, nguyên công thức)."""
    rng = np.random.default_rng(seed)
    risk = (
        12 * base["mkt_vol8"].to_numpy()
        + 8 * np.maximum(-base["mkt_ret"].to_numpy(), 0.0)
        + 6 * base["ownvol8"].to_numpy()
    )
    e_true = 1.0 / (1.0 + np.exp(-(-2.0 + 2.0 * risk)))
    D = rng.binomial(1, e_true)
    df = base.copy()
    df["e_true"] = e_true
    df["D"] = D
    df["Y"] = np.where(D == 1, df["Y1"], df["Y0"])
    return df


PILOT_METHODS = {
    "V3 low-rank": ("lowrank", False),
    "Raw high-dimensional": ("raw", False),
    "Oracle propensity": ("lowrank", True),
    "Unadjusted": ("none", False),
}


def estimate_pilot(panel: pd.DataFrame, feature_cols: list[str], method: str, cfg: PilotConfig = PilotConfig()):
    """Một phương pháp của pilot trên một lần gán; trả về Estimate của ΔES_α (không điều kiện)."""
    mode, oracle = PILOT_METHODS[method]
    dummies = pd.get_dummies(panel["asset"], prefix="asset").to_numpy(float)
    learners = Learners("constant", "none") if mode == "none" else Learners()
    (est,) = dml_estimates(
        panel["Y"].to_numpy(),
        panel["D"].to_numpy(),
        panel["week"].to_numpy(),
        cfg.alpha,
        Z=panel[feature_cols].to_numpy(float),
        representation=Representation(mode, cfg.pca_rank),
        learners=learners,
        settings=cfg.settings,
        extra=None if mode == "none" else dummies,
        propensity=panel["e_true"].to_numpy() if oracle else None,
        estimands=("d_es",),
    )
    return est


def run_benchmark(base: pd.DataFrame, feature_cols: list[str], cfg: PilotConfig = PilotConfig()) -> pd.DataFrame:
    """Lặp ``n_assignment_seeds`` lần gán; mỗi dòng một (seed, phương pháp) kèm SE gom cụm và độ phủ."""
    truth = true_delta_es(base, cfg)
    records = []
    for seed in range(cfg.n_assignment_seeds):
        panel = assign_treatment(base, seed)
        for method in PILOT_METHODS:
            est = estimate_pilot(panel, feature_cols, method, cfg)
            inf = infer(est.influence, panel["week"].to_numpy())
            lo, hi = inf.ci(est.theta)
            records.append(
                {
                    "seed": seed,
                    "Method": method,
                    "Estimate": est.theta,
                    "Error": est.theta - truth,
                    "AbsError": abs(est.theta - truth),
                    "SE": inf.se_cluster,
                    "Covered": lo <= truth <= hi,
                    "TreatmentRate": panel["D"].mean(),
                }
            )
    return pd.DataFrame(records)
