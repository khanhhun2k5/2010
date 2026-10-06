# ADR-0001 — Nền tảng kiến trúc

- **Trạng thái:** Chấp nhận (06/10/2026)
- **Liên quan:** [README](../../README.md) D1–D10, [02](../02-stack-and-architecture.md)

## Bối cảnh

Bản phân tích thiết kế ban đầu đưa ra 10 quyết định (D1–D10) và 6 câu hỏi mở. Ngày 06/10/2026, chủ dự án trả lời:

| # | Câu hỏi | Trả lời |
|---|---|---|
| Q1 | Phiên bản Office | Office 365 |
| Q2 | Editor là tiến trình riêng | Có |
| Q3 | TeX | MiKTeX |
| Q4 | Cỡ ∫ mặc định | `TeX`, chuyển đổi được |
| Q5 | Phát hành | Mã nguồn mở, dùng cá nhân |
| Q6 | Nguồn lực | Claude viết toàn bộ |

## Quyết định

1. Giữ D2–D10 như trong README.
2. D1 được sửa: dùng COM add-in thay cho VSTO ([ADR-0002](0002-com-addin-instead-of-vsto.md)).
3. **Chỉ hỗ trợ Office 365.**
   - SVG là định dạng vector mặc định.
   - EMF bị đưa ra khỏi MVP.
   - Capability Matrix chỉ đo trên Microsoft 365 Apps.
4. **MiKTeX là bản TeX được hỗ trợ.**
   - Mọi lần gọi engine đều có `--disable-installer` và `--disable-write18`, để MiKTeX không tự tải package qua mạng và không chạy lệnh shell.
   - Tuỳ chọn hạn chế đọc/ghi file kiểu `openin_any` của TeX Live chưa chắc có tương đương trong MiKTeX. Cho tới khi spike S7 xác minh, việc giới hạn file dựa vào token Low IL cộng thư mục job riêng.
5. **License MIT.** Không ký số bắt buộc. Không có telemetry dưới bất kỳ hình thức nào.
6. **Roadmap tính theo vertical slice**, không theo tuần. Các spike cần Office (S1, S2, S3, S5, S6, S10, S11) được chuyển thành **kịch bản kiểm tra** do chủ dự án chạy trên Windows; kết quả ghi vào `docs/spikes/`.

## Hệ quả

- Phần lớn code (core, parser, OMML, font, CLI) được build và test trên Linux CI.
- Phần cần Windows (add-in, editor WPF) vẫn compile được trên Linux nhờ `EnableWindowsTargeting`. Tuy nhiên chỉ kiểm chứng được khi chủ dự án chạy kịch bản kiểm tra trên Windows.
