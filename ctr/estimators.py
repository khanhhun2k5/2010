"""Ước lượng DML trực giao Neyman cho ΔES_α, chuyển từ ``estimate_v3`` của pilot dữ liệu thật.

Điểm ES cho nhánh d (spec, mục 2):

    ψ_d = q̂_d + [ m_d(X̂) + 1{D=d}/π_d(X̂) · ((L - q̂_d)_+ - m_d(X̂)) ] / (1 - α),

với q̂_d = VaR_α(L(d)) lấy từ CDF phản thực tế dạng AIPW
    F̂_d(c) = mean[ μ_d(c; X̂) + 1{D=d}/π_d(X̂) · (1{L ≤ c} - μ_d(c; X̂)) ].

Giống pilot: biểu diễn (chuẩn hóa, PCA hạng k) và mọi nuisance được fit trên phần huấn luyện
của mỗi fold tuần, có bỏ ``purge_weeks`` tuần hai bên. Khác pilot (theo spec):
- purge mặc định 12 tuần (pilot: 2);
- thêm ΔES_α theo trạng thái S (q̂ riêng cho từng trạng thái), H3 và ATE;
- trả về hàm ảnh hưởng φ_i để tính SE gom cụm theo tuần.
"""

from __future__ import annotations

from dataclasses import dataclass

import numpy as np
from sklearn.decomposition import PCA
from sklearn.preprocessing import StandardScaler

from .nuisance import LocationScaleCDF, PropensityModel
from .risk import quantile_from_cdf

ESTIMANDS = ("d_es", "d_es_normal", "d_es_stress", "h3_delta", "ate")
SCOPES = {"d_es": None, "d_es_normal": 0, "d_es_stress": 1}


@dataclass(frozen=True)
class Estimate:
    estimand: str
    theta: float
    influence: np.ndarray


@dataclass(frozen=True)
class Settings:
    """Siêu tham số của ước lượng; mặc định theo pilot, trừ ``purge_weeks`` theo spec."""

    n_folds: int = 4
    purge_weeks: int = 12
    propensity_clip: float = 0.03
    propensity_C: float = 0.5
    ridge_alpha: float = 3.0
    grid_levels: tuple[float, float, int] = (0.45, 0.999, 45)


@dataclass(frozen=True)
class Learners:
    propensity: str = "logistic"
    outcome: str = "ridge"

    def new_propensity(self, st: Settings) -> PropensityModel:
        return PropensityModel(self.propensity, clip=st.propensity_clip, C=st.propensity_C)

    def new_outcome(self, st: Settings) -> LocationScaleCDF:
        return LocationScaleCDF(self.outcome, ridge_alpha=st.ridge_alpha)


@dataclass(frozen=True)
class Representation:
    """Biểu diễn đặc trưng fit trên phần huấn luyện của fold (pilot: ``build_representation``).

    mode: ``lowrank`` (chuẩn hóa + PCA k thành phần), ``raw`` (chuẩn hóa), ``none`` (không đặc trưng).
    ``extra`` (ví dụ trạng thái S, dummy đơn vị) được nối thêm sau, không chuẩn hóa.
    """

    mode: str = "lowrank"
    rank: int = 4

    def build(self, Z: np.ndarray | None, extra: np.ndarray | None, tr: np.ndarray, va: np.ndarray):
        if self.mode == "none":
            Xtr, Xva = np.zeros((len(tr), 0)), np.zeros((len(va), 0))
        else:
            scaler = StandardScaler().fit(Z[tr])
            Xtr, Xva = scaler.transform(Z[tr]), scaler.transform(Z[va])
            if self.mode == "lowrank":
                k = min(self.rank, Xtr.shape[1], Xtr.shape[0] - 1)
                pca = PCA(n_components=k, random_state=0).fit(Xtr)
                Xtr, Xva = pca.transform(Xtr), pca.transform(Xva)
            elif self.mode != "raw":
                raise ValueError(f"biểu diễn không hợp lệ: {self.mode!r}")
        if extra is not None and extra.shape[1]:
            Xtr, Xva = np.hstack([Xtr, extra[tr]]), np.hstack([Xva, extra[va]])
        return Xtr, Xva


