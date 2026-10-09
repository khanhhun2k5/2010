"""Chạy lưới Monte Carlo từ file YAML và ghi kết quả dạng bảng dài (parquet).

    python -m ctr.experiment configs/smoke.yaml
    python -m ctr.experiment configs/mc_full.yaml --workers 4

Tái lập: mọi số ngẫu nhiên lấy từ ``np.random.SeedSequence(base_seed, spawn_key=...)`` với
khóa chỉ phụ thuộc vào (seed lặp, loại nhiễu, NSR, ...), không phụ thuộc thứ tự chạy hay số
tiến trình. Dữ liệu gốc (X, D, L) dùng chung giữa các loại nhiễu và NSR của cùng một seed
(biến ngẫu nhiên chung), nên so sánh giữa các ô ít nhiễu Monte Carlo hơn.
"""

from __future__ import annotations

import argparse
import multiprocessing
import time
import zlib
from concurrent.futures import ProcessPoolExecutor
from dataclasses import dataclass, field
from pathlib import Path

import numpy as np
import pandas as pd
import yaml

from .dgp import DGPConfig, Truth, population_truth, simulate
from .estimators import ESTIMANDS, ESTIMATORS, NOISE_FREE, USES_K, Settings, run_estimator
from .inference import infer
from .noise import NOISE_TYPES, add_noise


@dataclass(frozen=True)
class ExperimentConfig:
    name: str = "experiment"
    base_seed: int = 20261009
    n_seeds: int = 200
    workers: int = 1
    output: str = "results/mc.parquet"
    truth_samples: int = 1_000_000
    dgp: DGPConfig = field(default_factory=DGPConfig)
    confounding_levels: dict = field(default_factory=lambda: {"yeu": 0.3, "vua": 0.6, "manh": 1.0})
    noise: tuple = NOISE_TYPES
    nsr: tuple = (0.0, 0.25, 0.5, 1.0, 2.0)
    confounding: tuple = ("yeu", "vua", "manh")
    k: tuple = (2, 4, 6, 8)
    alpha: tuple = (0.90, 0.95)
    estimators: tuple = ESTIMATORS
    estimands: tuple = ESTIMANDS
    settings: Settings = field(default_factory=Settings)
    block_weeks: int = 12
    n_boot: int = 199
    min_coverage: float = 0.90
    noise_options: dict = field(default_factory=dict)

    @classmethod
    def from_yaml(cls, path: str | Path) -> "ExperimentConfig":
        raw = yaml.safe_load(Path(path).read_text(encoding="utf-8")) or {}
        grid = raw.pop("grid", {}) or {}
        inference = raw.pop("inference", {}) or {}
        dgp = DGPConfig(**(raw.pop("dgp", {}) or {}))
        est = raw.pop("estimator_settings", {}) or {}
        if "grid_levels" in est:
            est["grid_levels"] = tuple(est["grid_levels"])
        kwargs = {**raw, **inference, "dgp": dgp, "settings": Settings(**est)}
        for key, value in grid.items():
            kwargs[key] = tuple(value)
        for key in ("estimators", "estimands"):
            if key in kwargs:
                kwargs[key] = tuple(kwargs[key])
        cfg = cls(**kwargs)
        cfg.validate()
        return cfg

    def validate(self) -> None:
        bad = [n for n in self.noise if n not in NOISE_TYPES]
        bad += [e for e in self.estimators if e not in ESTIMATORS]
        bad += [e for e in self.estimands if e not in ESTIMANDS]
        bad += [c for c in self.confounding if c not in self.confounding_levels]
        if bad:
            raise ValueError(f"giá trị không hợp lệ trong config: {bad}")

    def dgp_for(self, confounding: str) -> DGPConfig:
        return self.dgp.replace(confounding=float(self.confounding_levels[confounding]))


def _key(value) -> int:
    """Khóa seed ổn định (không âm) cho mọi giá trị: tên, NSR, α, k."""
    text = f"{value:g}" if isinstance(value, float) else str(value)
    return zlib.crc32(text.encode("utf-8"))


def _rng(cfg: ExperimentConfig, *key: int) -> np.random.Generator:
    return np.random.default_rng(np.random.SeedSequence(cfg.base_seed, spawn_key=tuple(int(k) for k in key)))



