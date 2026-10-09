# Đặc tả thí nghiệm: hiệu ứng nhân quả lên Expected Shortfall với hiệp biến nhiễu

Tài liệu này khóa *cái gì được đo* và *khi nào một giả thuyết bị bác bỏ*, trước khi viết code (Bước 1.1, Ngày 1).
Mọi ngưỡng dưới đây là cố định; nếu muốn đổi, ghi lý do vào `NHAT_KY.md` trước khi xem kết quả.

## 1. Ký hiệu

- Quan sát $(i,t)$: đơn vị $i$, tuần $t$. Can thiệp $D_{it}\in\{0,1\}$ (ví dụ: hedge bằng VN30F1M), lợi suất tuần kế tiếp $Y_{it}$, tổn thất $L=-Y$.
- Hiệp biến thật $X_{it}\in\mathbb{R}^p$ (cấu trúc hạng thấp $r$) chỉ quan sát được qua $Z_{it}=X_{it}+E_{it}$, có thể thiếu.
  Độ mạnh nhiễu: $\mathrm{NSR}=\mathrm{Var}(E)/\mathrm{Var}(X)$ (trung bình theo cột).
- Trạng thái thị trường $S_t\in\{\text{bình thường},\text{căng thẳng}\}$, xác định *trước* $t$ (mặc định: căng thẳng khi độ biến động thực hiện 12 tuần của VN-Index vượt phân vị 80% lịch sử tính đến $t-1$).
- Propensity $\pi(x)=P(D=1\mid X=x)$; mô hình kết quả ước lượng trên $\hat X$ = khử nhiễu hạng $k$ của $Z$ (PCR, Agarwal–Singh).

## 2. Đại lượng mục tiêu

Với mức $\alpha\in\{0{,}90;0{,}95\}$ và tổn thất tiềm năng $L(d)$:

$$\mathrm{ES}_\alpha(d)=\frac{1}{1-\alpha}\int_\alpha^1 \mathrm{VaR}_u\big(L(d)\big)\,du=\min_c\Big\{c+\tfrac{1}{1-\alpha}\,\mathbb{E}\big[(L(d)-c)_+\big]\Big\}.$$

**Không điều kiện:** $\Delta\mathrm{ES}_\alpha=\mathrm{ES}_\alpha(1)-\mathrm{ES}_\alpha(0)$ (âm nghĩa là can thiệp làm giảm rủi ro đuôi).

**Theo trạng thái:** $\Delta\mathrm{ES}_\alpha(s)=\mathrm{ES}_\alpha(1\mid S=s)-\mathrm{ES}_\alpha(0\mid S=s)$, cùng công thức nhưng mọi kỳ vọng lấy có điều kiện $S=s$.

**Điểm ước lượng (DML, trực giao Neyman).** Với $\hat q_d=\widehat{\mathrm{VaR}}_\alpha(L(d))$ (từ CDF phản thực tế dạng AIPW) và $m_d(x)=\mathbb{E}[(L-\hat q_d)_+\mid X=x,D=d]$:

$$\psi_d=\hat q_d+\frac{1}{1-\alpha}\Big[m_d(\hat X)+\frac{\mathbf 1\{D=d\}}{\pi_d(\hat X)}\big((L-\hat q_d)_+-m_d(\hat X)\big)\Big],\qquad \widehat{\mathrm{ES}}_\alpha(d)=\overline{\psi_d}.$$

Bản theo trạng thái nhân điểm với $\mathbf 1\{S=s\}/\hat P(S=s)$. Cross-fitting theo khối thời gian, bỏ $\ge 12$ tuần giữa các fold (PURGE_WEEKS). SE gom cụm theo tuần; block bootstrap (khối 12 tuần) để đối chiếu.
Giá trị thật $\theta_0$ trong mô phỏng tính từ DGP với $N=10^6$ mẫu tiềm năng (không nhiễu).

## 3. Giả thuyết có thể bác bỏ

Phạm vi "giả định thỏa": nhiễu Gaussian, làm tròn Poisson, Laplace (kỳ vọng 0), $k\ge r$. Nhiễu lệch và cắt trần/sàn chỉ dùng để tìm giới hạn, không tính vào kết luận H1, H2.

