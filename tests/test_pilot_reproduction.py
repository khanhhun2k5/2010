"""Tái lập pilot: cùng dữ liệu, cùng thiết lập (purge 2 tuần như pilot) → sai khác < 1e-6."""

import numpy as np
import pandas as pd
import pytest

import pilot_reference as ref
from ctr.estimators import Learners, Representation, Settings, dml_estimates

PILOT_SETTINGS = Settings(n_folds=4, purge_weeks=2)


@pytest.fixture(scope="module")
def panel():
    rng = np.random.default_rng(42)
    n_weeks, n_assets, p = 120, 6, 40
    factors = rng.standard_normal((n_weeks * n_assets, 4))
    feats = factors @ rng.standard_normal((4, p)) + 0.5 * rng.standard_normal((n_weeks * n_assets, p))
    week = np.repeat(np.arange(n_weeks), n_assets)
    asset = np.tile(np.arange(n_assets), n_weeks)
    dummies = pd.get_dummies(asset).to_numpy(float)
    y0 = 0.02 * (factors[:, 0] + rng.standard_t(4, len(week)))
    e_true = 1 / (1 + np.exp(-(factors[:, 1] - 0.3)))
    D = rng.binomial(1, e_true)
    Y = np.where(D == 1, 0.5 * y0 + 0.0015, y0)
    return dict(Y=Y, D=D, week=week, feats=feats, dummies=dummies, e_true=e_true)


@pytest.mark.parametrize("mode,oracle", [("lowrank", False), ("raw", False), ("lowrank", True)])
@pytest.mark.parametrize("alpha", [0.90, 0.95])
def test_matches_pilot_estimate_v3(panel, mode, oracle, alpha):
    expected = ref.estimate_v3(
        panel["Y"], panel["D"], panel["week"], panel["feats"], panel["dummies"], panel["e_true"],
        mode=mode, rank=4, alpha=alpha, oracle_propensity=oracle, n_folds=4, purge_weeks=2,
    )
    (est,) = dml_estimates(
        panel["Y"], panel["D"], panel["week"], alpha,
        Z=panel["feats"], representation=Representation(mode, 4), learners=Learners(),
        settings=PILOT_SETTINGS, extra=panel["dummies"],
        propensity=panel["e_true"] if oracle else None, estimands=("d_es",),
    )
    assert abs(est.theta - expected) < 1e-6
