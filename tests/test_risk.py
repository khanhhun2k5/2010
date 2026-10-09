import numpy as np
import pytest

from ctr.risk import expected_shortfall, monotone_cdf, quantile_from_cdf, value_at_risk, weighted_quantile


def test_es_equals_mean_of_top_tail():
    x = np.random.default_rng(0).standard_normal(1000)
    for alpha in (0.90, 0.95, 0.99):
        top = np.sort(x)[-int(round(1000 * (1 - alpha))):]
        assert expected_shortfall(x, alpha) == pytest.approx(top.mean(), abs=1e-12)


def test_es_is_rockafellar_uryasev_minimum():
    x = np.random.default_rng(1).standard_t(4, 2000)
    alpha = 0.95
    es = expected_shortfall(x, alpha)
    for c in np.linspace(-1, 4, 51):
        assert c + np.mean(np.maximum(x - c, 0)) / (1 - alpha) >= es - 1e-12


def test_es_positive_homogeneity_and_translation():
    x = np.random.default_rng(2).standard_normal(500)
    assert expected_shortfall(0.5 * x + 0.0015, 0.9) == pytest.approx(0.5 * expected_shortfall(x, 0.9) + 0.0015)


def test_weighted_quantile_with_equal_weights_matches_var():
    x = np.random.default_rng(3).standard_normal(401)
    assert weighted_quantile(x, np.ones_like(x), 0.9) == value_at_risk(x, 0.9)


def test_monotone_cdf_is_nondecreasing_and_bounded():
    raw = np.array([-0.1, 0.3, 0.2, 0.8, 0.7, 1.2])
    F = monotone_cdf(raw)
    assert np.all(np.diff(F) >= 0) and F.min() >= 0 and F.max() <= 1


def test_quantile_from_cdf_interpolates():
    grid = np.array([0.0, 1.0, 2.0])
    assert quantile_from_cdf(grid, np.array([0.2, 0.6, 1.0]), 0.8) == pytest.approx(1.5)
    assert quantile_from_cdf(grid, np.array([0.95, 0.97, 1.0]), 0.9) == 0.0