def week_folds(week: np.ndarray, n_folds: int = 4, purge_weeks: int = 12) -> list[tuple[np.ndarray, np.ndarray]]:
    """Fold theo khối tuần liên tiếp, bỏ ``purge_weeks`` tuần hai bên khối kiểm tra (pilot: ``week_folds``)."""
    dates = np.unique(week)
    edges = np.linspace(0, len(dates), n_folds + 1, dtype=int)
    folds = []
    for j in range(n_folds):
        va_dates = dates[edges[j] : edges[j + 1]]
        lo = max(0, edges[j] - purge_weeks)
        hi = min(len(dates), edges[j + 1] + purge_weeks)
        tr_dates = np.concatenate([dates[:lo], dates[hi:]])
        if len(tr_dates) == 0:
            raise ValueError("purge_weeks quá lớn so với số tuần: một fold không còn dữ liệu huấn luyện")
        folds.append((np.flatnonzero(np.isin(week, tr_dates)), np.flatnonzero(np.isin(week, va_dates))))
    return folds


def dml_estimates(
    L: np.ndarray,
    D: np.ndarray,
    week: np.ndarray,
    alpha: float,
    *,
    Z: np.ndarray | None,
    representation: Representation,
    learners: Learners = Learners(),
    settings: Settings = Settings(),
    S: np.ndarray | None = None,
    extra: np.ndarray | None = None,
    propensity: np.ndarray | None = None,
    estimands: tuple[str, ...] = ESTIMANDS,
) -> list[Estimate]:
    """θ̂ và φ cho các đại lượng trong ``estimands``.

    ``propensity`` (π thật) thay cho mô hình propensity (pilot: ``oracle_propensity=True``).
    ``S`` (0/1) cần cho các đại lượng theo trạng thái; không có S thì chỉ tính ``d_es`` và ``ate``.
    """
    n = len(L)
    st = settings
    lo_lvl, hi_lvl, n_lvl = st.grid_levels
    grid = np.unique(np.quantile(L, np.linspace(lo_lvl, hi_lvl, n_lvl)))
    below = (L[:, None] <= grid[None, :]).astype(float)
    hajek = learners.outcome == "none"
    if S is None:
        estimands = tuple(e for e in estimands if e in ("d_es", "ate"))

    pi1 = np.empty(n)
    cdf_terms = {d: np.empty((n, len(grid))) for d in (0, 1)}
    mu = {d: np.empty(n) for d in (0, 1)}
    bundles = []
    for tr, va in week_folds(week, st.n_folds, st.purge_weeks):
        Xtr, Xva = representation.build(Z, extra, tr, va)
        if propensity is not None:
            e_va = np.clip(propensity[va], st.propensity_clip, 1.0 - st.propensity_clip)
        else:
            e_va = learners.new_propensity(st).fit(Xtr, D[tr]).predict(Xva)
        pi1[va] = e_va
        models = {}
        for d in (0, 1):
            arm = D[tr] == d
            model = learners.new_outcome(st).fit(Xtr[arm], L[tr][arm])
            p_d = e_va if d == 1 else 1.0 - e_va
            I_d = (D[va] == d).astype(float)
            m_cdf = model.cdf(Xva, grid)
            cdf_terms[d][va] = m_cdf + (I_d / p_d)[:, None] * (below[va] - m_cdf)
            mu[d][va] = model.mean(Xva)
            models[d] = model
        bundles.append((va, Xva, models))

    pi = {1: pi1, 0: 1.0 - pi1}
    ipw = {d: (D == d) / pi[d] for d in (0, 1)}

    def es_scores(mask: np.ndarray) -> dict[int, np.ndarray]:
        """ψ_d trên phạm vi ``mask`` (0 ngoài phạm vi)."""
        out = {}
        for d in (0, 1):
            if hajek:
                w = np.where(mask, ipw[d], 0.0)
                w = w / w[mask].mean()
                q = quantile_from_cdf(grid, (w[:, None] * below)[mask].mean(axis=0), alpha)
                h = q + np.maximum(L - q, 0.0) / (1.0 - alpha)
                es_d = float(np.sum(w * h) / np.sum(w))
                score = es_d + w * (h - es_d)
            else:
                q = quantile_from_cdf(grid, cdf_terms[d][mask].mean(axis=0), alpha)
                mplus = np.empty(n)
                for va, Xva, models in bundles:
                    mplus[va] = models[d].expected_excess(Xva, q)
                score = q + (mplus + ipw[d] * (np.maximum(L - q, 0.0) - mplus)) / (1.0 - alpha)
            out[d] = np.where(mask, score, 0.0)
        return out

    results: dict[str, Estimate] = {}
    for estimand, state in SCOPES.items():
        if estimand not in estimands and not (estimand != "d_es" and "h3_delta" in estimands):
            continue
        mask = np.ones(n, dtype=bool) if state is None else (S == state)
        if not mask.any() or any(not (mask & (D == d)).any() for d in (0, 1)):
            results[estimand] = Estimate(estimand, float("nan"), np.full(n, np.nan))
            continue
        psi = es_scores(mask)
        diff = psi[1] - psi[0]
        share = mask.mean()
        theta = float(diff[mask].mean())
        results[estimand] = Estimate(estimand, theta, np.where(mask, (diff - theta) / share, 0.0))

    if "h3_delta" in estimands:
        s1, s0 = results["d_es_stress"], results["d_es_normal"]
        results["h3_delta"] = Estimate(
            "h3_delta",
            abs(s1.theta) - abs(s0.theta),
            np.sign(s1.theta) * s1.influence - np.sign(s0.theta) * s0.influence,
        )

    if "ate" in estimands:
        score = {}
        for d in (0, 1):
            if hajek:
                w = ipw[d] / ipw[d].mean()
                mean_d = float(np.sum(w * L) / np.sum(w))
                score[d] = mean_d + w * (L - mean_d)
            else:
                score[d] = mu[d] + ipw[d] * (L - mu[d])
        diff = score[1] - score[0]
        theta = float(diff.mean())
        results["ate"] = Estimate("ate", theta, diff - theta)

    return [results[e] for e in estimands]


