from dataclasses import replace

import numpy as np
import pandas as pd
import pytest

from ctr.dgp import DGPConfig
from ctr.experiment import ExperimentConfig, run_experiment
from ctr.metrics import flagged_cells, hypothesis_h1_h2, summarize, validation_gate


@pytest.fixture(scope="module")
def tiny():
    return ExperimentConfig(
        name="tiny",
        n_seeds=3,
        truth_samples=200_000,
        dgp=DGPConfig(n_units=10, n_weeks=120),
        noise=("gaussian",),
        nsr=(0.0, 1.0),
        confounding=("vua",),
        k=(4,),
        alpha=(0.9,),
        estimators=("v3_lowrank", "raw", "unadjusted", "oracle"),
        estimands=("d_es", "ate"),
        n_boot=49,
    )


@pytest.fixture(scope="module")
def results(tiny):
    return run_experiment(tiny, workers=1, progress=False)


def test_rows_and_columns(results):
    # 2 nsr × (v3 + raw) × 2 estimands × 3 seeds + (unadjusted + oracle) × 2 × 3
    assert len(results) == 2 * 2 * 2 * 3 + 2 * 2 * 3
    for col in ("theta", "truth", "se_cluster", "se_iid", "se_boot", "covered"):
        assert col in results


def test_same_seed_same_results_across_worker_counts(tiny, results):
    again = run_experiment(tiny, workers=2, progress=False)
    pd.testing.assert_frame_equal(results, again)


def test_different_base_seed_changes_results(tiny, results):
    other = run_experiment(replace(tiny, base_seed=1), workers=1, progress=False)
    assert not np.allclose(other["theta"], results["theta"])


def test_summary_gate_and_flags(results):
    s = summarize(results)
    assert {"bias", "rel_bias", "rmse", "mae", "coverage", "ci_length", "se_ratio"} <= set(s.columns)
    g = validation_gate(s)
    assert {"G0", "G1", "G2", "gate_pass"} <= set(g.columns)
    assert len(g) == 2 * 2  # 2 NSR × 2 estimands for V3
    flagged_cells(s)
    h = hypothesis_h1_h2(s, rank=4)
    assert h["cells"] == 2


def test_yaml_config_loads(tmp_path):
    path = tmp_path / "c.yaml"
    path.write_text(
        "name: t\nn_seeds: 2\ndgp: {n_weeks: 80}\ngrid: {noise: [laplace], nsr: [0.5], k: [2]}\n"
        "estimator_settings: {purge_weeks: 6, grid_levels: [0.5, 0.999, 30]}\n",
        encoding="utf-8",
    )
    cfg = ExperimentConfig.from_yaml(path)
    assert cfg.dgp.n_weeks == 80 and cfg.noise == ("laplace",) and cfg.settings.purge_weeks == 6
    assert cfg.settings.grid_levels == (0.5, 0.999, 30)


def test_invalid_config_rejected(tmp_path):
    path = tmp_path / "bad.yaml"
    path.write_text("grid: {noise: [uniform]}\n", encoding="utf-8")
    with pytest.raises(ValueError):
        ExperimentConfig.from_yaml(path)
