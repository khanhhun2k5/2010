using System;
using System.Diagnostics;
using System.IO;

namespace MathTypeX.WordAddin
{
    /// <summary>Nhật ký cục bộ: %LOCALAPPDATA%\MathTypeX\logs\word-addin.log (không gửi đi đâu).</summary>
    internal static class AddinLog
    {
        private static readonly object Gate = new();

        public static string LogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MathTypeX", "logs", "word-addin.log");

        public static void Info(string message) => Write("INFO", message);

        public static void Error(string where, Exception ex) => Write("ERROR", $"{where}: {ex}");

        private static void Write(string level, string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                    File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
                }
            }
            catch (IOException)
            {
                // Không bao giờ để việc ghi log làm hỏng Word.
            }
        }

        public static void OpenInNotepad()
        {
            if (File.Exists(LogPath)) Process.Start("notepad.exe", "\"" + LogPath + "\"");
        }
    }
}
