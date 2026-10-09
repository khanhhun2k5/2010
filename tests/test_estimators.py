import numpy as np
import pytest

from ctr.dgp import DGPConfig, population_truth, simulate
from ctr.estimators import Settings, run_estimator, week_folds
from ctr.noise import add_noise
from ctr.risk import expected_shortfall


def test_week_folds_partition_and_purge():
    week = np.repeat(np.arange(100), 3)
    folds = week_folds(week, n_folds=4, purge_weeks=12)
    test_weeks = np.concatenate([np.unique(week[va]) for _, va in folds])
    np.testing.assert_array_equal(np.sort(test_weeks), np.arange(100))
    for tr, va in folds:
        wt, wv = np.unique(week[tr]), np.unique(week[va])
        gap = np.min(np.abs(wt[:, None] - wv[None, :]))
        assert gap > 12


def test_purge_too_large_is_rejected():
    with pytest.raises(ValueError):
        week_folds(np.arange(20), n_folds=2, purge_weeks=30)


@pytest.fixture(scope="module")
def big_sample():
    cfg = DGPConfig(n_weeks=1040)
    return cfg, simulate(cfg, np.random.default_rng(123))


def test_oracle_propensity_es_score_is_close_to_truth(big_sample):
    """Điểm ES đúng khi propensity là oracle: sai số nhỏ so với SE gom cụm."""
    cfg, data = big_sample
    truth = population_truth(cfg, 0.95)
    (est,) = run_estimator("oracle_propensity", data, data.X, 0.95, k=cfg.rank, estimands=("d_es",))
    from ctr.inference import cluster_se

    se = cluster_se(est.influence, data.week)
    assert abs(est.theta - truth.d_es) < 3 * se


def test_oracle_covariates_beat_unadjusted(big_sample):
    cfg, data = big_sample
    truth = population_truth(cfg, 0.95)
    (oracle,) = run_estimator("oracle", data, None, 0.95, estimands=("d_es",))
    (naive,) = run_estimator("unadjusted", data, None, 0.95, estimands=("d_es",))
    assert abs(oracle.theta - truth.d_es) < abs(naive.theta - truth.d_es)


def test_unadjusted_is_naive_group_contrast():
    data = simulate(DGPConfig(n_weeks=200), np.random.default_rng(4))
    (naive,) = run_estimator("unadjusted", data, None, 0.9, estimands=("d_es",))
    direct = expected_shortfall(data.L[data.D == 1], 0.9) - expected_shortfall(data.L[data.D == 0], 0.9)
    assert naive.theta == pytest.approx(direct, abs=0.02)
    assert naive.influence.mean() == pytest.approx(0.0, abs=1e-10)


def test_estimands_and_influence_shapes():
    data = simulate(DGPConfig(n_weeks=120), np.random.default_rng(5))
    Z = add_noise(data.X, "gaussian", 0.5, np.random.default_rng(6))
    ests = run_estimator("v3_lowrank", data, Z, 0.95, k=4)
    names = [e.estimand for e in ests]
    assert names == ["d_es", "d_es_normal", "d_es_stress", "h3_delta", "ate"]
    by = {e.estimand: e for e in ests}
    for e in ests:
        assert e.influence.shape == (data.n,)
        assert np.isfinite(e.theta)
    assert by["h3_delta"].theta == pytest.approx(abs(by["d_es_stress"].theta) - abs(by["d_es_normal"].theta))
    assert np.all(by["d_es_stress"].influence[data.S == 0] == 0)


def test_estimator_is_deterministic():
    data = simulate(DGPConfig(n_weeks=120), np.random.default_rng(8))
    Z = add_noise(data.X, "laplace", 1.0, np.random.default_rng(9))
    for name, k in (("v3_lowrank", 4), ("raw", None), ("lasso_dml", None)):
        a = run_estimator(name, data, Z, 0.9, k=k)
        b = run_estimator(name, data, Z, 0.9, k=k)
        assert [e.theta for e in a] == [e.theta for e in b]


def test_settings_purge_is_used():
    data = simulate(DGPConfig(n_weeks=120), np.random.default_rng(8))
    a = run_estimator("raw", data, data.X, 0.9, settings=Settings(purge_weeks=2), estimands=("d_es",))[0]
    b = run_estimator("raw", data, data.X, 0.9, settings=Settings(purge_weeks=12), estimands=("d_es",))[0]
    assert a.theta != b.theta
