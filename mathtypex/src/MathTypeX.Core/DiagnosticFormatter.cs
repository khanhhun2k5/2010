namespace MathTypeX;

public enum UiLanguage
{
    Vi,
    En,
}

/// <summary>Sinh thông báo lỗi thân thiện (Beginner) theo ngôn ngữ giao diện.</summary>
public static class DiagnosticFormatter
{
    public static string Format(Diagnostic d, UiLanguage lang)
    {
        bool vi = lang == UiLanguage.Vi;
        string role = d.Role is null ? "" : (vi ? d.Role.Vi : d.Role.En);
        string cmd = d.Command ?? "";
        string a0 = d.Arguments.Count > 0 ? d.Arguments[0] : "";
        string a1 = d.Arguments.Count > 1 ? d.Arguments[1] : "";

        switch (d.Code)
        {
            case DiagnosticCode.MissingCloseBrace:
                return d.Role is null
                    ? (vi ? "Thiếu dấu } để đóng nhóm { }." : "Missing } to close the group.")
                    : (vi ? $"Bạn đang thiếu dấu }} để kết thúc {role}." : $"Missing }} to close the {role}.");
            case DiagnosticCode.ExtraCloseBrace:
                return vi ? "Có một dấu } thừa." : "Unexpected }.";
            case DiagnosticCode.MissingArgument:
                return vi
                    ? $"{(cmd.Length > 0 ? cmd : "Lệnh")} cần {role} nhưng bạn chưa nhập."
                    : $"{(cmd.Length > 0 ? cmd : "Command")} is missing its {role}.";
            case DiagnosticCode.UnknownCommand:
                if (d.Arguments.Count > 0)
                {
                    string suggestions = string.Join(vi ? " hoặc " : " or ", d.Arguments.Select(s => "\\" + s));
                    return vi
                        ? $"Không có lệnh {cmd}. Có phải bạn muốn {suggestions}?"
                        : $"Unknown command {cmd}. Did you mean {suggestions}?";
                }
                return vi ? $"Không có lệnh {cmd}." : $"Unknown command {cmd}.";
            case DiagnosticCode.UnmatchedLeft:
                return vi
                    ? $"Ngoặc \\left{a0} chưa có \\right tương ứng."
                    : $"\\left{a0} has no matching \\right.";
            case DiagnosticCode.UnmatchedRight:
                return vi ? "\\right không có \\left tương ứng." : "\\right has no matching \\left.";
            case DiagnosticCode.MissingDelimiter:
                return vi
                    ? $"Sau {cmd} cần một dấu ngoặc, ví dụ ( [ \\{{ | hoặc dấu chấm ."
                    : $"{cmd} must be followed by a delimiter such as ( [ \\{{ | or .";
            case DiagnosticCode.DoubleSuperscript:
                return vi
                    ? "Hai số mũ liên tiếp. Hãy viết x^{a^b} hoặc {x^a}^b."
                    : "Double superscript. Write x^{a^b} or {x^a}^b.";
            case DiagnosticCode.DoubleSubscript:
                return vi
                    ? "Hai chỉ số dưới liên tiếp. Hãy viết x_{a_b} hoặc {x_a}_b."
                    : "Double subscript. Write x_{a_b} or {x_a}_b.";
            case DiagnosticCode.MisplacedAlignment:
                return vi ? "Dấu & chỉ dùng trong ma trận hoặc môi trường căn dòng (aligned, cases…)." : "& is only allowed inside matrices and alignment environments.";
            case DiagnosticCode.MisplacedRowSeparator:
                return vi ? "\\\\ (xuống dòng) chỉ dùng trong ma trận hoặc môi trường nhiều dòng." : "\\\\ is only allowed inside matrices and multi-line environments.";
            case DiagnosticCode.UnsupportedCaretNotation:
                return vi
                    ? "Ký pháp ^^ không được hỗ trợ. Nếu muốn số mũ của số mũ, hãy viết x^{a^b}."
                    : "The ^^ notation is not supported. For nested superscripts write x^{a^b}.";
            case DiagnosticCode.UnexpectedMathShift:
                return vi ? "Không cần dấu $ trong ô công thức." : "No $ is needed inside the formula box.";
            case DiagnosticCode.UnexpectedParameter:
                return vi ? "Dấu # chỉ dùng khi định nghĩa macro." : "# is only allowed in macro definitions.";
            case DiagnosticCode.IncompleteCommand:
                return vi ? "Dấu \\ ở cuối chưa thành lệnh." : "Trailing \\ is not a command.";
            case DiagnosticCode.MissingEnvironmentEnd:
                return vi ? $"Thiếu \\end{{{a0}}}." : $"Missing \\end{{{a0}}}.";
            case DiagnosticCode.MismatchedEnvironmentEnd:
                return vi ? $"\\begin{{{a0}}} lại kết thúc bằng \\end{{{a1}}}." : $"\\begin{{{a0}}} ended by \\end{{{a1}}}.";
            case DiagnosticCode.UnknownEnvironment:
                return vi ? $"Chưa hỗ trợ môi trường {a0}." : $"Environment {a0} is not supported.";
            case DiagnosticCode.MisplacedEnd:
                return vi ? $"\\end{{{a0}}} không có \\begin tương ứng." : $"\\end{{{a0}}} has no matching \\begin.";
            case DiagnosticCode.MissingCloseBracket:
                return d.Role is null
                    ? (vi ? "Thiếu dấu ] để đóng đối số tuỳ chọn." : "Missing ] to close the optional argument.")
                    : (vi ? $"Thiếu dấu ] để kết thúc {role}." : $"Missing ] to close the {role}.");
            case DiagnosticCode.UnsupportedCommand:
                return vi ? $"Lệnh {cmd} chưa được hỗ trợ." : $"Command {cmd} is not supported yet.";
            case DiagnosticCode.VietnameseImeMangled:
                return vi
                    ? $"Bộ gõ tiếng Việt đã đổi \\{a1} thành \\{a0}."
                    : $"The Vietnamese input method turned \\{a1} into \\{a0}.";
            case DiagnosticCode.NestingTooDeep:
                return vi ? "Công thức lồng nhau quá sâu." : "The formula is nested too deeply.";
            case DiagnosticCode.MisplacedLimits:
                return vi ? $"{cmd} phải đứng ngay sau một toán tử lớn như \\sum, \\int hoặc \\lim." : $"{cmd} must directly follow a large operator such as \\sum, \\int or \\lim.";
            case DiagnosticCode.ApproximateRendering:
                return vi ? $"Word Equation chỉ hiển thị gần đúng: {a0}." : $"Word Equation can only approximate: {a0}.";
            default:
                return d.Code.ToString();
        }
    }
}
