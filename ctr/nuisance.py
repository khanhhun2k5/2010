"""Mô hình nuisance cho DML, chuyển từ pilot ``causal_tail_risk_realdata_pilot.py``.

- ``PropensityModel``: hồi quy logistic (pilot: C = 0,5), cắt π về [clip, 1 - clip] (pilot: 0,03).
- ``LocationScaleCDF``: L | x ~ μ(x) + s(x)·e, μ và log s là hồi quy Ridge (pilot: α = 3),
  e lấy từ phân phối thực nghiệm của phần dư chuẩn hóa. Cho cả CDF có điều kiện
  μ(c; x) = P(L ≤ c | x) và phần vượt kỳ vọng m(x) = E[(L - q)_+ | x].

Đặc trưng đầu vào đã được chuẩn hóa ở bước biểu diễn (``estimators.Representation``), nên ở
đây không chuẩn hóa thêm, giống pilot.
"""

from __future__ import annotations

import numpy as np
from sklearn.linear_model import Lasso, LogisticRegression, Ridge

PROPENSITY_KINDS = ("logistic", "lasso", "constant")
OUTCOME_KINDS = ("ridge", "lasso", "none")


class PropensityModel:
    def __init__(self, kind: str = "logistic", clip: float = 0.03, C: float = 0.5, lasso_C: float = 0.1):
        if kind not in PROPENSITY_KINDS:
            raise ValueError(f"propensity không hợp lệ: {kind!r}")
        self.kind, self.clip, self.C, self.lasso_C = kind, clip, C, lasso_C

    def fit(self, F: np.ndarray, D: np.ndarray) -> "PropensityModel":
        if self.kind == "constant":
            self._share = float(np.mean(D))
        elif self.kind == "logistic":
            self._model = LogisticRegression(C=self.C, max_iter=3000).fit(F, D)
        else:
            self._model = LogisticRegression(C=self.lasso_C, l1_ratio=1.0, solver="liblinear", max_iter=3000, random_state=0).fit(F, D)
        return self

    def predict(self, F: np.ndarray) -> np.ndarray:
        if self.kind == "constant":
            p = np.full(len(F), self._share)
        else:
            p = self._model.predict_proba(F)[:, 1]
        return np.clip(p, self.clip, 1.0 - self.clip)


class LocationScaleCDF:
    """Mô hình vị trí–thang của pilot. ``kind="none"`` trả mọi đại lượng bằng 0 (IPW thuần)."""

    def __init__(self, kind: str = "ridge", ridge_alpha: float = 3.0, lasso_alpha: float = 0.01):
        if kind not in OUTCOME_KINDS:
            raise ValueError(f"mô hình kết quả không hợp lệ: {kind!r}")
        self.kind, self.ridge_alpha, self.lasso_alpha = kind, ridge_alpha, lasso_alpha

    @property
    def is_null(self) -> bool:
        return self.kind == "none"

    def _regressor(self):
        if self.kind == "lasso":
            return Lasso(alpha=self.lasso_alpha, max_iter=5000, random_state=0)
        return Ridge(alpha=self.ridge_alpha)

    def fit(self, X: np.ndarray, y: np.ndarray) -> "LocationScaleCDF":
        if self.is_null:
            return self
        self.mean_model = self._regressor().fit(X, y)
        resid = y - self.mean_model.predict(X)
        self.scale_model = self._regressor().fit(X, np.log(np.abs(resid) + 1e-3))
        s = np.clip(np.exp(self.scale_model.predict(X)), 1e-3, None)
        self.r_sorted = np.sort(resid / s)
        self.suffix_sum = np.cumsum(self.r_sorted[::-1])[::-1]
        return self

    def _mu_scale(self, X: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
        mu = self.mean_model.predict(X)
        s = np.clip(np.exp(self.scale_model.predict(X)), 1e-3, None)
        return mu, s

    def mean(self, X: np.ndarray) -> np.ndarray:
        if self.is_null:
            return np.zeros(len(X))
        return self.mean_model.predict(X)

    def cdf(self, X: np.ndarray, grid: np.ndarray) -> np.ndarray:
        """Ma trận n × G: P(L ≤ grid_g | x_i)."""
        if self.is_null:
            return np.zeros((len(X), len(grid)))
        mu, s = self._mu_scale(X)
        z = (grid[None, :] - mu[:, None]) / s[:, None]
        return np.searchsorted(self.r_sorted, z, side="right") / len(self.r_sorted)

    def expected_excess(self, X: np.ndarray, q: float) -> np.ndarray:
        """m(x_i) = E[(L - q)_+ | x_i] = s(x)·E[(e - z)_+], z = (q - μ(x))/s(x)."""
        if self.is_null:
            return np.zeros(len(X))
        mu, s = self._mu_scale(X)
        z = (q - mu) / s
        idx = np.searchsorted(self.r_sorted, z, side="right")
        n = len(self.r_sorted)
        count = n - idx
        sums = np.zeros(len(z))
        valid = idx < n
        sums[valid] = self.suffix_sum[idx[valid]]
        return s * (sums - z * count) / n
