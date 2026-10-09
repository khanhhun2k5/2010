"""ctr: hiệu ứng nhân quả lên rủi ro đuôi (Expected Shortfall) với hiệp biến nhiễu.

Các mô-đun:
- ``risk``: ES/VaR thực nghiệm, CDF có trọng số.
- ``dgp``: quá trình sinh dữ liệu có kiểm soát và giá trị thật của ΔES_α.
- ``noise``: năm loại nhiễu đo lường (ba loại thỏa giả định, hai loại vi phạm).
- ``nuisance``: propensity và mô hình vị trí–thang ``LocationScaleCDF`` (từ pilot).
- ``estimators``: ``estimate_v3`` của pilot tổng quát hóa: DML trực giao cho ΔES_α (theo trạng thái), ATE.
- ``inference``: SE gom cụm theo tuần, SE iid, block bootstrap.
- ``metrics``: chệch, RMSE, độ phủ, độ dài KTC, cổng kiểm định.
- ``semisynthetic``: thiết kế bán mô phỏng trên lợi suất thật của pilot.
- ``experiment``: chạy lưới Monte Carlo từ file YAML, ghi parquet.
"""

__version__ = "0.1.0"
