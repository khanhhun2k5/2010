"""Bản sao của ``estimate_v3`` trong pilot dữ liệu thật, giữ nguyên logic, chỉ đổi biến toàn cục
(FEATURE_COLS, asset_dummies, N_FOLDS, PURGE_WEEKS) thành tham số. Dùng làm chuẩn tái lập."""

import numpy as np
from sklearn.decomposition import PCA
from sklearn.linear_model import LogisticRegression, Ridge
from sklearn.preprocessing import StandardScaler


def week_folds(dates_col, n_folds=4, purge_weeks=2):
    dates = np.array(sorted(np.unique(dates_col)))
    edges = np.linspace(0, len(dates), n_folds + 1, dtype=int)
    for j in range(n_folds):
        va_dates = dates[edges[j]:edges[j + 1]]
        lo = max(0, edges[j] - purge_weeks)
        hi = min(len(dates), edges[j + 1] + purge_weeks)
        tr_dates = np.concatenate([dates[:lo], dates[hi:]])
        tr = np.where(np.isin(dates_col, tr_dates))[0]
        va = np.where(np.isin(dates_col, va_dates))[0]
        yield tr, va


class LocationScaleCDF:
    def __init__(self, ridge_alpha=3.0):
        self.ridge_alpha = ridge_alpha

    def fit(self, X, y):
        self.mean_model = Ridge(alpha=self.ridge_alpha).fit(X, y)
        mu = self.mean_model.predict(X)
        resid = y - mu
        self.scale_model = Ridge(alpha=self.ridge_alpha).fit(X, np.log(np.abs(resid) + 1e-3))
        s = np.clip(np.exp(self.scale_model.predict(X)), 1e-3, None)
        self.r_sorted = np.sort(resid / s)
        self.suffix_sum = np.cumsum(self.r_sorted[::-1])[::-1]
        return self

    def _mu_scale(self, X):
        mu = self.mean_model.predict(X)
        s = np.clip(np.exp(self.scale_model.predict(X)), 1e-3, None)
        return mu, s

    def cdf(self, X, grid):
        mu, s = self._mu_scale(X)
        z = (grid[None, :] - mu[:, None]) / s[:, None]
        return np.searchsorted(self.r_sorted, z, side="right") / len(self.r_sorted)

    def expected_excess(self, X, q):
        mu, s = self._mu_scale(X)
        z = (q - mu) / s
        idx = np.searchsorted(self.r_sorted, z, side="right")
        n = len(self.r_sorted)
        count = n - idx
        sums = np.zeros(len(z))
        valid = idx < n
        sums[valid] = self.suffix_sum[idx[valid]]
        return s * (sums - z * count) / n


def build_representation(features, dummies, tr, va, mode="lowrank", rank=4):
    scaler = StandardScaler().fit(features[tr])
    Xtr = scaler.transform(features[tr])
    Xva = scaler.transform(features[va])
    if mode == "lowrank":
        k = min(rank, Xtr.shape[1], Xtr.shape[0] - 1)
        pca = PCA(n_components=k, random_state=0).fit(Xtr)
        Xtr = pca.transform(Xtr)
        Xva = pca.transform(Xva)
    elif mode != "raw":
        raise ValueError(mode)
    Xtr = np.hstack([Xtr, dummies[tr]])
    Xva = np.hstack([Xva, dummies[va]])
    return Xtr, Xva


def monotone_cdf(F):
    return np.maximum.accumulate(np.clip(F, 0.0, 1.0))


def invert_cdf(grid, F, alpha):
    F = monotone_cdf(F)
    j = np.searchsorted(F, alpha, side="left")
    if j <= 0:
        return float(grid[0])
    if j >= len(grid):
        return float(grid[-1])
    y0, y1 = grid[j - 1], grid[j]
    f0, f1 = F[j - 1], F[j]
    if f1 <= f0 + 1e-12:
        return float(y1)
    return float(y0 + (alpha - f0) * (y1 - y0) / (f1 - f0))


def estimate_v3(Y, D, dates, features, dummies, e_true=None, mode="lowrank", rank=4, alpha=0.90,
                oracle_propensity=False, n_folds=4, purge_weeks=2):
    n = len(Y)
    grid = np.unique(np.quantile(Y, np.linspace(0.45, 0.999, 45)))
    cdf_score = {0: np.full((n, len(grid)), np.nan), 1: np.full((n, len(grid)), np.nan)}
    bundles = []
    for tr, va in week_folds(dates, n_folds=n_folds, purge_weeks=purge_weeks):
        Xtr, Xva = build_representation(features, dummies, tr, va, mode=mode, rank=rank)
        if oracle_propensity:
            e_va = np.clip(e_true[va], 0.03, 0.97)
        else:
            prop = LogisticRegression(C=0.5, max_iter=3000).fit(Xtr, D[tr])
            e_va = np.clip(prop.predict_proba(Xva)[:, 1], 0.03, 0.97)
        models = {}
        for a in [0, 1]:
            arm = D[tr] == a
            model = LocationScaleCDF(ridge_alpha=3.0).fit(Xtr[arm], Y[tr][arm])
            p_a = e_va if a == 1 else 1.0 - e_va
            I_a = (D[va] == a).astype(float)
            m_cdf = model.cdf(Xva, grid)
            I_y = (Y[va, None] <= grid[None, :]).astype(float)
            cdf_score[a][va] = m_cdf + (I_a / p_a)[:, None] * (I_y - m_cdf)
            models[a] = model
        bundles.append((va, Xva, e_va, models))
    qhat = {}
    for a in [0, 1]:
        qhat[a] = invert_cdf(grid, monotone_cdf(np.nanmean(cdf_score[a], axis=0)), alpha)
    es_score = {0: np.full(n, np.nan), 1: np.full(n, np.nan)}
    for va, Xva, e_va, models in bundles:
        for a in [0, 1]:
            p_a = e_va if a == 1 else 1.0 - e_va
            I_a = (D[va] == a).astype(float)
            q = qhat[a]
            h = np.maximum(Y[va] - q, 0.0)
            mplus = models[a].expected_excess(Xva, q)
            es_score[a][va] = q + (mplus + I_a / p_a * (h - mplus)) / (1.0 - alpha)
    contrast = es_score[1] - es_score[0]
    return float(np.nanmean(contrast))
