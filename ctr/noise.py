"""Nhiễu đo lường cho hiệp biến: Z = X + E.

Độ mạnh nhiễu NSR = Var(E_j) / Var(X_j) cho từng cột j (spec, mục 1).

Ba loại thỏa giả định (kỳ vọng 0, độc lập giữa các ô):
- ``gaussian``: E ~ N(0, NSR·Var(X_j)).
- ``laplace``: E ~ Laplace(0, b), b = sqrt(NSR·Var(X_j)/2).
- ``poisson``: làm tròn Poisson, Z = s·Poisson((X - m)/s) + m với m = min cột; E[Z|X] = X,
  Var(Z|X) = s·(X - m), chọn s để phương sai trung bình bằng NSR·Var(X_j). Rời rạc, phương sai
  thay đổi theo X.

Hai loại vi phạm giả định (chỉ để tìm giới hạn, spec mục 3):
- ``biased``: kỳ vọng ≠ 0, E_ij = c_j·u_i + Gauss, u_i ~ Exp(1) chung cho cả hàng
  (lệch dương, có cấu trúc hạng 1 nên PCR giữ lại như tín hiệu). ``bias_share`` là phần phương
  sai nhiễu dồn vào thành phần lệch.
- ``clipped``: nhiễu Gaussian rồi cắt trần/sàn theo phân vị ``clip_quantiles`` của từng cột.
  Kể cả khi NSR = 0 vẫn cắt.
"""

from __future__ import annotations

import numpy as np

NOISE_TYPES = ("gaussian", "poisson", "laplace", "biased", "clipped")
ASSUMPTION_OK = frozenset({"gaussian", "poisson", "laplace"})


def add_noise(
    X: np.ndarray,
    kind: str,
    nsr: float,
    rng: np.random.Generator,
    *,
    bias_share: float = 0.5,
    clip_quantiles: tuple[float, float] = (0.05, 0.95),
) -> np.ndarray:
    if kind not in NOISE_TYPES:
        raise ValueError(f"loại nhiễu không hợp lệ: {kind!r}; chọn một trong {NOISE_TYPES}")
    if nsr < 0:
        raise ValueError("NSR phải ≥ 0")
    X = np.asarray(X, dtype=float)
    noise_var = nsr * X.var(axis=0)

    if kind == "gaussian":
        return X + rng.standard_normal(X.shape) * np.sqrt(noise_var)
    if kind == "laplace":
        return X + rng.laplace(0.0, 1.0, X.shape) * np.sqrt(noise_var / 2.0)
    if kind == "poisson":
        return _poisson_rounding(X, noise_var, rng)
    if kind == "biased":
        shift_sd = np.sqrt(bias_share * noise_var)
        gauss_sd = np.sqrt((1.0 - bias_share) * noise_var)
        u = rng.exponential(1.0, size=(X.shape[0], 1))
        return X + u * shift_sd + rng.standard_normal(X.shape) * gauss_sd
    # clipped
    Z = X + rng.standard_normal(X.shape) * np.sqrt(noise_var)
    lo, hi = np.quantile(Z, clip_quantiles, axis=0)
    return np.clip(Z, lo, hi)


def _poisson_rounding(X: np.ndarray, noise_var: np.ndarray, rng: np.random.Generator) -> np.ndarray:
    Z = X.copy()
    floor = X.min(axis=0)
    shifted = X - floor
    mean_shift = shifted.mean(axis=0)
    for j in range(X.shape[1]):
        if noise_var[j] <= 0 or mean_shift[j] <= 0:
            continue
        step = noise_var[j] / mean_shift[j]
        Z[:, j] = step * rng.poisson(shifted[:, j] / step) + floor[j]
    return Z
