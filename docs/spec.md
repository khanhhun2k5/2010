# Đặc tả thí nghiệm: hiệu ứng nhân quả lên Expected Shortfall với hiệp biến nhiễu

Tài liệu này khóa *cái gì được đo* và *khi nào một giả thuyết bị bác bỏ*, trước khi viết code (Bước 1.1, Ngày 1).
Mọi ngưỡng dưới đây là cố định; nếu muốn đổi, ghi lý do vào `NHAT_KY.md` trước khi xem kết quả.

## 1. Ký hiệu

- Quan sát $(i,t)$: tài sản $i$, tuần $t$ (pilot: 6 cổ phiếu của `px.data.stocks`, dữ liệu tuần). Tổn thất tuần kế tiếp $L(0)=-r_{i,t+1}$.
- Can thiệp $D_{it}\in\{0,1\}$ là giảm rủi ro: theo pilot, $L(1)=0{,}5\,L(0)+c$ với $c=0{,}0015$ (HEDGE_COST, giữ nửa vị thế cộng phí). Trên dữ liệu Việt Nam (Ngày 2), $D$ sẽ là hedge bằng VN30F1M.
- Trong mô phỏng bán tổng hợp, $D$ được gán theo propensity thật $\pi(x)=\sigma(-2+2\,\mathrm{risk})$, với $\mathrm{risk}=12\,\mathrm{mkt\_vol8}+8\max(-\mathrm{mkt\_ret},0)+6\,\mathrm{ownvol8}$; mỗi seed là một lần gán lại $D$ trên cùng đường giá thật.
- Hiệp biến thật $X_{it}\in\mathbb{R}^p$ (cấu trúc hạng thấp $r$) chỉ quan sát được qua $Z_{it}=X_{it}+E_{it}$, có thể thiếu.
  Độ mạnh nhiễu: $\mathrm{NSR}=\mathrm{Var}(E)/\mathrm{Var}(X)$ (trung bình theo cột).
- Trạng thái thị trường $S_t\in\{\text{bình thường},\text{căng thẳng}\}$, xác định *trước* $t$ (mặc định: căng thẳng khi `mkt_vol12`, độ lệch chuẩn 12 tuần của lợi suất thị trường bình quân đều của chính panel, vượt phân vị 80% của chuỗi đó tính đến $t-1$; trên dữ liệu Việt Nam dùng VN-Index). Pilot chưa có trạng thái, đây là phần mới.
- Hiệp biến: 73 đặc trưng giá như pilot (lợi suất trễ, trung bình và độ biến động trượt 4/8/12 tuần, theo tài sản và thị trường), chuẩn hóa rồi chiếu xuống $k$ thành phần chính (V3 low-rank, mặc định $k=4$), cộng dummy tài sản. Propensity: logistic ($C=0{,}5$), cắt vào $[0{,}03;0{,}97]$. Mô hình kết quả: CDF location-scale (Ridge, $\alpha_{\text{ridge}}=3$) cho từng nhánh.

## 2. Đại lượng mục tiêu

Với mức $\alpha\in\{0{,}90;0{,}95\}$ (pilot: 0,90) và tổn thất tiềm năng $L(d)$:

$$\mathrm{ES}_\alpha(d)=\frac{1}{1-\alpha}\int_\alpha^1 \mathrm{VaR}_u\big(L(d)\big)\,du=\min_c\Big\{c+\tfrac{1}{1-\alpha}\,\mathbb{E}\big[(L(d)-c)_+\big]\Big\}.$$

**Không điều kiện:** $\Delta\mathrm{ES}_\alpha=\mathrm{ES}_\alpha(1)-\mathrm{ES}_\alpha(0)$ (âm nghĩa là can thiệp làm giảm rủi ro đuôi). Với can thiệp của pilot, ES thuần nhất dương và bất biến tịnh tiến nên $\Delta\mathrm{ES}_\alpha=c-0{,}5\,\mathrm{ES}_\alpha(L(0))$ đúng chính xác; đây là một unit test.

