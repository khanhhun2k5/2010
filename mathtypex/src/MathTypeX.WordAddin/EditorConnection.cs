using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;
using MathTypeX.Interop;
using Microsoft.Win32;

namespace MathTypeX.WordAddin
{
    /// <summary>Kết nối tới tiến trình MathTypeX.Editor.exe (tự khởi động nếu chưa chạy) — docs/02 §5.1.</summary>
    internal sealed class EditorConnection : IDisposable
    {
        private NamedPipeClientStream? _pipe;
        private RpcClient? _client;

        public async Task<RpcClient> GetClientAsync()
        {
            if (_client is not null && _pipe is { IsConnected: true }) return _client;
            Reset();

            string pipeName = EditorProtocol.PipeName(Environment.UserName);
            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(300).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                StartEditorProcess();
                await pipe.ConnectAsync(15000).ConfigureAwait(false);
            }

            _pipe = pipe;
            _client = new RpcClient(pipe);
            return _client;
        }

        /// <summary>Khởi động sẵn editor ở nền ngay khi Word mở xong, để Alt+M hiện editor tức thì.</summary>
        public void Prewarm()
        {
            Task.Run(async () =>
            {
                try
                {
                    var client = await GetClientAsync().ConfigureAwait(false);
                    await client.InvokeAsync<string, string>(RpcMethods.Ping, "word").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    AddinLog.Error("Prewarm", ex);
                }
            });
        }

        public static string? FindEditorPath()
        {
            string? fromEnv = Environment.GetEnvironmentVariable("MATHTYPEX_EDITOR");
            if (!string.IsNullOrEmpty(fromEnv) && File.Exists(fromEnv)) return fromEnv;

            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\MathTypeX"))
            {
                if (key?.GetValue("EditorPath") is string fromRegistry && File.Exists(fromRegistry)) return fromRegistry;
            }

            string sibling = Path.GetFullPath(Path.Combine(AssemblyResolver.AddinDirectory, "..", "editor", "MathTypeX.Editor.exe"));
            return File.Exists(sibling) ? sibling : null;
        }

        private static void StartEditorProcess()
        {
            string path = FindEditorPath() ?? throw new FileNotFoundException(
                "Không tìm thấy MathTypeX.Editor.exe. Hãy chạy lại tools/install-dev.ps1.");
            AddinLog.Info("Khởi động editor: " + path);
            Process.Start(new ProcessStartInfo(path, "--server") { UseShellExecute = false });
        }

        private void Reset()
        {
            _client?.Dispose();
            _client = null;
            _pipe = null;
        }

        public void Dispose() => Reset();
    }
}
