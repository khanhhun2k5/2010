using System;
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

        public void Start()
        {
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
                var report = _word.ConvertSelection(UserSettings.Load());
                _word.SetStatus(report.Summary);
                if (report.Found == 0 || report.LowConfidence.Count > 0 || report.Problems.Count > 0)
                {
                    MessageBox.Show(report.Details(), "MathTypeX", MessageBoxButtons.OK,
                        report.Problems.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                }
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

        public void Dispose()
        {
            _hook?.Dispose();
            _hook = null;
            _editor.Dispose();
        }
    }
}
