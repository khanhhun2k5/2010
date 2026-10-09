"""Sai số chuẩn từ hàm ảnh hưởng φ_i: iid, gom cụm theo tuần, block bootstrap theo tuần."""

from __future__ import annotations

from dataclasses import dataclass

import numpy as np

Z_975 = 1.959963984540054


def iid_se(influence: np.ndarray) -> float:
    n = len(influence)
    return float(np.sqrt(np.sum(influence**2)) / n)


def cluster_totals(influence: np.ndarray, cluster: np.ndarray) -> np.ndarray:
    """Tổng φ trong mỗi cụm, theo thứ tự nhãn cụm tăng dần."""
    _, inv = np.unique(cluster, return_inverse=True)
    return np.bincount(inv, weights=influence)


def cluster_se(influence: np.ndarray, cluster: np.ndarray) -> float:
    """SE gom cụm: sqrt(Σ_g (Σ_{i∈g} φ_i)² · G/(G-1)) / n."""
    totals = cluster_totals(influence, cluster)
    G, n = len(totals), len(influence)
    return float(np.sqrt(np.sum(totals**2) * G / (G - 1)) / n)


def block_bootstrap_se(
    influence: np.ndarray, week: np.ndarray, rng: np.random.Generator, block_weeks: int = 12, n_boot: int = 199
) -> float:
    """Moving-block bootstrap trên chuỗi tổng φ theo tuần (khối ``block_weeks`` tuần)."""
    totals = cluster_totals(influence, week)
    T, n = len(totals), len(influence)
    b = min(block_weeks, T)
    n_blocks = int(np.ceil(T / b))
    starts = rng.integers(0, T - b + 1, size=(n_boot, n_blocks))
    idx = (starts[:, :, None] + np.arange(b)[None, None, :]).reshape(n_boot, -1)[:, :T]
    boot_means = totals[idx].sum(axis=1) / n
    return float(boot_means.std(ddof=1))


@dataclass(frozen=True)
class Inference:
    se_cluster: float
    se_iid: float
    se_boot: float

    def ci(self, theta: float, z: float = Z_975) -> tuple[float, float]:
        """KTC chuẩn với SE gom cụm (spec: H2 dùng SE gom cụm)."""
        return theta - z * self.se_cluster, theta + z * self.se_cluster


def infer(
    influence: np.ndarray,
    week: np.ndarray,
    rng: np.random.Generator | None = None,
    block_weeks: int = 12,
    n_boot: int = 199,
) -> Inference:
    if np.isnan(influence).any():
        return Inference(float("nan"), float("nan"), float("nan"))
    se_boot = (
        block_bootstrap_se(influence, week, rng, block_weeks, n_boot) if rng is not None and n_boot > 1 else float("nan")
    )
    return Inference(cluster_se(influence, week), iid_se(influence), se_boot)
