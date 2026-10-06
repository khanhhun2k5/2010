using System;
using System.Reflection;
using MathTypeX.Documents;
using MathTypeX.Interop;

namespace MathTypeX.WordAddin
{
    /// <summary>Công thức có sẵn tại con trỏ (VS-4).</summary>
    internal sealed class ExistingEquation
    {
        public dynamic Range = null!;
        public string Key = "";
        public string Latex = "";
        public bool Display;
        public string? MathFont;
        public bool FromRecord;
    }

    /// <summary>Vị trí chèn đã chụp lúc nhấn Alt+M (Range của Word tự cập nhật khi tài liệu thay đổi).</summary>
    internal sealed class InsertionContext
    {
        public dynamic Document = null!;
        public dynamic Range = null!;
        public ScreenRect? Caret;
        public double? FontSizePt;
        public string? FontName;
        public bool DisplayAllowed;
        public ExistingEquation? Existing;
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
        private const int WdOMathDisplay = 0;

        private readonly dynamic _app;
        private readonly LocalEquationStore _local = new();

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

            ctx.Existing = ReadEquationAtSelection(ctx.Document, selection);

            // Display equation phải đứng riêng một đoạn: chỉ cho chọn Display khi đoạn chỉ chứa công thức (hoặc trống).
            dynamic paragraph = range.Paragraphs.Item(1);
            string paragraphText = (paragraph.Range.Text as string) ?? "";
            if (ctx.Existing is { } existing)
            {
                string equationText = (existing.Range.Text as string) ?? "";
                ctx.DisplayAllowed = existing.Display || paragraphText.Replace(equationText, "").Trim('\r', '\a', ' ', '\t').Length == 0;
            }
            else
            {
                ctx.DisplayAllowed = (int)range.Start == (int)range.End && paragraphText.Trim('\r', '\a', ' ', '\t').Length == 0;
            }

            ctx.Caret = TryGetCaretRect((object)_app.ActiveWindow, (object)range);
            return ctx;
        }

        /// <summary>
        /// Con trỏ nằm trong một Word Equation: đọc OMML → khoá chuẩn hoá → tra kho trong tài liệu (L1), kho cục bộ (L3),
        /// cuối cùng chuyển ngược OMML → LaTeX (L4). Không bao giờ đoán từ hình ảnh.
        /// </summary>
        private ExistingEquation? ReadEquationAtSelection(dynamic doc, dynamic selection)
        {
            if ((int)selection.OMaths.Count == 0) return null;
            dynamic om = selection.OMaths.Item(1);
            dynamic omRange = om.Range;
            if ((int)selection.Start < (int)omRange.Start || (int)selection.End > (int)omRange.End) return null;

            var element = OmmlExtractor.FirstEquation((string)omRange.WordOpenXML);
            if (element is null) return null;
            var reverse = OmmlToLatex.Convert(element);
            string key = EquationIdentity.KeyOfNormalized(reverse.NormalizedLatex);
            var record = ReadDocumentStore(doc).FindByKey(key) ?? _local.FindByKey(key);
            AddinLog.Info($"Sửa công thức: key={key}, nguồn={(record is null ? "chuyển ngược" : "metadata")}");

            return new ExistingEquation
            {
                Range = omRange,
                Key = key,
                Latex = record?.OriginalLatex ?? reverse.NormalizedLatex,
                Display = (int)om.Type == WdOMathDisplay || reverse.Display,
                MathFont = reverse.PrimaryFont ?? record?.MathFont,
                FromRecord = record is not null,
            };
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

        /// <summary>
        /// Chèn mới hoặc thay công thức cũ bằng OMML (gói Flat OPC), gói trong một mục Undo duy nhất; sau đó đọc lại
        /// OMML mà Word thực sự lưu để tính khoá và ghi metadata.
        /// </summary>
        public void Apply(InsertionContext ctx, EditResult result)
        {
            dynamic doc = ctx.Document;
            dynamic range = ctx.Existing?.Range ?? ctx.Range;
            int start = range.Start;
            int paragraphsBefore = doc.Paragraphs.Count;

            dynamic undo = _app.UndoRecord;
            undo.StartCustomRecord(ctx.Existing is null ? "MathTypeX: chèn công thức" : "MathTypeX: sửa công thức");
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
                AddinLog.Info($"Đã {(ctx.Existing is null ? "chèn" : "thay")}: paragraphs {paragraphsBefore}→{(int)doc.Paragraphs.Count}, end={end}");

                if (end >= 0) _app.Selection.SetRange(end, end);
            }
            finally
            {
                undo.EndCustomRecord();
            }

            SaveMetadata(doc, start, result);
        }

        private void SaveMetadata(dynamic doc, int start, EditResult result)
        {
            try
            {
                dynamic? om = FindEquation(doc, start);
                if (om is null) return;
                var element = OmmlExtractor.FirstEquation((string)om.Range.WordOpenXML);
                if (element is null) return;
                string key = EquationIdentity.KeyOfNormalized(OmmlToLatex.Convert(element).NormalizedLatex);
                var record = EquationRecord.Create(key, result.Latex, result.NormalizedLatex, result.Display, result.MathFont);
                var store = ReadDocumentStore(doc);
                store.Upsert(record);
                WriteDocumentStore(doc, store);
                _local.Save(record);
                AddinLog.Info($"Đã lưu metadata: key={key}");
            }
            catch (Exception ex)
            {
                // Metadata hỏng không được làm hỏng công thức vừa chèn; lần sau vẫn chuyển ngược được.
                AddinLog.Error("SaveMetadata", ex);
            }
        }

        private static EquationStore ReadDocumentStore(dynamic doc)
        {
            dynamic parts = doc.CustomXMLParts.SelectByNamespace(EquationStore.Namespace);
            return (int)parts.Count == 0 ? new EquationStore() : EquationStore.Parse((string)parts.Item(1).XML);
        }

        private static void WriteDocumentStore(dynamic doc, EquationStore store)
        {
            dynamic parts = doc.CustomXMLParts.SelectByNamespace(EquationStore.Namespace);
            for (int i = (int)parts.Count; i >= 1; i--) parts.Item(i).Delete();
            doc.CustomXMLParts.Add(store.ToXml());
        }

        private static dynamic? FindEquation(dynamic doc, int start)
        {
            int docEnd = doc.Content.End;
            dynamic probe = doc.Range(start, Math.Min(start + 1, docEnd));
            return (int)probe.OMaths.Count > 0 ? probe.OMaths.Item(1) : null;
        }

        private static int FindEquationEnd(dynamic doc, int start)
        {
            try
            {
                dynamic? om = FindEquation(doc, start);
                if (om is not null) return om.Range.End;
            }
            catch (Exception ex)
            {
                AddinLog.Error("FindEquationEnd", ex);
            }
            return -1;
        }
    }
}
