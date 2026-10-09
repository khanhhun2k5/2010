import numpy as np
import pytest

from ctr.noise import NOISE_TYPES, add_noise


@pytest.fixture(scope="module")
def X():
    rng = np.random.default_rng(10)
    return rng.standard_normal((200_000, 3)) * np.array([1.0, 2.0, 0.5])


@pytest.mark.parametrize("kind", ["gaussian", "laplace", "poisson"])
@pytest.mark.parametrize("nsr", [0.25, 1.0, 2.0])
def test_mean_zero_noise_has_right_mean_and_variance(X, kind, nsr):
    E = add_noise(X, kind, nsr, np.random.default_rng(1)) - X
    var_x = X.var(axis=0)
    np.testing.assert_allclose(E.mean(axis=0) / np.sqrt(var_x), 0, atol=0.01)
    np.testing.assert_allclose(E.var(axis=0) / (nsr * var_x), 1, rtol=0.03)


def test_biased_noise_has_nonzero_mean_and_right_variance(X):
    nsr = 1.0
    E = add_noise(X, "biased", nsr, np.random.default_rng(2)) - X
    var_x = X.var(axis=0)
    assert np.all(E.mean(axis=0) > 0.5 * np.sqrt(0.5 * nsr * var_x))
    np.testing.assert_allclose(E.var(axis=0) / (nsr * var_x), 1, rtol=0.05)


def test_clipped_noise_respects_column_quantiles(X):
    Z = add_noise(X, "clipped", 0.5, np.random.default_rng(3))
    assert np.all(Z.max(axis=0) < X.max(axis=0))
    assert np.mean(Z[:, 0] == Z[:, 0].max()) == pytest.approx(0.05, abs=0.002)


@pytest.mark.parametrize("kind", ["gaussian", "laplace", "poisson", "biased"])
def test_zero_nsr_is_identity(X, kind):
    np.testing.assert_array_equal(add_noise(X[:100], kind, 0.0, np.random.default_rng(0)), X[:100])


def test_same_seed_same_noise(X):
    for kind in NOISE_TYPES:
        a = add_noise(X[:500], kind, 1.0, np.random.default_rng(5))
        b = add_noise(X[:500], kind, 1.0, np.random.default_rng(5))
        np.testing.assert_array_equal(a, b)


def test_invalid_kind_rejected(X):
    with pytest.raises(ValueError):
        add_noise(X, "uniform", 1.0, np.random.default_rng(0))