**Theo trạng thái:** $\Delta\mathrm{ES}_\alpha(s)=\mathrm{ES}_\alpha(1\mid S=s)-\mathrm{ES}_\alpha(0\mid S=s)$, cùng công thức nhưng mọi kỳ vọng lấy có điều kiện $S=s$.

**Điểm ước lượng (DML, trực giao Neyman).** Với $\hat q_d=\widehat{\mathrm{VaR}}_\alpha(L(d))$ (từ CDF phản thực tế dạng AIPW) và $m_d(x)=\mathbb{E}[(L-\hat q_d)_+\mid X=x,D=d]$:

$$\psi_d=\hat q_d+\frac{1}{1-\alpha}\Big[m_d(\hat X)+\frac{\mathbf 1\{D=d\}}{\pi_d(\hat X)}\big((L-\hat q_d)_+-m_d(\hat X)\big)\Big],\qquad \widehat{\mathrm{ES}}_\alpha(d)=\overline{\psi_d}.$$

$\hat q_d$ lấy bằng nghịch đảo CDF AIPW trên lưới 45 phân vị (đã ép đơn điệu), như `estimate_v3` của pilot. Bản theo trạng thái nhân điểm với $\mathbf 1\{S=s\}/\hat P(S=s)$ và tính $\hat q_d$ riêng trong trạng thái $s$.

Cross-fitting: 4 fold theo khối tuần liên tiếp. **Khác pilot:** PURGE_WEEKS = 12 thay vì 2 ở mỗi phía, vì đặc trưng trượt dài nhất là 12 tuần nên purge 2 tuần vẫn để rò rỉ thông tin giữa train và validation. SE gom cụm theo tuần; block bootstrap (khối 12 tuần) để đối chiếu. Pilot chưa có SE hay KTC, đây là phần mới.

Giá trị thật $\theta_0$: với panel thật, là ES thực nghiệm trên toàn panel của $L(1)$ và $L(0)$ (như `TRUE_DELTA` của pilot); với DGP tổng hợp có nhiễu (`dgp.py`), tính từ $N=10^6$ mẫu tiềm năng không nhiễu.

## 3. Giả thuyết có thể bác bỏ

Phạm vi "giả định thỏa": nhiễu Gaussian, làm tròn Poisson, Laplace (kỳ vọng 0), $k\ge r$. Nhiễu lệch và cắt trần/sàn chỉ dùng để tìm giới hạn, không tính vào kết luận H1, H2.

| | Phát biểu | Chỉ số | Ngưỡng ĐẠT | Bác bỏ khi |
|---|---|---|---|---|
| **H1** | Chệch nhỏ khi NSR ≤ 1 | Chệch tương đối $\mathrm{RB}=\lvert\overline{\hat\theta}-\theta_0\rvert/\lvert\theta_0\rvert$ mỗi ô | $\mathrm{RB}<0{,}10$ ở **mọi** ô có NSR ≤ 1 trong phạm vi giả định thỏa | có ô NSR ≤ 1 với $\mathrm{RB}\ge 0{,}10$ và cận dưới KTC Monte Carlo 95% của RB cũng ≥ 0,10 |
| **H2** | KTC 95% đúng danh nghĩa | Tỷ lệ phủ $\hat c$ = tỷ lệ seed có $\theta_0\in$ KTC (SE gom cụm) | $0{,}93\le\hat c\le0{,}97$ ở mọi ô NSR ≤ 1 trong phạm vi giả định thỏa | có ô nằm ngoài [0,93; 0,97] sau khi chạy lại ô đó với 1000 seed |
| **H3** | Rủi ro đuôi phản ứng mạnh hơn khi căng thẳng | $\delta=\lvert\Delta\mathrm{ES}_\alpha(\text{căng thẳng})\rvert-\lvert\Delta\mathrm{ES}_\alpha(\text{bình thường})\rvert$ | cận dưới KTC một phía 95% của $\delta$ > 0 (SE gom cụm theo tuần, phương pháp delta) | cận dưới ≤ 0 |
| **H4** | Quy tắc quyết định hiệu quả hơn hedge luôn luôn | Ngoài mẫu (walk-forward): ES thực hiện $\mathrm{ES}^{\text{rule}}$, $\mathrm{ES}^{\text{never}}$, $\mathrm{ES}^{\text{always}}$; chi phí $C$ = lợi suất bình quân bị bỏ lỡ so với không hedge, $\overline{L^{\text{chiến lược}}}-\overline{L^{\text{never}}}$ (gồm phí 0,0015 mỗi tuần hedge và nửa lợi suất bị bỏ lại) | (a) $\mathrm{ES}^{\text{rule}}<\mathrm{ES}^{\text{never}}$ với cận trên KTC block bootstrap 95% của hiệu < 0, **và** (b) $C^{\text{rule}}\le0{,}7\,C^{\text{always}}$, **và** (c) $\mathrm{ES}^{\text{rule}}-\mathrm{ES}^{\text{always}}\le 0{,}5\,(\mathrm{ES}^{\text{never}}-\mathrm{ES}^{\text{always}})$ | vi phạm (a), (b) hoặc (c) |

