import numpy as np
import pytest

from ctr.dgp import DGPConfig, population_truth, simulate


def test_simulate_is_reproducible():
    cfg = DGPConfig(n_weeks=50)
    a, b = simulate(cfg, np.random.default_rng(7)), simulate(cfg, np.random.default_rng(7))
    for field in ("X", "D", "L", "S", "propensity"):
        np.testing.assert_array_equal(getattr(a, field), getattr(b, field))


def test_shapes_and_covariate_scale():
    cfg = DGPConfig(n_units=30, n_weeks=400)
    d = simulate(cfg, np.random.default_rng(0))
    assert d.X.shape == (12_000, cfg.p)
    np.testing.assert_allclose(d.X.var(axis=0), 1.0, rtol=0.1)
    assert np.linalg.matrix_rank(d.X) == cfg.rank
    assert set(np.unique(d.S)) <= {0, 1}
    assert np.all((d.week[:-1] <= d.week[1:]))


def test_observed_loss_is_consistent_with_potential_outcomes():
    d = simulate(DGPConfig(n_weeks=30), np.random.default_rng(1))
    np.testing.assert_array_equal(d.L, np.where(d.D == 1, d.L1, d.L0))


def test_truth_signs_and_ate():
    cfg = DGPConfig()
    tr = population_truth(cfg, 0.95, n_samples=400_000)
    assert tr.d_es < 0
    assert abs(tr.d_es_stress) > abs(tr.d_es_normal)
    assert tr.h3_delta > 0
    assert tr.ate == pytest.approx(cfg.mean_shift, abs=0.01)


def test_truth_is_deterministic_and_stable_across_seeds():
    cfg = DGPConfig()
    a = population_truth(cfg, 0.9, n_samples=1_000_000, seed=0)
    b = population_truth(cfg, 0.9, n_samples=1_000_000, seed=1)
    assert a == population_truth(cfg, 0.9, n_samples=1_000_000, seed=0)
    assert a.d_es == pytest.approx(b.d_es, rel=0.02)


def test_confounding_moves_naive_contrast_away_from_truth():
    weak = simulate(DGPConfig(confounding=0.0, stress_propensity=0.0), np.random.default_rng(3))
    strong = simulate(DGPConfig(confounding=1.5), np.random.default_rng(3))
    gap = lambda d: d.L[d.D == 1].mean() - d.L[d.D == 0].mean()
    assert abs(gap(weak) - 0.05) < 0.05
    assert gap(strong) > gap(weak) + 0.2
