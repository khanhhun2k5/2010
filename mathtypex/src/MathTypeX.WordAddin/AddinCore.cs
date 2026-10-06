using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using MathTypeX.Editing;
using MathTypeX.Interop;

namespace MathTypeX.WordAddin
{
    /// <summary>
    /// Logic của add-in: Alt+M → chụp ngữ cảnh → gọi editor (không chặn thread UI của Word) → chèn kết quả.
    /// </summary>
    internal sealed class AddinCore : IDisposable
    {
        private readonly WordGateway _word;
        private readonly EditorConnection _editor = new();
        private readonly SynchronizationContext _ui;
        private KeyboardHook? _hook;
        private bool _busy;

        public AddinCore(object application)
        {
            _word = new WordGateway(application);
            // Word không cài SynchronizationContext nào; dùng của WinForms để await quay về đúng thread UI.
            _ui = new WindowsFormsSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(_ui);
        }

        /// <summary>Cài hook Alt+M và khởi động sẵn editor. Gọi nhiều lần cũng chỉ có tác dụng một lần.</summary>
        public void Start()
        {
            if (_hook is not null) return;
            _hook = new KeyboardHook(() => _ui.Post(_ => OpenEditor(), null));
            _editor.Prewarm();
        }

        public async void OpenEditor()
        {
            if (_busy)
            {
                AddinLog.Info("Editor đang mở — bỏ qua Alt+M");
                return;
            }
            _busy = true;
            SynchronizationContext.SetSynchronizationContext(_ui);
            try
            {
                var ctx = _word.Capture();
                var existing = ctx.Existing;
                var request = new EditRequest
                {
                    Host = "Word",
                    Latex = existing?.Latex ?? ctx.SelectedText ?? "",
                    Display = existing?.Display ?? false,
                    DisplayAllowed = ctx.DisplayAllowed,
                    FontSizePt = ctx.FontSizePt,
                    TextFont = ctx.FontName,
                    PreferredMathFont = existing?.MathFont,
                    Caret = ctx.Caret,
                    OwnerWindow = ctx.WordWindow.ToInt64(),
                    HostProcessId = System.Diagnostics.Process.GetCurrentProcess().Id,
                    Notice = existing is { FromRecord: false }
                        ? "Công thức này chưa có source LaTeX lưu kèm (tạo bằng Word hoặc đã bị sửa trực tiếp) — đã chuyển ngược từ Word Equation."
                        : null,
                };

                var client = await _editor.GetClientAsync();
                NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
                var result = await client.InvokeAsync<EditRequest, EditResult>(RpcMethods.Edit, request);

                if (result.Cancelled) return;
                _word.Apply(ctx, result);
            }
            catch (Exception ex)
            {
                AddinLog.Error("OpenEditor", ex);
                MessageBox.Show(ex.Message, "MathTypeX", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>Ribbon "Chuyển LaTeX": thay $…$, $$…$$, \(…\), \[…\] trong vùng chọn bằng Word Equation.</summary>
        public void ConvertSelection()
        {
            if (_busy)
            {
                AddinLog.Info("Editor đang mở — bỏ qua Convert");
                return;
            }
            _busy = true;
            try
            {
                ShowReport(_word.ConvertSelection(UserSettings.Load()));
            }
            catch (Exception ex)
            {
                AddinLog.Error("ConvertSelection", ex);
                MessageBox.Show(ex.Message, "MathTypeX", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>
        /// Ribbon "Chuyển cả tài liệu" (VS-9): quét mọi story → hộp duyệt trong editor (có preview) → chuyển các mục được chọn.
        /// Không mở được editor thì hỏi bằng MessageBox và chỉ chuyển các mục chắc chắn.
        /// </summary>
        public async void ConvertDocument()
        {
            if (_busy)
            {
                AddinLog.Info("Đang bận — bỏ qua Convert Document");
                return;
            }
            _busy = true;
            SynchronizationContext.SetSynchronizationContext(_ui);
            try
            {
                var settings = UserSettings.Load();
                _word.SetStatus("MathTypeX: đang quét tài liệu…");
                var scan = _word.ScanDocument(settings);
                _word.SetStatus("");
                if (scan.Entries.Count == 0)
                {
                    MessageBox.Show(scan.IgnoredCount > 0
                            ? $"Không có công thức LaTeX nào cần chuyển ({scan.IgnoredCount} mục nằm trong danh sách luôn bỏ qua)."
                            : "Không thấy công thức LaTeX ($…$, $$…$$, \\(…\\), \\[…\\]) nào trong tài liệu.",
                        "MathTypeX", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                ScanResult review;
                try
                {
                    var client = await _editor.GetClientAsync();
                    NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
                    review = await client.InvokeAsync<ScanRequest, ScanResult>(RpcMethods.Review,
                        WordGateway.ToReviewRequest(scan, settings, NativeMethods.GetForegroundWindow()));
                }
                catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException or UnauthorizedAccessException)
                {
                    AddinLog.Error("Review", ex);
                    var recommended = scan.Entries.Where(e => e.Candidate.Recommended && e.Blocked is null).Select(e => e.Id).ToArray();
                    var answer = MessageBox.Show(
                        $"Không mở được hộp duyệt ({ex.Message}).\n\nTìm thấy {scan.Entries.Count} công thức, {recommended.Length} mục chắc chắn là công thức. Chuyển {recommended.Length} mục đó?",
                        "MathTypeX", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    review = answer == DialogResult.Yes ? new ScanResult { SelectedIds = recommended } : new ScanResult { Cancelled = true };
                }

                if (review.Cancelled || review.SelectedIds.Length == 0) return;
                ShowReport(_word.ApplyDocument(scan, review.SelectedIds.ToList(), settings));
            }
            catch (Exception ex)
            {
                AddinLog.Error("ConvertDocument", ex);
                MessageBox.Show(ex.Message, "MathTypeX", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>Ribbon "Trả về LaTeX": equation trong vùng chọn (hoặc tại con trỏ) → văn bản LaTeX gốc.</summary>
        public void RevertToLatex()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                int count = _word.RevertSelection();
                _word.SetStatus(count == 0
                    ? "MathTypeX: không có công thức nào trong vùng chọn."
                    : $"MathTypeX: đã trả {count} công thức về văn bản LaTeX.");
            }
            catch (Exception ex)
            {
                AddinLog.Error("RevertToLatex", ex);
                MessageBox.Show(ex.Message, "MathTypeX", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _busy = false;
            }
        }

        private void ShowReport(ConversionReport report)
        {
            _word.SetStatus(report.Summary);
            if (report.NeedsAttention)
            {
                MessageBox.Show(report.Details(), "MathTypeX", MessageBoxButtons.OK,
                    report.Problems.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
            }
        }

        public void Dispose()
        {
            _hook?.Dispose();
            _hook = null;
            _editor.Dispose();
        }
    }
}
