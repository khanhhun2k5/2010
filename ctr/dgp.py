"""Quá trình sinh dữ liệu (DGP) có kiểm soát cho ΔES_α.

Mỗi quan sát (i, t) là đơn vị i ở tuần t. Cấu trúc:

- Hiệp biến thật hạng thấp: X_it = B a_it, với a_it ~ N(0, I_r), B (p×r) có các hàng
  chuẩn hóa nên mỗi cột của X có phương sai 1.
- Trạng thái S_t ∈ {0 bình thường, 1 căng thẳng}: xích Markov hai trạng thái theo tuần,
  phân phối dừng P(S=1) = ``stress_prob``. S quan sát được (spec: xác định trước t).
- Can thiệp: logit π(X, S) = ``confounding`` · g(X) + ``stress_propensity`` · S + ``propensity_intercept``,
  với g(X) = a·w_g là chỉ số gây nhiễu (độ lệch chuẩn 1). ``confounding`` là "độ mạnh nhiễu loạn".
- Tổn thất tiềm năng:

      L(d) = outcome_loading · o(X) + η_t + mean_shift · d
             + σ_S · exp(-d · (effect + heterogeneity · tanh(h(X)))) · ε,

  với o(X) tương quan với g(X) (gây nhiễu thật sự), η_t là cú sốc chung của tuần
  (tạo cấu trúc cụm), ε ~ Student-t chuẩn hóa phương sai 1, σ_S = 1 hoặc ``stress_scale``.
  Can thiệp thu nhỏ thang đuôi nên ΔES_α < 0, và |ΔES_α| lớn hơn khi căng thẳng.

Giá trị thật θ_0 tính bằng ``population_truth`` từ 10^6 mẫu tiềm năng không nhiễu.
"""

from __future__ import annotations

from dataclasses import asdict, dataclass, field
from functools import lru_cache

import numpy as np

from .risk import expected_shortfall


@dataclass(frozen=True)
class DGPConfig:
    n_units: int = 30
    n_weeks: int = 260
    p: int = 20
    rank: int = 4
    confounding: float = 0.6
    heterogeneity: float = 0.2
    effect: float = 0.3
    mean_shift: float = 0.05
    outcome_loading: float = 0.5
    outcome_confounder_corr: float = 0.7
    propensity_intercept: float = -0.2
    stress_prob: float = 0.2
    stress_persistence: float = 0.8
    stress_scale: float = 2.0
    stress_propensity: float = 0.5
    week_shock_sd: float = 0.3
    t_df: float = 5.0
    structure_seed: int = 2010

    def replace(self, **changes) -> "DGPConfig":
        return DGPConfig(**{**asdict(self), **changes})


@dataclass(frozen=True)
class Structure:
    """Phần cố định của DGP (không đổi giữa các seed): ma trận tải và các hướng chỉ số."""

    loadings: np.ndarray  # B, p × r
    w_confounder: np.ndarray  # w_g, r
    w_outcome: np.ndarray  # w_o, r
    w_hetero: np.ndarray  # w_h, r


@dataclass
class SimData:
    """Một bộ dữ liệu mô phỏng. Các mảng dài n = n_units · n_weeks, theo thứ tự tuần rồi đơn vị."""

    X: np.ndarray
    D: np.ndarray
    L: np.ndarray
    S: np.ndarray
    week: np.ndarray
    unit: np.ndarray
    propensity: np.ndarray
    L0: np.ndarray
    L1: np.ndarray
    latent: np.ndarray = field(repr=False)

    @property
    def n(self) -> int:
        return len(self.L)


def _unit(v: np.ndarray) -> np.ndarray:
    return v / np.linalg.norm(v)


@lru_cache(maxsize=32)
def make_structure(p: int, rank: int, outcome_confounder_corr: float, structure_seed: int) -> Structure:
    rng = np.random.default_rng(structure_seed)
    B = rng.standard_normal((p, rank))
    B /= np.linalg.norm(B, axis=1, keepdims=True)
    w_g = _unit(rng.standard_normal(rank))
    orth = rng.standard_normal(rank)
    orth = _unit(orth - orth @ w_g * w_g)
    rho = outcome_confounder_corr
    w_o = rho * w_g + np.sqrt(1.0 - rho**2) * orth
    w_h = _unit(rng.standard_normal(rank))
    return Structure(B, w_g, w_o, w_h)


def structure_of(cfg: DGPConfig) -> Structure:
    return make_structure(cfg.p, cfg.rank, cfg.outcome_confounder_corr, cfg.structure_seed)


def _student_t_unit_var(rng: np.random.Generator, df: float, size) -> np.ndarray:
    return rng.standard_t(df, size=size) * np.sqrt((df - 2.0) / df)


def _sigmoid(x: np.ndarray) -> np.ndarray:
    return 1.0 / (1.0 + np.exp(-x))


