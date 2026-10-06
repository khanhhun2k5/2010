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
        public bool Stopped;
        public readonly List<string> LowConfidence = new();
        public readonly List<string> Problems = new();

        public string Summary => Found == 0
            ? "MathTypeX: không thấy công thức LaTeX ($…$, $$…$$, \\(…\\), \\[…\\])."
            : $"MathTypeX: đã chuyển {Converted}/{Found} công thức{(Stopped ? " (đã dừng theo yêu cầu)" : "")}.";

        public bool NeedsAttention => Found == 0 || Stopped || LowConfidence.Count > 0 || Problems.Count > 0;

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

    /// <summary>Text của một story (hoặc một phần story) đã đọc, cùng Range neo để dựng Range con.</summary>
    internal sealed class StoryText
    {
        public dynamic Anchor = null!;
        public int BaseStart;
        public string Text = "";
        public string Location = "";
        public IReadOnlyList<MathCandidate> Candidates = Array.Empty<MathCandidate>();
    }

    /// <summary>Một ứng viên khi quét cả tài liệu.</summary>
    internal sealed class ScanEntry
    {
        public int Id;
        public StoryText Story = null!;
        public MathCandidate Candidate = null!;
        public string? Blocked;
        public string Source => Story.Text.Substring(Candidate.Start, Candidate.Length);
    }

    internal sealed class DocumentScan
    {
        public dynamic Document = null!;
        public string Name = "";
        public readonly List<StoryText> Stories = new();
        public readonly List<ScanEntry> Entries = new();
        public int IgnoredCount;
    }

    internal sealed partial class WordGateway
    {
        private const int VkEscape = 0x1B;

        private static readonly string[] MonospaceFonts =
        {
            "Consolas", "Courier New", "Courier", "Cascadia Code", "Cascadia Mono", "Lucida Console", "Lucida Sans Typewriter",
            "Source Code Pro", "Fira Code", "Fira Mono", "JetBrains Mono", "Menlo", "Monaco", "DejaVu Sans Mono", "Liberation Mono",
        };

        // ── Convert Selection (VS-8) ─────────────────────────────────────────

        /// <summary>
        /// Tìm $…$, $$…$$, \(…\), \[…\], equation*/align* trong vùng chọn (không chọn gì thì lấy đoạn chứa con trỏ)
        /// và thay bằng Word Equation gốc — tất cả trong một mục Undo (docs/05 §9.4).
        /// </summary>
        public ConversionReport ConvertSelection(UserSettings settings)
        {
            dynamic selection = _app.Selection;
            dynamic anchor = selection.Range;
            dynamic doc = anchor.Document;
            EnsureCompatible(doc);

            int selStart = anchor.Start, selEnd = anchor.End;
            // Đọc trọn các đoạn chứa vùng chọn: cần chữ trước/sau công thức để quyết định có tách đoạn cho display không.
            dynamic firstParagraph = RangeAt(anchor, selStart, selStart).Paragraphs.Item(1).Range;
            dynamic lastParagraph = RangeAt(anchor, selEnd, selEnd).Paragraphs.Item(1).Range;
            int baseStart = firstParagraph.Start;
            int scopeEnd = Math.Max((int)lastParagraph.End, (int)firstParagraph.End);
            StoryText story = ReadStory(RangeAt(anchor, baseStart, scopeEnd), "vùng chọn", settings);

            int windowStart = (selStart == selEnd ? (int)firstParagraph.Start : selStart) - baseStart;
            int windowEnd = (selStart == selEnd ? (int)firstParagraph.End : selEnd) - baseStart;

            var report = new ConversionReport();
            var include = new HashSet<int>();
            foreach (var c in story.Candidates.Where(c => c.Start >= windowStart && c.End <= windowEnd))
            {
                string source = story.Text.Substring(c.Start, c.Length);
                if (settings.IsIgnored(source)) continue;
                report.Found++;
                if (!c.Recommended)
                {
                    report.LowConfidence.Add($"{Shorten(source)} — {string.Join(", ", c.Reasons.DefaultIfEmpty("độ tin cậy thấp"))}");
                    continue;
                }
                string? blocked = SkipReason(RangeAt(anchor, baseStart + c.Start, baseStart + c.End));
                if (blocked is not null) report.Problems.Add($"{Shorten(source)} — {blocked}");
                else include.Add(c.Start);
            }

            var plan = ConversionPlanner.Plan(story.Text, windowStart, windowEnd, ScannerOptionsFor(settings), c => include.Contains(c.Start));
            if (plan.Items.Count == 0)
            {
                LogReport("Convert Selection", report);
                return report;
            }
            dynamic undo = _app.UndoRecord;
            var records = new List<EquationRecord>();
            undo.StartCustomRecord("MathTypeX: chuyển LaTeX");
            try
            {
                ApplyPlan(story, plan, settings, report, records, progress: null);
            }
            finally
            {
                undo.EndCustomRecord();
                // Kể cả khi giữa chừng có lỗi: công thức đã chèn vẫn phải có metadata (source LaTeX gốc).
                SaveRecords(doc, records);
            }
            LogReport("Convert Selection", report);
            return report;
        }

        // ── Convert Document (VS-9) ──────────────────────────────────────────

        /// <summary>Quét mọi story của tài liệu: thân bài, chú thích, header/footer, hộp văn bản (bỏ qua comment).</summary>
        public DocumentScan ScanDocument(UserSettings settings)
        {
            dynamic doc = _app.ActiveDocument;
            EnsureCompatible(doc);
            var scan = new DocumentScan { Document = doc, Name = (doc.Name as string) ?? "" };
            var seenHeaders = new HashSet<(string, string)>();

            IEnumerable<(dynamic Range, string Location)> stories = Stories(doc);
            foreach (var (range, location) in stories)
            {
                StoryText story = ReadStory(range, location, settings);
                if (story.Candidates.Count == 0) continue;
                // Header/footer liên kết với section trước xuất hiện nhiều lần với cùng nội dung — chỉ lấy một lần.
                if (location is "Header" or "Footer" && !seenHeaders.Add((location, story.Text))) continue;
                scan.Stories.Add(story);

                foreach (var c in story.Candidates)
                {
                    string source = story.Text.Substring(c.Start, c.Length);
                    if (settings.IsIgnored(source))
                    {
                        scan.IgnoredCount++;
                        continue;
                    }
                    scan.Entries.Add(new ScanEntry
                    {
                        Id = scan.Entries.Count + 1,
                        Story = story,
                        Candidate = c,
                        Blocked = SkipReason(RangeAt(story.Anchor, story.BaseStart + c.Start, story.BaseStart + c.End)),
                    });
                }
            }
            AddinLog.Info($"Scan Document: {scan.Stories.Count} story có công thức, {scan.Entries.Count} ứng viên, {scan.IgnoredCount} trong danh sách bỏ qua");
            return scan;
        }

        public static ScanRequest ToReviewRequest(DocumentScan scan, UserSettings settings, IntPtr owner) => new()
        {
            DocumentName = scan.Name,
            MathFont = settings.MathFont,
            OwnerWindow = owner.ToInt64(),
            Items = scan.Entries.Select(e => new ScanItem
            {
                Id = e.Id,
                Source = e.Source,
                Latex = ConversionPlanner.LatexFor(e.Candidate),
                Display = e.Candidate.Display,
                Confidence = e.Candidate.Confidence,
                Recommended = e.Candidate.Recommended && e.Blocked is null,
                Reasons = e.Candidate.Reasons.ToArray(),
                Blocked = e.Blocked,
                Location = e.Story.Location,
                Context = ConversionPlanner.ContextOf(e.Story.Text, e.Candidate.Start, e.Candidate.End),
            }).ToArray(),
        };

        /// <summary>
        /// Chuyển các mục đã chọn trong một mục Undo. Thanh trạng thái báo tiến độ; giữ Esc để dừng
        /// (phần đã chuyển vẫn nằm trong cùng mục Undo nên một lần Ctrl+Z vẫn hoàn tác hết).
        /// </summary>
        public ConversionReport ApplyDocument(DocumentScan scan, ICollection<int> selectedIds, UserSettings settings)
        {
            var report = new ConversionReport { Found = selectedIds.Count };
            var records = new List<EquationRecord>();
            int total = selectedIds.Count, done = 0;
            bool screenUpdating = _app.ScreenUpdating;
            dynamic undo = _app.UndoRecord;
            undo.StartCustomRecord("MathTypeX: chuyển LaTeX trong tài liệu");
            try
            {
                _app.ScreenUpdating = false;
                foreach (var story in scan.Stories)
                {
                    var selectedStarts = scan.Entries.Where(e => e.Story == story && selectedIds.Contains(e.Id) && e.Blocked is null)
                        .Select(e => e.Candidate.Start).ToList();
                    if (selectedStarts.Count == 0) continue;

                    // Người dùng có thể đã sửa tài liệu trong lúc duyệt: đọc lại và ánh xạ các mục đã chọn.
                    StoryText fresh = ReadStory(story.Anchor, story.Location, settings);
                    var include = ConversionPlanner.Remap(story.Text, story.Candidates, selectedStarts, fresh.Text, fresh.Candidates);
                    if (include.Count < selectedStarts.Count)
                        report.Problems.Add($"{story.Location}: {selectedStarts.Count - include.Count} công thức không còn ở chỗ cũ (tài liệu đã sửa trong lúc duyệt)");

                    var plan = ConversionPlanner.Plan(fresh.Text, 0, fresh.Text.Length, ScannerOptionsFor(settings), c => include.Contains(c.Start));
                    bool keepGoing = ApplyPlan(fresh, plan, settings, report, records, () =>
                    {
                        done++;
                        if (done % 5 == 0 || done == total) SetStatus($"MathTypeX: đang chuyển {done}/{total} công thức… (giữ Esc để dừng)");
                        return (NativeMethods.GetAsyncKeyState(VkEscape) & 0x8000) == 0;
                    });
                    if (!keepGoing)
                    {
                        report.Stopped = true;
                        break;
                    }
                }
            }
            finally
            {
                _app.ScreenUpdating = screenUpdating;
                undo.EndCustomRecord();
                SaveRecords(scan.Document, records);
            }
            LogReport("Convert Document", report);
            return report;
        }

        /// <summary>
        /// Áp dụng một kế hoạch trên một story, từ cuối lên đầu để vị trí của các mục phía trước không bị xê dịch.
        /// Trả về false nếu <paramref name="progress"/> yêu cầu dừng.
        /// </summary>
        private static bool ApplyPlan(StoryText story, ConversionPlan plan, UserSettings settings, ConversionReport report, List<EquationRecord> records, Func<bool>? progress)
        {
            dynamic anchor = story.Anchor;
            for (int k = plan.Items.Count - 1; k >= 0; k--)
            {
                var item = plan.Items[k];
                try
                {
                    ApplyItem(anchor, story, item, settings, report, records);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Một mục lỗi (vùng bị khoá, nội dung được bảo vệ, lỗi COM…) không được bỏ dở cả lô.
                    AddinLog.Error("Convert: " + item.SourceText, ex);
                    report.Problems.Add($"{Shorten(item.SourceText)} — {ex.Message}");
                }
                if (progress is not null && !progress()) return false;
            }
            return true;
        }

        private static void ApplyItem(dynamic anchor, StoryText story, ConversionItem item, UserSettings settings, ConversionReport report, List<EquationRecord> records)
        {
            int rs = story.BaseStart + item.ReplaceStart, re = story.BaseStart + item.ReplaceEnd;
            dynamic source = RangeAt(anchor, story.BaseStart + item.Candidate.Start, story.BaseStart + item.Candidate.End);
            EditResult? result = Compose(item, source, settings, out string? error);
            if (result is null)
            {
                report.Problems.Add($"{Shorten(item.SourceText)} — {error}");
                return;
            }
            if (ReadText(RangeAt(anchor, rs, re)) != item.ExpectedText)
            {
                report.Problems.Add($"{Shorten(item.SourceText)} — văn bản đã thay đổi trong lúc chuyển");
                return;
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
                return;
            }
            report.Converted++;
            records.Add(CreateRecord(item, result));
        }

        private static EditResult? Compose(ConversionItem item, dynamic source, UserSettings settings, out string? error)
        {
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
            error = outcome.BlockingMessage;
            return outcome.Result;
        }

        // ── Revert to LaTeX text (VS-9) ──────────────────────────────────────

        /// <summary>
        /// Thay các equation trong vùng chọn (hoặc equation tại con trỏ) bằng văn bản LaTeX: đúng văn bản gốc nếu công thức
        /// được tạo bằng Convert LaTeX, nếu không thì $…$ / $$…$$ quanh source đã lưu (hoặc bản chuyển ngược).
        /// </summary>
        public int RevertSelection()
        {
            dynamic selection = _app.Selection;
            dynamic anchor = selection.Range;
            dynamic doc = anchor.Document;
            int count = (int)anchor.OMaths.Count;
            if (count == 0) return 0;

            var store = ReadDocumentStore(doc);
            var targets = new List<(int Start, int End, string Text)>();
            for (int i = 1; i <= count; i++)
            {
                dynamic om = anchor.OMaths.Item(i);
                dynamic omRange = om.Range;
                var element = OmmlExtractor.FirstEquation((string)omRange.WordOpenXML);
                if (element is null) continue;
                var reverse = OmmlToLatex.Convert(element);
                string key = EquationIdentity.KeyOfNormalized(reverse.NormalizedLatex);
                var record = store.FindByKey(key) ?? _local.FindByKey(key);
                bool display = (int)om.Type == WdOMathDisplay || reverse.Display;
                string latex = record?.OriginalLatex ?? reverse.NormalizedLatex;
                string text = record?.SourceText ?? (display ? "$$" + latex + "$$" : "$" + latex + "$");
                targets.Add(((int)omRange.Start, (int)omRange.End, ConversionPlanner.NormalizeWordText(text)));
            }

            dynamic undo = _app.UndoRecord;
            undo.StartCustomRecord("MathTypeX: trả về LaTeX");
            try
            {
                foreach (var (start, end, text) in targets.OrderByDescending(t => t.Start))
                    RangeAt(anchor, start, end).InsertXML(MathTypeX.Render.Omml.FlatOpc.ForText(text));
            }
            finally
            {
                undo.EndCustomRecord();
            }
            AddinLog.Info($"Revert: {targets.Count} công thức → văn bản LaTeX");
            return targets.Count;
        }

        // ── Dùng chung ───────────────────────────────────────────────────────

        private static void EnsureCompatible(dynamic doc)
        {
            if ((int)doc.CompatibilityMode < WdWord2007CompatibilityMode)
                throw new InvalidOperationException("Tài liệu đang ở chế độ tương thích Word 97–2003 nên không chèn được Word Equation. Hãy dùng File → Info → Convert.");
        }

        private static ScannerOptions ScannerOptionsFor(UserSettings settings) => new()
        {
            MinConfidence = settings.ConvertMinConfidence,
            SingleDollar = settings.ConvertSingleDollar,
        };

        private static StoryText ReadStory(dynamic range, string location, UserSettings settings)
        {
            int start = range.Start, end = range.End;
            string text = ReadText(range);
            if (text.Length != end - start)
                AddinLog.Info($"{location}: độ dài text {text.Length} khác số ký tự {end - start} — sẽ kiểm tra lại từng công thức trước khi thay");
            return new StoryText
            {
                Anchor = range,
                BaseStart = start,
                Text = text,
                Location = location,
                Candidates = LatexScanner.Scan(text, ScannerOptionsFor(settings)),
            };
        }

        /// <summary>Các story cần quét, theo WdStoryType; mỗi loại có thể có nhiều Range nối bằng NextStoryRange.</summary>
        private static IEnumerable<(dynamic Range, string Location)> Stories(dynamic doc)
        {
            foreach (dynamic story in doc.StoryRanges)
            {
                string? location = StoryLabel((int)story.StoryType);
                if (location is null) continue;
                dynamic? range = story;
                while (range is not null)
                {
                    yield return (range, location);
                    range = range.NextStoryRange;
                }
            }
        }

        private static string? StoryLabel(int storyType) => storyType switch
        {
            1 => "Thân bài",
            2 => "Chú thích cuối trang",
            3 => "Chú thích cuối tài liệu",
            5 => "Hộp văn bản",
            6 or 7 or 10 => "Header",
            8 or 9 or 11 => "Footer",
            _ => null, // comment, các dấu ngăn chú thích
        };

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
            // Dấu hết ô/hàng của bảng chiếm MỘT vị trí trong tài liệu nhưng Range.Text trả về hai ký tự "\r\a":
            // thu về "\a" để vị trí trong chuỗi khớp vị trí Range (nếu không, mọi công thức sau bảng đều lệch).
            return ((range.Text as string) ?? "").Replace("\r\a", "\a");
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

        private static void LogReport(string what, ConversionReport report)
        {
            AddinLog.Info($"{what}: tìm thấy {report.Found}, đã chuyển {report.Converted}, độ tin cậy thấp {report.LowConfidence.Count}, lỗi {report.Problems.Count}{(report.Stopped ? ", đã dừng" : "")}");
            foreach (var problem in report.Problems) AddinLog.Info($"{what}: bỏ qua " + problem);
        }

        private static string Shorten(string source)
        {
            string oneLine = ConversionPlanner.NormalizeWordText(source).Trim();
            return oneLine.Length <= 60 ? oneLine : oneLine.Substring(0, 57) + "…";
        }
    }
}
