using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MathTypeX.Documents;
using MathTypeX.Editing;
using MathTypeX.Editing.Conversion;
using MathTypeX.Interop;
using MathTypeX.Scanning;

namespace MathTypeX.WordAddin
{
    /// <summary>Kết quả một lần Convert LaTeX: số công thức đã chuyển và những gì bị bỏ qua (kèm lý do).</summary>
    internal sealed class ConversionReport
    {
        public int Found;
        public int Converted;
        public readonly List<string> LowConfidence = new();
        public readonly List<string> Problems = new();

        public string Summary => Found == 0
            ? "MathTypeX: không thấy công thức LaTeX ($…$, $$…$$, \\(…\\), \\[…\\]) trong vùng chọn."
            : $"MathTypeX: đã chuyển {Converted}/{Found} công thức.";

        public string Details()
        {
            var sb = new StringBuilder(Summary);
            Append(sb, "Chưa chuyển vì không chắc là công thức (chọn riêng rồi nhấn Alt+M nếu đúng là công thức):", LowConfidence);
            Append(sb, "Không chuyển được:", Problems);
            return sb.ToString();
        }

        private static void Append(StringBuilder sb, string title, List<string> lines)
        {
            if (lines.Count == 0) return;
            sb.AppendLine().AppendLine().AppendLine(title);
            foreach (var line in lines.Take(12)) sb.Append("  • ").AppendLine(line);
            if (lines.Count > 12) sb.AppendLine($"  … và {lines.Count - 12} mục khác (xem Nhật ký).");
        }
    }

    internal sealed partial class WordGateway
    {
        private static readonly string[] MonospaceFonts =
        {
            "Consolas", "Courier New", "Courier", "Cascadia Code", "Cascadia Mono", "Lucida Console", "Lucida Sans Typewriter",
            "Source Code Pro", "Fira Code", "Fira Mono", "JetBrains Mono", "Menlo", "Monaco", "DejaVu Sans Mono", "Liberation Mono",
        };

