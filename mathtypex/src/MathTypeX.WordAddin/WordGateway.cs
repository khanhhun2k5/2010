using System;
using System.Reflection;
using MathTypeX.Interop;

namespace MathTypeX.WordAddin
{
    /// <summary>Vị trí chèn đã chụp lúc nhấn Alt+M (Range của Word tự cập nhật khi tài liệu thay đổi).</summary>
    internal sealed class InsertionContext
    {
        public dynamic Document = null!;
        public dynamic Range = null!;
        public ScreenRect? Caret;
        public double? FontSizePt;
        public string? FontName;
        public bool DisplayAllowed;
        public bool InsideEquation;
        public IntPtr WordWindow;
    }

    /// <summary>
    /// Mọi lời gọi Word Object Model (late binding, không cần PIA) — chỉ được gọi trên thread UI của Word.
    /// Phần còn lại của add-in không chạm vào <c>dynamic</c>.
    /// </summary>
    internal sealed class WordGateway
    {
        private const int WdUndefined = 9999999;
        private const int WdWord2007CompatibilityMode = 12;

        private readonly dynamic _app;

        public WordGateway(object application) => _app = application;

        public InsertionContext Capture()
        {
            dynamic selection = _app.Selection;
            dynamic range = selection.Range;
            var ctx = new InsertionContext
            {
                Document = range.Document,
                Range = range,
                WordWindow = NativeMethods.GetForegroundWindow(),
            };

            if ((int)ctx.Document.CompatibilityMode < WdWord2007CompatibilityMode)
                throw new InvalidOperationException("Tài liệu đang ở chế độ tương thích Word 97–2003 nên không chèn được Word Equation. Hãy dùng File → Info → Convert.");

            double size = Convert.ToDouble(selection.Font.Size);
            ctx.FontSizePt = size > 0 && size < WdUndefined ? size : null;
            string? fontName = selection.Font.Name as string;
            ctx.FontName = string.IsNullOrEmpty(fontName) ? null : fontName;
            ctx.InsideEquation = (int)selection.OMaths.Count > 0;

            // Display equation phải đứng riêng một đoạn: chỉ cho chọn Display khi đoạn hiện tại đang trống (VS-2).
            dynamic paragraph = range.Paragraphs.Item(1);
            string paragraphText = (paragraph.Range.Text as string) ?? "";
            ctx.DisplayAllowed = (int)range.Start == (int)range.End && paragraphText.Trim('\r', '\a', ' ', '\t').Length == 0;

            ctx.Caret = TryGetCaretRect((object)_app.ActiveWindow, (object)range);
            return ctx;
        }

        /// <summary>Window.GetPoint trả về toạ độ màn hình (pixel) của một Range — dùng InvokeMember vì có tham số out.</summary>
        private static ScreenRect? TryGetCaretRect(object window, object range)
        {
            try
            {
                object[] args = { 0, 0, 0, 0, range };
                var modifiers = new ParameterModifier(5);
                for (int i = 0; i < 4; i++) modifiers[i] = true;
                window.GetType().InvokeMember("GetPoint", BindingFlags.InvokeMethod, null, window, args, new[] { modifiers }, null, null);
                return new ScreenRect
                {
                    Left = Convert.ToInt32(args[0]),
                    Top = Convert.ToInt32(args[1]),
                    Width = Convert.ToInt32(args[2]),
                    Height = Convert.ToInt32(args[3]),
                };
            }
            catch (Exception ex)
            {
                AddinLog.Error("GetPoint", ex);
                return null;
            }
        }

        /// <summary>Chèn OMML (gói Flat OPC) vào vị trí đã chụp, gói trong một mục Undo duy nhất.</summary>
        public void Insert(InsertionContext ctx, EditResult result)
        {
            dynamic doc = ctx.Document;
            dynamic range = ctx.Range;
            int start = range.Start;
            int paragraphsBefore = doc.Paragraphs.Count;

            dynamic undo = _app.UndoRecord;
            undo.StartCustomRecord("MathTypeX: chèn công thức");
            try
            {
                range.InsertXML(result.FlatOpc);

                // ⚠ S1: InsertXML với một đoạn văn trọn vẹn có thể sinh thêm một dấu xuống đoạn — bỏ nó đi.
                int end = FindEquationEnd(doc, start);
                int paragraphsAfter = doc.Paragraphs.Count;
                if (!result.Display && paragraphsAfter == paragraphsBefore + 1 && end >= 0)
                {
                    dynamic mark = doc.Range(end, end + 1);
                    if ((mark.Text as string) == "\r") mark.Delete();
                }
                AddinLog.Info($"Đã chèn: paragraphs {paragraphsBefore}→{(int)doc.Paragraphs.Count}, end={end}");

                if (end >= 0) _app.Selection.SetRange(end, end);
            }
            finally
            {
                undo.EndCustomRecord();
            }
        }

        private static int FindEquationEnd(dynamic doc, int start)
        {
            try
            {
                int docEnd = doc.Content.End;
                dynamic probe = doc.Range(start, Math.Min(start + 1, docEnd));
                if ((int)probe.OMaths.Count > 0) return probe.OMaths.Item(1).Range.End;
            }
            catch (Exception ex)
            {
                AddinLog.Error("FindEquationEnd", ex);
            }
            return -1;
        }
    }
}