# ---------------------------------------------------------------------------
# Các ước lượng so sánh trong mô phỏng (spec, mục 4)
# ---------------------------------------------------------------------------

ESTIMATORS = ("v3_lowrank", "raw", "unadjusted", "oracle", "oracle_propensity", "lasso_dml")
NOISE_FREE = frozenset({"unadjusted", "oracle"})  # không dùng Z
USES_K = frozenset({"v3_lowrank", "oracle_propensity"})


def run_estimator(
    estimator: str,
    data,
    Z: np.ndarray | None,
    alpha: float,
    k: int | None = None,
    settings: Settings = Settings(),
    estimands: tuple[str, ...] = ESTIMANDS,
) -> list[Estimate]:
    """Chạy một ước lượng trên ``SimData``; trạng thái S luôn được nối vào đặc trưng."""
    extra = data.S[:, None].astype(float)
    kwargs = dict(S=data.S, extra=extra, settings=settings, estimands=estimands)
    common = (data.L, data.D, data.week, alpha)
    if estimator == "unadjusted":
        return dml_estimates(*common, Z=None, representation=Representation("none"), learners=Learners("constant", "none"), **{**kwargs, "extra": None})
    if estimator == "oracle":
        return dml_estimates(*common, Z=data.X, representation=Representation("raw"), **kwargs)
    if Z is None:
        raise ValueError(f"{estimator} cần ma trận hiệp biến nhiễu Z")
    if estimator in ("v3_lowrank", "oracle_propensity"):
        if k is None:
            raise ValueError(f"{estimator} cần k")
        rep = Representation("lowrank", k)
        pi = data.propensity if estimator == "oracle_propensity" else None
        return dml_estimates(*common, Z=Z, representation=rep, propensity=pi, **kwargs)
    if estimator == "raw":
        return dml_estimates(*common, Z=Z, representation=Representation("raw"), **kwargs)
    if estimator == "lasso_dml":
        return dml_estimates(*common, Z=Z, representation=Representation("raw"), learners=Learners("lasso", "lasso"), **kwargs)
    raise ValueError(f"ước lượng không hợp lệ: {estimator!r}; chọn một trong {ESTIMATORS}")