def run_replication(cfg: ExperimentConfig, confounding: str, rep: int, truths: dict[float, Truth]) -> list[dict]:
    """Một seed của một mức nhiễu loạn: mọi loại nhiễu × NSR × α × ước lượng × k."""
    dgp = cfg.dgp_for(confounding)
    data = simulate(dgp, _rng(cfg, rep, 0))
    rows: list[dict] = []

    def record(estimator, estimates, alpha, noise, nsr, k):
        truth = truths[alpha]
        for est in estimates:
            if est.estimand not in cfg.estimands:
                continue
            boot_rng = _rng(cfg, rep, 2, *map(_key, (confounding, noise, float(nsr), float(alpha), estimator, k, est.estimand)))
            inf = infer(est.influence, data.week, boot_rng, cfg.block_weeks, cfg.n_boot)
            lo, hi = inf.ci(est.theta)
            th0 = truth.value(est.estimand)
            rows.append(
                {
                    "noise": noise,
                    "nsr": float(nsr),
                    "confounding": confounding,
                    "k": int(k),
                    "alpha": float(alpha),
                    "estimator": estimator,
                    "estimand": est.estimand,
                    "rep": rep,
                    "theta": est.theta,
                    "truth": th0,
                    "se_cluster": inf.se_cluster,
                    "se_iid": inf.se_iid,
                    "se_boot": inf.se_boot,
                    "ci_lo": lo,
                    "ci_hi": hi,
                    "covered": bool(lo <= th0 <= hi),
                    "treated_share": float(data.D.mean()),
                }
            )

    for alpha in cfg.alpha:
        for estimator in cfg.estimators:
            if estimator in NOISE_FREE:
                ests = run_estimator(estimator, data, None, alpha, settings=cfg.settings, estimands=cfg.estimands)
                record(estimator, ests, alpha, "-", -1.0, -1)

    for noise in cfg.noise:
        for nsr in cfg.nsr:
            Z = add_noise(data.X, noise, nsr, _rng(cfg, rep, 1, _key(noise), _key(float(nsr))), **cfg.noise_options)
            for alpha in cfg.alpha:
                for estimator in cfg.estimators:
                    if estimator in NOISE_FREE:
                        continue
                    for k in cfg.k if estimator in USES_K else (-1,):
                        ests = run_estimator(
                            estimator, data, Z, alpha, k=None if k < 0 else k, settings=cfg.settings, estimands=cfg.estimands
                        )
                        record(estimator, ests, alpha, noise, nsr, k)
    return rows


def compute_truths(cfg: ExperimentConfig) -> dict[str, dict[float, Truth]]:
    return {
        c: {a: population_truth(cfg.dgp_for(c), a, cfg.truth_samples, cfg.base_seed) for a in cfg.alpha}
        for c in cfg.confounding
    }


def _task(args) -> list[dict]:
    cfg, confounding, rep, truths = args
    return run_replication(cfg, confounding, rep, truths)


def run_experiment(cfg: ExperimentConfig, workers: int | None = None, progress: bool = True) -> pd.DataFrame:
    truths = compute_truths(cfg)
    tasks = [(cfg, c, rep, truths[c]) for c in cfg.confounding for rep in range(cfg.n_seeds)]
    workers = workers or cfg.workers
    rows: list[dict] = []
    start = time.time()
    if workers <= 1:
        results = map(_task, tasks)
    else:
        pool = ProcessPoolExecutor(max_workers=workers, mp_context=multiprocessing.get_context("spawn"))
        results = pool.map(_task, tasks, chunksize=1)
    try:
        for i, r in enumerate(results, 1):
            rows.extend(r)
            if progress and (i % max(1, len(tasks) // 20) == 0 or i == len(tasks)):
                print(f"[{cfg.name}] {i}/{len(tasks)} replications, {time.time() - start:.0f}s", flush=True)
    finally:
        if workers > 1:
            pool.shutdown()
    df = pd.DataFrame(rows)
    return df.sort_values(["confounding", "rep", "noise", "nsr", "alpha", "estimator", "k", "estimand"], kind="stable").reset_index(drop=True)


def main(argv: list[str] | None = None) -> None:
    parser = argparse.ArgumentParser(description="Chạy lưới Monte Carlo cho ΔES_α")
    parser.add_argument("config", help="file YAML")
    parser.add_argument("--workers", type=int, default=None)
    parser.add_argument("--n-seeds", type=int, default=None, help="ghi đè n_seeds trong config")
    parser.add_argument("--output", default=None)
    args = parser.parse_args(argv)

    cfg = ExperimentConfig.from_yaml(args.config)
    if args.n_seeds is not None or args.output is not None:
        from dataclasses import replace

        cfg = replace(cfg, n_seeds=args.n_seeds or cfg.n_seeds, output=args.output or cfg.output)
    df = run_experiment(cfg, workers=args.workers)
    out = Path(cfg.output)
    out.parent.mkdir(parents=True, exist_ok=True)
    df.to_parquet(out, index=False)
    print(f"Đã ghi {len(df)} dòng vào {out}")


if __name__ == "__main__":
    main()