        /// <summary>
        /// Convert Selection (docs/05 §9.4): tìm $…$, $$…$$, \(…\), \[…\], equation*/align* trong vùng chọn
        /// (không chọn gì thì lấy đoạn chứa con trỏ), thay bằng Word Equation gốc — tất cả trong một mục Undo.
        /// </summary>
        public ConversionReport ConvertSelection(UserSettings settings)
        {
            dynamic selection = _app.Selection;
            dynamic anchor = selection.Range;
            dynamic doc = anchor.Document;
            if ((int)doc.CompatibilityMode < WdWord2007CompatibilityMode)
                throw new InvalidOperationException("Tài liệu đang ở chế độ tương thích Word 97–2003 nên không chèn được Word Equation. Hãy dùng File → Info → Convert.");

            int selStart = anchor.Start, selEnd = anchor.End;
            // Đọc trọn các đoạn chứa vùng chọn: cần chữ trước/sau công thức để quyết định có tách đoạn cho display không.
            dynamic firstParagraph = RangeAt(anchor, selStart, selStart).Paragraphs.Item(1).Range;
            dynamic lastParagraph = RangeAt(anchor, selEnd, selEnd).Paragraphs.Item(1).Range;
            int baseStart = firstParagraph.Start;
            int scopeEnd = Math.Max((int)lastParagraph.End, (int)firstParagraph.End);
            string text = ReadText(RangeAt(anchor, baseStart, scopeEnd));
            if (text.Length != scopeEnd - baseStart)
                AddinLog.Info($"Convert: độ dài text {text.Length} khác số ký tự {scopeEnd - baseStart} — sẽ kiểm tra lại từng công thức trước khi thay");

            int windowStart = (selStart == selEnd ? (int)firstParagraph.Start : selStart) - baseStart;
            int windowEnd = (selStart == selEnd ? (int)firstParagraph.End : selEnd) - baseStart;
            var plan = ConversionPlanner.Plan(text, windowStart, windowEnd, new ScannerOptions
            {
                MinConfidence = settings.ConvertMinConfidence,
                SingleDollar = settings.ConvertSingleDollar,
            });

            var report = new ConversionReport { Found = plan.Items.Count + plan.LowConfidence.Count };
            foreach (var low in plan.LowConfidence)
                report.LowConfidence.Add($"{Shorten(text.Substring(low.Start, low.Length))} — {string.Join(", ", low.Reasons.Where(r => r.Length > 0).DefaultIfEmpty("độ tin cậy thấp"))}");

            // Soạn OMML trước (không đụng tài liệu), chỉ mở UndoRecord khi đã có danh sách sẵn sàng.
            var ready = new List<(ConversionItem Item, EditResult Result)>();
            foreach (var item in plan.Items)
            {
                dynamic source = RangeAt(anchor, baseStart + item.Candidate.Start, baseStart + item.Candidate.End);
                string? skip = SkipReason(source);
                if (skip is not null)
                {
                    report.Problems.Add($"{Shorten(item.SourceText)} — {skip}");
                    continue;
                }

                double size = Convert.ToDouble(source.Font.Size);
                string? font = source.Font.Name as string;
                var outcome = EquationComposer.Compose(item.Latex, new ComposeOptions
                {
                    MathFont = settings.MathFont,
                    TextFont = string.IsNullOrEmpty(font) ? "Times New Roman" : font!,
                    Display = item.Display,
                    FontSizePt = size > 0 && size < WdUndefined ? size : null,
                    DecimalComma = settings.DecimalComma,
                    UprightDifferential = settings.UprightDifferential,
                    NarySizing = settings.NarySizing,
                }, UiLanguage.Vi, allowEmptySlots: true, stripDelimiters: false);

                if (outcome.Result is null)
                    report.Problems.Add($"{Shorten(item.SourceText)} — {outcome.BlockingMessage}");
                else
                    ready.Add((item, outcome.Result));
            }

            if (ready.Count == 0) return report;

            var records = new List<EquationRecord>();
            dynamic undo = _app.UndoRecord;
            bool screenUpdating = _app.ScreenUpdating;
            undo.StartCustomRecord("MathTypeX: chuyển LaTeX");
            try
            {
                _app.ScreenUpdating = false;
                // Từ cuối lên đầu để vị trí của các mục phía trước không bị xê dịch.
                for (int k = ready.Count - 1; k >= 0; k--)
                {
                    var (item, result) = ready[k];
                    int rs = baseStart + item.ReplaceStart, re = baseStart + item.ReplaceEnd;
                    if (ReadText(RangeAt(anchor, rs, re)) != item.ExpectedText)
                    {
                        report.Problems.Add($"{Shorten(item.SourceText)} — văn bản đã thay đổi trong lúc chuyển");
                        continue;
                    }

                    if (item.BreakAfter) RangeAt(anchor, re, re).InsertParagraphAfter();
                    if (item.BreakBefore)
                    {
                        RangeAt(anchor, rs, rs).InsertParagraphAfter();
                        rs++;
                        re++;
                    }

                    int end = InsertMath(anchor, RangeAt(anchor, rs, re), result.FlatOpc, item.Display);
                    if (end < 0)
                    {
                        report.Problems.Add($"{Shorten(item.SourceText)} — Word không nhận equation (xem Nhật ký)");
                        continue;
                    }
                    report.Converted++;
                    records.Add(CreateRecord(item, result));
                }
            }
            finally
            {
                _app.ScreenUpdating = screenUpdating;
                undo.EndCustomRecord();
            }

            SaveRecords(doc, records);
            AddinLog.Info($"Convert: tìm thấy {report.Found}, đã chuyển {report.Converted}, độ tin cậy thấp {report.LowConfidence.Count}, lỗi {report.Problems.Count}");
            foreach (var problem in report.Problems) AddinLog.Info("Convert: bỏ qua " + problem);
            return report;
        }

