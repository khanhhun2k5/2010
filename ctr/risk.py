"""Đo rủi ro đuôi: VaR, ES thực nghiệm và CDF có trọng số.

Quy ước: biến là *tổn thất* L (càng lớn càng xấu), ES_α là trung bình phần đuôi trên
của L ở mức α, theo công thức Rockafellar–Uryasev:

    ES_α(L) = min_c { c + E[(L - c)_+] / (1 - α) },

đạt cực tiểu tại c = VaR_α(L).
"""

from __future__ import annotations

import numpy as np


def value_at_risk(losses: np.ndarray, alpha: float) -> float:
    """VaR_α thực nghiệm: giá trị nhỏ nhất c có F̂(c) ≥ α (nghịch đảo CDF trái)."""
    return float(np.quantile(np.asarray(losses, dtype=float), alpha, method="inverted_cdf"))


def expected_shortfall(losses: np.ndarray, alpha: float) -> float:
    """ES_α thực nghiệm theo công thức Rockafellar–Uryasev tại c = VaR_α.

    Khi n(1-α) là số nguyên, kết quả bằng đúng trung bình của n(1-α) tổn thất lớn nhất.
    """
    losses = np.asarray(losses, dtype=float)
    q = value_at_risk(losses, alpha)
    return q + float(np.mean(np.maximum(losses - q, 0.0))) / (1.0 - alpha)


def weighted_quantile(values: np.ndarray, weights: np.ndarray, alpha: float) -> float:
    """Phân vị α của phân phối rời rạc có trọng số không âm (nghịch đảo CDF trái)."""
    values = np.asarray(values, dtype=float)
    weights = np.asarray(weights, dtype=float)
    order = np.argsort(values, kind="stable")
    cum = np.cumsum(weights[order])
    cum /= cum[-1]
    idx = int(np.searchsorted(cum, alpha, side="left"))
    return float(values[order][min(idx, len(values) - 1)])


def monotone_cdf(cdf_values: np.ndarray) -> np.ndarray:
    """Sắp xếp lại (rearrangement) để CDF ước lượng đơn điệu không giảm và nằm trong [0, 1]."""
    return np.clip(np.maximum.accumulate(np.asarray(cdf_values, dtype=float)), 0.0, 1.0)


def quantile_from_cdf(grid: np.ndarray, cdf_values: np.ndarray, alpha: float) -> float:
    """Phân vị α từ CDF đơn điệu trên lưới tăng dần, nội suy tuyến tính trong khoảng chứa α."""
    grid = np.asarray(grid, dtype=float)
    cdf_values = monotone_cdf(cdf_values)
    j = int(np.searchsorted(cdf_values, alpha, side="left"))
    if j <= 0:
        return float(grid[0])
    if j >= len(grid):
        return float(grid[-1])
    lo, hi = cdf_values[j - 1], cdf_values[j]
    if hi <= lo + 1e-12:
        return float(grid[j])
    w = (alpha - lo) / (hi - lo)
    return float(grid[j - 1] + w * (grid[j] - grid[j - 1]))
