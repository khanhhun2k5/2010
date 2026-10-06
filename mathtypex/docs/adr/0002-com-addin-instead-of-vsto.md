# ADR-0002 — Dùng COM add-in thay cho VSTO

- **Trạng thái:** Chấp nhận (06/10/2026)
- **Thay thế:** D1 trong bản đề xuất ban đầu

## Bối cảnh

Project VSTO cần các file build `Microsoft.VisualStudio.Tools.Office.targets`. Các file này chỉ có khi cài Visual Studio kèm workload *Office/SharePoint development*, nên không build được bằng `dotnet build` (kể cả trên Windows nếu không có Visual Studio), và càng không build được trên Linux CI. Ngoài ra VSTO còn cần manifest ClickOnce đã ký số và VSTO Runtime.

Dự án do Claude viết toàn bộ, build trong container Linux; chủ dự án chỉ chạy thử trên Windows.

## Quyết định

Add-in Word/PowerPoint là **COM add-in thông thường** viết bằng C# .NET Framework 4.8, project SDK-style (gói `Microsoft.NETFramework.ReferenceAssemblies`):

- Lớp `[ComVisible]` có `[Guid]` và `[ProgId]` cố định, implement:
  - `IDTExtensibility2` (khai báo lại bằng `ComImport`, GUID `B65AD801-ABAF-11D0-BB8B-00A0C90F2744`);
  - `IRibbonExtensibility` (GUID `000C0396-0000-0000-C000-000000000046`).
- Ribbon callback gọi qua `IDispatch` (lớp dùng `ClassInterfaceType.AutoDispatch`).
- Gọi Office Object Model bằng **late binding** (`dynamic`), không phụ thuộc PIA. Mọi lệnh gọi được gói trong một lớp `WordGateway` có kiểu rõ ràng, để phần còn lại của code không chạm vào `dynamic`.
- Đăng ký per-user bằng script PowerShell (không cần admin):
  - `HKCU\Software\Classes\CLSID\{…}\InprocServer32`: `mscoree.dll`, Class, Assembly, CodeBase, RuntimeVersion;
  - ProgId;
  - `HKCU\Software\Microsoft\Office\Word\Addins\MathTypeX.Word` với `LoadBehavior=3`.

## Hệ quả

- ✅ Build được bằng `dotnet build` trên mọi hệ điều hành; không cần Visual Studio.
- ✅ Không phụ thuộc VSTO Runtime và ClickOnce.
- ⚠ Không có AppDomain riêng như VSTO: add-in chạy chung AppDomain mặc định với các COM add-in managed khác không dùng shim. Chấp nhận được vì đây là dùng cá nhân. Mọi điểm vào từ Office đều có `try/catch`.
- ⚠ `dynamic` không được trình biên dịch kiểm tra. Rủi ro này được giảm bằng `WordGateway` mỏng cùng kịch bản kiểm tra tích hợp chạy trên Windows.
