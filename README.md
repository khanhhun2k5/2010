# Hiệu ứng nhân quả lên rủi ro đuôi với hiệp biến nhiễu

Gói `ctr/` dựng mô phỏng Monte Carlo có kiểm soát cho ΔES_α theo `docs/spec.md`, dùng lại bộ ước lượng
của pilot dữ liệu thật (`estimate_v3`, `LocationScaleCDF`, fold tuần có purge, so sánh low-rank với raw).

## Cài đặt và kiểm thử

```bash
python3 -m pip install -r requirements.txt
python3 -m pytest -q
```

## Chạy mô phỏng

```bash
python3 -m ctr.experiment configs/smoke.yaml          # lưới nhỏ, vài phút trên 4 lõi
python3 -m ctr.experiment configs/mc_full.yaml        # lưới đầy đủ Bước 1.3, 200 seed mỗi ô
```

Kết quả là bảng dài `results/*.parquet`, mỗi dòng một (ô, ước lượng, đại lượng, seed). Tổng hợp:

```python
import pandas as pd
from ctr.metrics import summarize, validation_gate, flagged_cells, hypothesis_h1_h2

s = summarize(pd.read_parquet("results/smoke.parquet"))   # chệch, RMSE, MAE, độ phủ, độ dài KTC, tỷ lệ SE
validation_gate(s)                                       # G0/G1 so với raw và unadjusted, G2 độ phủ ≥ 0,90
flagged_cells(s)                                         # mọi ô độ phủ < 0,90 hoặc chệch tương đối > 0,10
hypothesis_h1_h2(s, rank=4)                              # đếm ô ĐẠT / KHÔNG ĐẠT / chưa kết luận
```

## Thành phần

| Mô-đun | Nội dung |
|---|---|
| `ctr/dgp.py` | DGP: hiệp biến hạng r, trạng thái căng thẳng Markov theo tuần, cú sốc chung theo tuần, nhiễu loạn γ, đuôi Student-t; θ₀ từ 10⁶ mẫu tiềm năng |
| `ctr/noise.py` | Gaussian, làm tròn Poisson, Laplace (thỏa giả định); lệch và cắt trần/sàn (vi phạm) |
| `ctr/nuisance.py` | Propensity logistic, `LocationScaleCDF` của pilot |
| `ctr/estimators.py` | Điểm ES trực giao Neyman (không điều kiện, theo trạng thái), H3, ATE; các ước lượng `v3_lowrank`, `raw`, `unadjusted`, `oracle`, `oracle_propensity`, `lasso_dml` |
| `ctr/inference.py` | SE gom cụm theo tuần (dùng cho KTC), SE iid, block bootstrap 12 tuần |
| `ctr/metrics.py` | Tổng hợp theo ô, cổng kiểm định, danh sách ô bị đánh dấu, đếm H1/H2 |
| `ctr/semisynthetic.py` | Thiết kế bán mô phỏng của pilot trên lợi suất thật (L(1) = 0,5·L(0) + phí) |
| `ctr/experiment.py` | Đọc YAML, chạy song song, ghi parquet |

## Khác pilot (theo spec, đều chỉnh được trong YAML)

- `purge_weeks` = 12 (pilot: 2). Đặt về 2 thì kết quả trùng pilot tới < 1e-6 (`tests/test_pilot_reproduction.py`).
- Trạng thái căng thẳng: mkt_vol12 vượt phân vị 80% tính đến tuần trước (`PilotConfig.stress_quantile`, `stress_window`).
- SE và KTC gom cụm theo tuần.
- Cổng G0/G1 so với cả raw và unadjusted; G2 đòi độ phủ ≥ 0,90 (`min_coverage`).
- 200 seed mỗi ô (pilot: 50).

## Tái lập

Mọi số ngẫu nhiên lấy từ `SeedSequence(base_seed, spawn_key=...)` với khóa là (seed lặp, loại nhiễu, NSR, …),
không phụ thuộc thứ tự chạy hay số tiến trình; test kiểm tra 1 tiến trình và 2 tiến trình cho cùng bảng kết quả.
Cùng seed lặp dùng chung dữ liệu gốc giữa các loại nhiễu và NSR (biến ngẫu nhiên chung).