| | Phát biểu | Chỉ số | Ngưỡng ĐẠT | Bác bỏ khi |
|---|---|---|---|---|
| **H1** | Chệch nhỏ khi NSR ≤ 1 | Chệch tương đối $\mathrm{RB}=\lvert\overline{\hat\theta}-\theta_0\rvert/\lvert\theta_0\rvert$ mỗi ô | $\mathrm{RB}<0{,}10$ ở **mọi** ô có NSR ≤ 1 trong phạm vi giả định thỏa | có ô NSR ≤ 1 với $\mathrm{RB}\ge 0{,}10$ và cận dưới KTC Monte Carlo 95% của RB cũng ≥ 0,10 |
| **H2** | KTC 95% đúng danh nghĩa | Tỷ lệ phủ $\hat c$ = tỷ lệ seed có $\theta_0\in$ KTC (SE gom cụm) | $0{,}93\le\hat c\le0{,}97$ ở mọi ô NSR ≤ 1 trong phạm vi giả định thỏa | có ô nằm ngoài [0,93; 0,97] sau khi chạy lại ô đó với 1000 seed |
| **H3** | Rủi ro đuôi phản ứng mạnh hơn khi căng thẳng | $\delta=\lvert\Delta\mathrm{ES}_\alpha(\text{căng thẳng})\rvert-\lvert\Delta\mathrm{ES}_\alpha(\text{bình thường})\rvert$ | cận dưới KTC một phía 95% của $\delta$ > 0 (SE gom cụm theo tuần, phương pháp delta) | cận dưới ≤ 0 |
| **H4** | Quy tắc quyết định hiệu quả hơn hedge luôn luôn | Ngoài mẫu (walk-forward): ES thực hiện $\mathrm{ES}^{\text{rule}}$, $\mathrm{ES}^{\text{never}}$, $\mathrm{ES}^{\text{always}}$; chi phí $C$ = số tuần hedge × phí giao dịch + roll | (a) $\mathrm{ES}^{\text{rule}}<\mathrm{ES}^{\text{never}}$ với cận trên KTC block bootstrap 95% của hiệu < 0, **và** (b) $C^{\text{rule}}\le0{,}7\,C^{\text{always}}$, **và** (c) $\mathrm{ES}^{\text{rule}}-\mathrm{ES}^{\text{always}}\le 0{,}5\,(\mathrm{ES}^{\text{never}}-\mathrm{ES}^{\text{always}})$ | vi phạm (a), (b) hoặc (c) |

Quy tắc quyết định của H4: tại tuần $t$, hedge khi $S_t=s$ và cận trên KTC 95% của $\widehat{\Delta\mathrm{ES}}_\alpha(s)$, ước lượng chỉ bằng dữ liệu trước $t-12$, nhỏ hơn 0.

## 4. Chỉ số báo cáo cho mỗi ô của lưới

Ô = loại nhiễu × NSR {0; 0,25; 0,5; 1; 2} × nhiễu loạn {yếu, vừa, mạnh} × $k$ {2, 4, 6, 8} × $\alpha$ {0,90; 0,95}, ≥ 200 seed.

| Chỉ số | Định nghĩa | Dùng cho |
|---|---|---|
| Chệch, chệch tương đối | $\overline{\hat\theta}-\theta_0$; RB như trên | H1 |
| RMSE | $\sqrt{\overline{(\hat\theta-\theta_0)^2}}$ | H1, so sánh ước lượng |
| Độ phủ | $\hat c$, kèm SE Monte Carlo $\sqrt{\hat c(1-\hat c)/n_{\text{seed}}}$ | H2 |
| Độ dài KTC | trung vị $2\cdot1{,}96\cdot\widehat{\mathrm{SE}}$ | H2 (KTC phủ đúng nhưng quá rộng là vô dụng) |
| Tỷ lệ SE | $\overline{\widehat{\mathrm{SE}}}/\mathrm{sd}(\hat\theta)$ | chẩn đoán H2 |
| Cổng kiểm định | ĐẠT khi RMSE(V3) < RMSE(raw) **và** < RMSE(unadjusted) **và** $\hat c\ge0{,}90$ | lọc ô hoạt động/thất bại |

Ước lượng so sánh: V3 low-rank, raw, unadjusted, oracle ($X$ thật), Lasso-DML, Thuật toán 1 Agarwal–Singh (ATE, kiểm tra chéo).
Danh sách bắt buộc trong báo cáo: mọi ô có $\hat c<0{,}90$ hoặc RB > 0,10, kể cả ô ngoài phạm vi giả định thỏa.

**Sai số Monte Carlo.** Ô nằm giữa ngưỡng ĐẠT và ngưỡng bác bỏ (H1, H2) là *chưa kết luận*: tăng lên 1000 seed rồi xét lại. Với 200 seed, SE của độ phủ ≈ 0,015, nên ô có $\hat c$ trong [0,92; 0,93) hoặc (0,97; 0,98] gần như chắc chắn rơi vào trường hợp này.
