import numpy as np
import pytest

from ctr.inference import block_bootstrap_se, cluster_se, iid_se, infer


def test_cluster_se_with_singleton_clusters_matches_iid():
    phi = np.random.default_rng(0).standard_normal(1000)
    phi -= phi.mean()
    G = len(phi)
    assert cluster_se(phi, np.arange(G)) == pytest.approx(iid_se(phi) * np.sqrt(G / (G - 1)))


def test_cluster_se_detects_within_week_correlation():
    rng = np.random.default_rng(1)
    week = np.repeat(np.arange(200), 30)
    phi = rng.standard_normal(200)[week] + rng.standard_normal(len(week))
    phi -= phi.mean()
    assert cluster_se(phi, week) > 3 * iid_se(phi)


def test_block_bootstrap_matches_cluster_se_on_average_for_independent_weeks():
    ratios = []
    for seed in range(20):
        rng = np.random.default_rng(seed)
        week = np.repeat(np.arange(260), 20)
        phi = rng.standard_normal(260)[week] + rng.standard_normal(len(week))
        phi -= phi.mean()
        b = block_bootstrap_se(phi, week, np.random.default_rng(100 + seed), block_weeks=12, n_boot=499)
        ratios.append(b / cluster_se(phi, week))
    assert np.mean(ratios) == pytest.approx(1.0, abs=0.1)


def test_infer_ci_is_symmetric():
    phi = np.random.default_rng(4).standard_normal(500)
    inf = infer(phi, np.repeat(np.arange(50), 10))
    lo, hi = inf.ci(1.0)
    assert (lo + hi) / 2 == pytest.approx(1.0)
    assert hi - lo == pytest.approx(2 * 1.959963984540054 * inf.se_cluster)
