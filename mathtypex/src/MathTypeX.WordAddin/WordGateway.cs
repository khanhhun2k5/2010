using System;
using System.Linq;
using System.Reflection;
using MathTypeX.Documents;
using MathTypeX.Editing.Conversion;
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
        /// <summary>Đoạn văn bản đang chọn (không phải equation): Alt+M mở editor với đoạn này làm LaTeX, Enter thay nó.</summary>
        public string? SelectedText;
        public IntPtr WordWindow;
    }

    /// <summary>
    /// Mọi lời gọi Word Object Model (late binding, không cần PIA) — chỉ được gọi trên thread UI của Word.
    /// Phần còn lại của add-in không chạm vào <c>dynamic</c>.
    /// </summary>
    internal sealed partial class WordGateway
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
            string rawSelection = (int)range.Start == (int)range.End ? "" : (range.Text as string) ?? "";
            if (ctx.Existing is null && rawSelection.Length > 0 && rawSelection.Length <= 4000
                && !rawSelection.Any(c => c < ' ' && c != '\v' && c != '\t') && (int)range.OMaths.Count == 0)
            {
                ctx.SelectedText = ConversionPlanner.NormalizeWordText(rawSelection).Trim();
            }

            // Display equation phải đứng riêng một đoạn: chỉ cho chọn Display khi đoạn chỉ chứa công thức (hoặc trống).
            dynamic paragraph = range.Paragraphs.Item(1);
            string paragraphText = (paragraph.Range.Text as string) ?? "";
            if (ctx.Existing is { } existing)
            {
                string equationText = (existing.Range.Text as string) ?? "";
                ctx.DisplayAllowed = existing.Display || paragraphText.Replace(equationText, "").Trim('\r', '\a', ' ', '\t').Length == 0;
            }
            else if (ctx.SelectedText is not null)
            {
                ctx.DisplayAllowed = paragraphText.Replace(rawSelection, "").Trim('\r', '\a', ' ', '\t').Length == 0;
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

            dynamic undo = _app.UndoRecord;
            undo.StartCustomRecord(ctx.Existing is null ? "MathTypeX: chèn công thức" : "MathTypeX: sửa công thức");
            try
            {
                int end = InsertMath(range, range, result.FlatOpc, result.Display);
                AddinLog.Info($"Đã {(ctx.Existing is null ? "chèn" : "thay")}: end={end}");
                if (end >= 0) _app.Selection.SetRange(end, end);
            }
            finally
            {
                undo.EndCustomRecord();
            }

            SaveMetadata(doc, range, start, result);
        }

        /// <summary>
        /// Thay <paramref name="target"/> bằng OMML (gói Flat OPC) và trả về vị trí cuối của equation (-1 nếu không thấy).
        /// <paramref name="anchor"/> là một Range bất kỳ trong cùng story (main, footnote, header…) để dựng Range con.
        /// <para>
        /// ⚠ S1: InsertXML với một đoạn văn trọn vẹn có thể sinh thêm một dấu hết đoạn trước hoặc sau equation.
        /// Nhận ra bằng cách so vài ký tự quanh vị trí chèn trước và sau khi chèn (không đếm đoạn của cả story — chậm
        /// với tài liệu lớn): nếu sau equation là "\r" + đúng những gì vốn đứng sau vùng bị thay, "\r" đó là thừa.
        /// </para>
        /// </summary>
        private static int InsertMath(dynamic anchor, dynamic target, string flatOpc, bool display)
        {
            int start = target.Start, oldEnd = target.End;
            string after0 = TextNear(anchor, oldEnd, oldEnd + 3);
            string before0 = TextNear(anchor, start - 3, start);
            target.InsertXML(flatOpc);

            dynamic? om = FindEquationNear(anchor, start);
            if (om is null) return -1;
            int eqStart = om.Range.Start, eqEnd = om.Range.End;

            if (TextNear(anchor, eqEnd, eqEnd + after0.Length + 1) == "\r" + after0)
            {
                RangeAt(anchor, eqEnd, eqEnd + 1).Delete();
                AddinLog.Info($"InsertXML sinh thêm một dấu hết đoạn sau equation ({(display ? "display" : "inline")}) — đã bỏ");
            }
            if (eqStart > start && TextNear(anchor, eqStart - before0.Length - 1, eqStart) == before0 + "\r")
            {
                RangeAt(anchor, eqStart - 1, eqStart).Delete();
                AddinLog.Info($"InsertXML sinh thêm một dấu hết đoạn trước equation ({(display ? "display" : "inline")}) — đã bỏ");
                eqEnd--;
            }
            return eqEnd;
        }

        /// <summary>Text của [start, end) đã kẹp vào trong story (đầu/cuối story có thể ngắn hơn yêu cầu).</summary>
        private static string TextNear(dynamic anchor, int start, int end)
        {
            int s = Math.Max(0, start);
            if (end <= s) return "";
            dynamic r = RangeAt(anchor, s, end);
            return (int)r.Start == s ? (r.Text as string) ?? "" : "";
        }

        /// <summary>Range con [start, end) trong cùng story với <paramref name="anchor"/> (Document.Range chỉ dùng được cho main story).</summary>
        private static dynamic RangeAt(dynamic anchor, int start, int end)
        {
            dynamic r = anchor.Duplicate;
            r.SetRange(start, end);
            return r;
        }

        private void SaveMetadata(dynamic doc, dynamic anchor, int start, EditResult result)
        {
            try
            {
                dynamic? om = FindEquationNear(anchor, start);
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

        /// <summary>Equation vừa chèn tại <paramref name="start"/> (hoặc ngay sau một dấu hết đoạn thừa).</summary>
        private static dynamic? FindEquationNear(dynamic anchor, int start)
        {
            try
            {
                dynamic probe = RangeAt(anchor, start, start + 2);
                return (int)probe.OMaths.Count > 0 ? probe.OMaths.Item(1) : null;
            }
            catch (Exception ex)
            {
                AddinLog.Error("FindEquationNear", ex);
                return null;
            }
        }
    }
}
