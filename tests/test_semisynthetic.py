import numpy as np
import pandas as pd
import pytest

from ctr.risk import expected_shortfall
from ctr.semisynthetic import PilotConfig, assign_treatment, build_panel, estimate_pilot, true_delta_es


@pytest.fixture(scope="module")
def returns():
    rng = np.random.default_rng(0)
    idx = pd.date_range("2018-01-05", periods=150, freq="W-FRI")
    common = 0.02 * rng.standard_t(4, len(idx))
    data = {f"A{j}": common + 0.02 * rng.standard_normal(len(idx)) for j in range(5)}
    return pd.DataFrame(data, index=idx)


def test_true_delta_es_formula(returns):
    cfg = PilotConfig()
    base, _ = build_panel(returns, cfg)
    es0 = expected_shortfall(base["Y0"].to_numpy(), cfg.alpha)
    assert true_delta_es(base, cfg) == pytest.approx(0.0015 - 0.5 * es0, abs=1e-12)


def test_panel_shapes_and_no_lookahead_in_stress(returns):
    cfg = PilotConfig()
    base, cols = build_panel(returns, cfg)
    assert len(cols) == 5 * (5 + 2 + 3) + 1 + 4 + 6 + 2
    assert not base[cols].isna().any().any()
    weekly = base.groupby("date")[["mkt_vol12", "S"]].first().sort_index()
    for t in range(5, len(weekly)):
        thr = weekly["mkt_vol12"].iloc[:t].quantile(cfg.stress_quantile)
        assert weekly["S"].iloc[t] == int(weekly["mkt_vol12"].iloc[t] > thr)


def test_assign_treatment_is_seeded(returns):
    base, _ = build_panel(returns)
    a, b = assign_treatment(base, 3), assign_treatment(base, 3)
    np.testing.assert_array_equal(a["D"], b["D"])
    assert 0 < a["D"].mean() < 1


def test_pilot_methods_run(returns):
    cfg = PilotConfig()
    base, cols = build_panel(returns, cfg)
    panel = assign_treatment(base, 1)
    for method in ("V3 low-rank", "Raw high-dimensional", "Oracle propensity", "Unadjusted"):
        est = estimate_pilot(panel, cols, method, cfg)
        assert np.isfinite(est.theta)
