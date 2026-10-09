"""Tổng hợp kết quả Monte Carlo theo ô: chệch, RMSE, độ phủ, độ dài KTC, tỷ lệ SE, cổng kiểm định.

Định nghĩa theo spec mục 4. Đầu vào là bảng dài, mỗi dòng một (ô, ước lượng, đại lượng, seed).
Ước lượng không phụ thuộc k ghi k = -1; ước lượng không phụ thuộc nhiễu (oracle, unadjusted)
ghi noise = "-" và nsr = -1.
"""

from __future__ import annotations

import numpy as np
import pandas as pd

from .inference import Z_975
from .noise import ASSUMPTION_OK

CELL = ["noise", "nsr", "confounding", "k", "alpha"]
KEYS = CELL + ["estimator", "estimand"]


def summarize(results: pd.DataFrame) -> pd.DataFrame:
    def per_group(g: pd.DataFrame) -> pd.Series:
        theta, truth = g["theta"].to_numpy(), float(g["truth"].iloc[0])
        n = len(theta)
        bias = theta.mean() - truth
        sd = theta.std(ddof=1) if n > 1 else float("nan")
        mc_se_bias = sd / np.sqrt(n) if n > 1 else float("nan")
        cov = g["covered"].mean()
        denom = abs(truth) if truth != 0 else float("nan")
        return pd.Series(
            {
                "n_seeds": n,
                "truth": truth,
                "mean_theta": theta.mean(),
                "bias": bias,
                "rel_bias": abs(bias) / denom,
                "rel_bias_lo": max(abs(bias) - Z_975 * mc_se_bias, 0.0) / denom,
                "rmse": float(np.sqrt(np.mean((theta - truth) ** 2))),
                "mae": float(np.mean(np.abs(theta - truth))),
                "sd_theta": sd,
                "coverage": cov,
                "coverage_mcse": float(np.sqrt(cov * (1 - cov) / n)),
                "ci_length": float(np.median(2 * Z_975 * g["se_cluster"])),
                "se_ratio": g["se_cluster"].mean() / sd if n > 1 and sd > 0 else float("nan"),
                "se_ratio_iid": g["se_iid"].mean() / sd if n > 1 and sd > 0 else float("nan"),
                "se_ratio_boot": g["se_boot"].mean() / sd if n > 1 and sd > 0 else float("nan"),
            }
        )

    out = results.groupby(KEYS, sort=True).apply(per_group, include_groups=False).reset_index()
    out["assumption_ok"] = out["noise"].isin(ASSUMPTION_OK)
    return out


def validation_gate(summary: pd.DataFrame, min_coverage: float = 0.90, candidate: str = "v3_lowrank") -> pd.DataFrame:
    """Cổng kiểm định của pilot, mở rộng theo spec. Mỗi ô của ``candidate`` ĐẠT khi:

    - G0: |chệch| nhỏ hơn của raw **và** của unadjusted (pilot chỉ so với raw);
    - G1: MAE nhỏ hơn của raw **và** của unadjusted (RMSE vẫn được báo cáo);
    - G2: độ phủ ≥ ``min_coverage`` (spec thêm vào).
    """
    cols = ["bias", "mae", "rmse"]
    cand = summary[summary["estimator"] == candidate].copy()
    raw = summary[summary["estimator"] == "raw"][["noise", "nsr", "confounding", "alpha", "estimand"] + cols]
    unadj = summary[summary["estimator"] == "unadjusted"][["confounding", "alpha", "estimand"] + cols]
    gate = cand.merge(raw.rename(columns={c: f"{c}_raw" for c in cols}), on=["noise", "nsr", "confounding", "alpha", "estimand"], how="left")
    gate = gate.merge(unadj.rename(columns={c: f"{c}_unadjusted" for c in cols}), on=["confounding", "alpha", "estimand"], how="left")
    gate["G0"] = (gate["bias"].abs() < gate["bias_raw"].abs()) & (gate["bias"].abs() < gate["bias_unadjusted"].abs())
    gate["G1"] = (gate["mae"] < gate["mae_raw"]) & (gate["mae"] < gate["mae_unadjusted"])
    gate["G2"] = gate["coverage"] >= min_coverage
    gate["gate_pass"] = gate["G0"] & gate["G1"] & gate["G2"]
    return gate


def flagged_cells(summary: pd.DataFrame, min_coverage: float = 0.90, max_rel_bias: float = 0.10) -> pd.DataFrame:
    """Danh sách bắt buộc trong báo cáo: mọi ô có độ phủ < 0,90 hoặc chệch tương đối > 0,10."""
    m = (summary["coverage"] < min_coverage) | (summary["rel_bias"] > max_rel_bias)
    return summary[m].sort_values(KEYS).reset_index(drop=True)


def hypothesis_h1_h2(
    summary: pd.DataFrame,
    estimator: str = "v3_lowrank",
    estimand: str = "d_es",
    max_nsr: float = 1.0,
    rank: int | None = None,
    coverage_band: tuple[float, float] = (0.93, 0.97),
    max_rel_bias: float = 0.10,
) -> dict:
    """Kết luận sơ bộ H1, H2 trên phạm vi giả định thỏa (nhiễu thỏa giả định, NSR ≤ 1, k ≥ r).

    Trả về số ô đạt/không đạt/chưa kết luận. Ô "chưa kết luận" cần chạy lại với 1000 seed (spec mục 4).
    """
    s = summary[(summary["estimator"] == estimator) & (summary["estimand"] == estimand)]
    s = s[s["assumption_ok"] & (s["nsr"] <= max_nsr)]
    if rank is not None and estimator == "v3_lowrank":
        s = s[s["k"] >= rank]
    h1_fail = s[(s["rel_bias"] >= max_rel_bias) & (s["rel_bias_lo"] >= max_rel_bias)]
    h1_pass = s[s["rel_bias"] < max_rel_bias]
    lo, hi = coverage_band
    in_band = s["coverage"].between(lo, hi)
    clearly_out = (s["coverage"] + Z_975 * s["coverage_mcse"] < lo) | (s["coverage"] - Z_975 * s["coverage_mcse"] > hi)
    return {
        "cells": len(s),
        "H1_pass": len(h1_pass),
        "H1_fail": len(h1_fail),
        "H1_inconclusive": len(s) - len(h1_pass) - len(h1_fail),
        "H2_in_band": int(in_band.sum()),
        "H2_clearly_out": int(clearly_out.sum()),
        "H2_inconclusive": int((~in_band & ~clearly_out).sum()),
    }