def propensity_of(cfg: DGPConfig, latent: np.ndarray, S: np.ndarray) -> np.ndarray:
    st = structure_of(cfg)
    logit = cfg.confounding * (latent @ st.w_confounder) + cfg.stress_propensity * S + cfg.propensity_intercept
    return _sigmoid(logit)


def potential_losses(
    cfg: DGPConfig, latent: np.ndarray, S: np.ndarray, week_shock: np.ndarray, eps: np.ndarray
) -> tuple[np.ndarray, np.ndarray]:
    """L(0), L(1) với cùng ε (biến ngẫu nhiên chung)."""
    st = structure_of(cfg)
    location = cfg.outcome_loading * (latent @ st.w_outcome) + week_shock
    sigma = np.where(S == 1, cfg.stress_scale, 1.0)
    shrink = np.exp(-(cfg.effect + cfg.heterogeneity * np.tanh(latent @ st.w_hetero)))
    L0 = location + sigma * eps
    L1 = location + cfg.mean_shift + sigma * shrink * eps
    return L0, L1


def simulate_stress_path(cfg: DGPConfig, rng: np.random.Generator) -> np.ndarray:
    p, rho = cfg.stress_prob, cfg.stress_persistence
    stay_stress = p + rho * (1.0 - p)
    enter_stress = p * (1.0 - rho)
    u = rng.random(cfg.n_weeks)
    S = np.empty(cfg.n_weeks, dtype=np.int8)
    S[0] = u[0] < p
    for t in range(1, cfg.n_weeks):
        S[t] = u[t] < (stay_stress if S[t - 1] else enter_stress)
    return S


def simulate(cfg: DGPConfig, rng: np.random.Generator) -> SimData:
    """Sinh một bộ dữ liệu (n_units × n_weeks quan sát) không nhiễu đo lường."""
    st = structure_of(cfg)
    T, N = cfg.n_weeks, cfg.n_units
    S_week = simulate_stress_path(cfg, rng)
    shock_week = rng.normal(0.0, cfg.week_shock_sd, size=T)
    latent = rng.standard_normal((T * N, cfg.rank))
    eps = _student_t_unit_var(rng, cfg.t_df, T * N)
    week = np.repeat(np.arange(T), N)
    unit = np.tile(np.arange(N), T)
    S = S_week[week].astype(np.int8)
    pi = propensity_of(cfg, latent, S)
    D = (rng.random(T * N) < pi).astype(np.int8)
    L0, L1 = potential_losses(cfg, latent, S, shock_week[week], eps)
    L = np.where(D == 1, L1, L0)
    X = latent @ st.loadings.T
    return SimData(X=X, D=D, L=L, S=S, week=week, unit=unit, propensity=pi, L0=L0, L1=L1, latent=latent)


@dataclass(frozen=True)
class Truth:
    """Giá trị thật θ_0 của các đại lượng mục tiêu ở một mức α."""

    alpha: float
    es0: float
    es1: float
    d_es: float
    d_es_normal: float
    d_es_stress: float
    ate: float

    @property
    def h3_delta(self) -> float:
        return abs(self.d_es_stress) - abs(self.d_es_normal)

    def value(self, estimand: str) -> float:
        return {
            "d_es": self.d_es,
            "d_es_normal": self.d_es_normal,
            "d_es_stress": self.d_es_stress,
            "h3_delta": self.h3_delta,
            "ate": self.ate,
        }[estimand]


def population_truth(cfg: DGPConfig, alpha: float, n_samples: int = 1_000_000, seed: int = 0) -> Truth:
    """θ_0 từ ``n_samples`` mẫu tiềm năng (S lấy theo phân phối dừng, η độc lập mỗi mẫu)."""
    return _population_truth_cached(cfg, float(alpha), int(n_samples), int(seed))


@lru_cache(maxsize=256)
def _population_truth_cached(cfg: DGPConfig, alpha: float, n_samples: int, seed: int) -> Truth:
    rng = np.random.default_rng(np.random.SeedSequence([seed, 7919]))
    latent = rng.standard_normal((n_samples, cfg.rank))
    S = (rng.random(n_samples) < cfg.stress_prob).astype(np.int8)
    shock = rng.normal(0.0, cfg.week_shock_sd, size=n_samples)
    eps = _student_t_unit_var(rng, cfg.t_df, n_samples)
    L0, L1 = potential_losses(cfg, latent, S, shock, eps)
    es0, es1 = expected_shortfall(L0, alpha), expected_shortfall(L1, alpha)
    by_state = {}
    for s in (0, 1):
        m = S == s
        by_state[s] = expected_shortfall(L1[m], alpha) - expected_shortfall(L0[m], alpha)
    return Truth(
        alpha=alpha,
        es0=es0,
        es1=es1,
        d_es=es1 - es0,
        d_es_normal=by_state[0],
        d_es_stress=by_state[1],
        ate=float(np.mean(L1 - L0)),
    )