Quy tắc quyết định của H4: tại tuần $t$, hedge khi $S_t=s$ và cận trên KTC 95% của $\widehat{\Delta\mathrm{ES}}_\alpha(s)$, ước lượng chỉ bằng dữ liệu trước $t-12$, nhỏ hơn 0.

## 4. Chỉ số báo cáo cho mỗi ô của lưới

Ô = loại nhiễu × NSR {0; 0,25; 0,5; 1; 2} × nhiễu loạn {yếu, vừa, mạnh} × $k$ {2, 4, 6, 8} × $\alpha$ {0,90; 0,95}, ≥ 200 seed (pilot: 50 lần gán).

| Chỉ số | Định nghĩa | Dùng cho |
|---|---|---|
| Chệch, chệch tương đối | $\overline{\hat\theta}-\theta_0$; RB như trên | H1 |
| RMSE | $\sqrt{\overline{(\hat\theta-\theta_0)^2}}$ | H1, so sánh ước lượng |
| Độ phủ | $\hat c$, kèm SE Monte Carlo $\sqrt{\hat c(1-\hat c)/n_{\text{seed}}}$ | H2 |
| Độ dài KTC | trung vị $2\cdot1{,}96\cdot\widehat{\mathrm{SE}}$ | H2 (KTC phủ đúng nhưng quá rộng là vô dụng) |
| Tỷ lệ SE | $\overline{\widehat{\mathrm{SE}}}/\mathrm{sd}(\hat\theta)$ | chẩn đoán H2 |
| MAE | $\overline{\lvert\hat\theta-\theta_0\rvert}$ (chỉ số của pilot) | cổng kiểm định |
| Cổng kiểm định | Mở rộng G0/G1 của pilot (pilot chỉ so V3 với raw): G0 $\lvert$chệch$\rvert$ của V3 nhỏ hơn raw **và** unadjusted; G1 MAE của V3 nhỏ hơn raw **và** unadjusted; G2 $\hat c\ge0{,}90$. ĐẠT khi cả ba đạt | lọc ô hoạt động/thất bại |

Ước lượng so sánh: V3 low-rank, raw, unadjusted, oracle ($X$ thật), Lasso-DML, Thuật toán 1 Agarwal–Singh (ATE, kiểm tra chéo).
Danh sách bắt buộc trong báo cáo: mọi ô có $\hat c<0{,}90$ hoặc RB > 0,10, kể cả ô ngoài phạm vi giả định thỏa.

**Sai số Monte Carlo.** Ô nằm giữa ngưỡng ĐẠT và ngưỡng bác bỏ (H1, H2) là *chưa kết luận*: tăng lên 1000 seed rồi xét lại. Với 200 seed, SE của độ phủ ≈ 0,015, nên ô có $\hat c$ trong [0,92; 0,93) hoặc (0,97; 0,98] gần như chắc chắn rơi vào trường hợp này.
