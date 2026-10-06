using System;
using System.IO;
using System.Runtime.InteropServices;
using MathTypeX.WordAddin.Interop;

namespace MathTypeX.WordAddin
{
    /// <summary>
    /// Điểm vào COM mà Word nạp (ProgId "MathTypeX.WordAddin"). Lớp này chỉ dùng kiểu của BCL để
    /// <see cref="AssemblyResolver"/> kịp cài trước khi các assembly phụ thuộc được nạp.
    /// Mọi lời gọi từ Office đều được bọc try/catch — ngoại lệ lọt ra sẽ khiến Office vô hiệu hoá add-in.
    /// </summary>
    [ComVisible(true)]
    [Guid("5EBC7F71-F8F9-45E5-AC8E-54FED67797E1")]
    [ProgId("MathTypeX.WordAddin")]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public sealed class Connect : IDTExtensibility2, IRibbonExtensibility
    {
        private AddinCore? _core;

        static Connect()
        {
            AssemblyResolver.Install();
        }

        public void OnConnection(object application, ext_ConnectMode connectMode, object addInInst, ref Array custom)
        {
            try
            {
                AddinLog.Info($"OnConnection mode={connectMode}");
                _core = new AddinCore(application);
            }
            catch (Exception ex)
            {
                AddinLog.Error("OnConnection", ex);
            }
        }

        public void OnStartupComplete(ref Array custom)
        {
            try
            {
                _core?.Start();
            }
            catch (Exception ex)
            {
                AddinLog.Error("OnStartupComplete", ex);
            }
        }

        public void OnAddInsUpdate(ref Array custom)
        {
        }

        public void OnBeginShutdown(ref Array custom) => Shutdown();

        public void OnDisconnection(ext_DisconnectMode removeMode, ref Array custom) => Shutdown();

        private void Shutdown()
        {
            try
            {
                _core?.Dispose();
            }
            catch (Exception ex)
            {
                AddinLog.Error("Shutdown", ex);
            }
            _core = null;
        }

        public string GetCustomUI(string ribbonId)
        {
            try
            {
                using var stream = typeof(Connect).Assembly.GetManifestResourceStream("MathTypeX.WordAddin.Ribbon.xml");
                if (stream is null) return "";
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (Exception ex)
            {
                AddinLog.Error("GetCustomUI", ex);
                return "";
            }
        }

        // ── Ribbon callbacks (Office gọi qua IDispatch theo tên) ─────────────────

        public void OnInsertEquation(object control)
        {
            try
            {
                _core?.OpenEditor();
            }
            catch (Exception ex)
            {
                AddinLog.Error("OnInsertEquation", ex);
            }
        }

        public void OnConvertSelection(object control)
        {
            try
            {
                _core?.ConvertSelection();
            }
            catch (Exception ex)
            {
                AddinLog.Error("OnConvertSelection", ex);
            }
        }

        public void OnConvertDocument(object control)
        {
            try
            {
                _core?.ConvertDocument();
            }
            catch (Exception ex)
            {
                AddinLog.Error("OnConvertDocument", ex);
            }
        }

        public void OnRevertToLatex(object control)
        {
            try
            {
                _core?.RevertToLatex();
            }
            catch (Exception ex)
            {
                AddinLog.Error("OnRevertToLatex", ex);
            }
        }

        public void OnOpenLog(object control)
        {
            try
            {
                AddinLog.OpenInNotepad();
            }
            catch (Exception ex)
            {
                AddinLog.Error("OnOpenLog", ex);
            }
        }
    }
}