        public void SetStatus(string text)
        {
            try
            {
                _app.StatusBar = text;
            }
            catch (Exception ex)
            {
                AddinLog.Error("StatusBar", ex);
            }
        }

        /// <summary>
        /// Đọc text kèm chữ ẩn và mã field để vị trí trong chuỗi khớp vị trí ký tự của Range (docs/05 §9.4 bước 1).
        /// </summary>
        private static string ReadText(dynamic range)
        {
            dynamic mode = range.TextRetrievalMode;
            mode.IncludeHiddenText = true;
            mode.IncludeFieldCodes = true;
            return (range.Text as string) ?? "";
        }

        /// <summary>Không chuyển: đã nằm trong equation, chữ ẩn, định dạng code hoặc style "MTX Ignore" (docs/05 §9.4 bảng quy tắc).</summary>
        private static string? SkipReason(dynamic range)
        {
            try
            {
                if ((int)range.OMaths.Count > 0) return "nằm trong một công thức có sẵn";
            }
            catch (Exception ex)
            {
                AddinLog.Error("SkipReason.OMaths", ex);
            }
            try
            {
                if ((int)range.Font.Hidden == -1) return "là chữ ẩn";
                string font = range.Font.Name as string ?? "";
                if (MonospaceFonts.Any(f => string.Equals(f, font, StringComparison.OrdinalIgnoreCase))) return $"dùng font code ({font})";
            }
            catch (Exception ex)
            {
                AddinLog.Error("SkipReason.Font", ex);
            }
            foreach (string style in StyleNames(range))
            {
                if (style.IndexOf("MTX Ignore", StringComparison.OrdinalIgnoreCase) >= 0) return "style MTX Ignore";
                if (style.IndexOf("Code", StringComparison.OrdinalIgnoreCase) >= 0 || style.IndexOf("HTML", StringComparison.OrdinalIgnoreCase) >= 0)
                    return $"style code ({style})";
            }
            return null;
        }

        private static IEnumerable<string> StyleNames(dynamic range)
        {
            var names = new List<string>();
            try
            {
                if (range.ParagraphFormat.Style is object paragraphStyle) names.Add((string)((dynamic)paragraphStyle).NameLocal);
            }
            catch (Exception)
            {
                // Vùng có nhiều style khác nhau: bỏ qua.
            }
            try
            {
                if (range.CharacterStyle is object characterStyle) names.Add((string)((dynamic)characterStyle).NameLocal);
            }
            catch (Exception)
            {
                // CharacterStyle chỉ có từ Word 2013.
            }
            return names;
        }

        private static EquationRecord CreateRecord(ConversionItem item, EditResult result)
        {
            // Khoá tính từ OMML của chính MathTypeX: khoá chuẩn hoá bền trước cách Word ghi lại XML (VS-4).
            var element = OmmlExtractor.FirstEquation(result.FlatOpc);
            string key = element is null ? "" : EquationIdentity.KeyOfNormalized(OmmlToLatex.Convert(element).NormalizedLatex);
            var record = EquationRecord.Create(key, result.Latex, result.NormalizedLatex, result.Display, result.MathFont);
            record.SourceText = item.SourceText;
            return record;
        }

        private void SaveRecords(dynamic doc, List<EquationRecord> records)
        {
            if (records.Count == 0) return;
            try
            {
                var store = ReadDocumentStore(doc);
                foreach (var record in records.Where(r => r.Key.Length > 0))
                {
                    store.Upsert(record);
                    _local.Save(record);
                }
                WriteDocumentStore(doc, store);
            }
            catch (Exception ex)
            {
                // Metadata hỏng không làm hỏng công thức đã chèn; lần sau vẫn chuyển ngược được.
                AddinLog.Error("SaveRecords", ex);
            }
        }

        private static string Shorten(string source)
        {
            string oneLine = ConversionPlanner.NormalizeWordText(source).Trim();
            return oneLine.Length <= 60 ? oneLine : oneLine.Substring(0, 57) + "…";
        }
    }
}
