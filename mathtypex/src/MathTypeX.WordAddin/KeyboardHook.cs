using System;

namespace MathTypeX.WordAddin
{
    /// <summary>
    /// Hook bàn phím **cục bộ theo thread UI của Word** (WH_KEYBOARD, không phải hook toàn hệ thống).
    /// Chỉ bắt Alt+M khi con trỏ đang ở vùng soạn tài liệu (class "_WwG") — docs/06 §11.2.
    /// Callback không làm việc nặng: chỉ post sang hàng đợi UI.
    /// </summary>
    internal sealed class KeyboardHook : IDisposable
    {
        private const string WordDocumentWindowClass = "_WwG";

        // Giữ delegate trong field để GC không thu hồi khi Windows còn gọi vào.
        private readonly NativeMethods.HookProc _proc;
        private readonly Action _onAltM;
        private IntPtr _handle;
        private bool _swallowKeyUp;

        public KeyboardHook(Action onAltM)
        {
            _onAltM = onAltM;
            _proc = Callback;
            _handle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD, _proc, IntPtr.Zero, NativeMethods.GetCurrentThreadId());
            AddinLog.Info(_handle == IntPtr.Zero ? "Không cài được keyboard hook" : "Đã cài keyboard hook (thread-local)");
        }

        public bool IsInstalled => _handle != IntPtr.Zero;

        private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (code == NativeMethods.HC_ACTION && wParam.ToInt32() == NativeMethods.VK_M)
                {
                    long flags = lParam.ToInt64();
                    bool keyUp = (flags & 0x80000000L) != 0;
                    bool altDown = (flags & 0x20000000L) != 0;
                    bool repeat = (flags & 0x40000000L) != 0;

                    if (!keyUp && altDown
                        && !NativeMethods.IsKeyDown(NativeMethods.VK_CONTROL)
                        && !NativeMethods.IsKeyDown(NativeMethods.VK_SHIFT)
                        && NativeMethods.ClassNameOf(NativeMethods.GetFocus()) == WordDocumentWindowClass)
                    {
                        _swallowKeyUp = true;
                        if (!repeat)
                        {
                            NativeMethods.SendMenuMask();
                            _onAltM();
                        }
                        return (IntPtr)1;
                    }

                    if (keyUp && _swallowKeyUp)
                    {
                        _swallowKeyUp = false;
                        return (IntPtr)1;
                    }
                }
            }
            catch (Exception ex)
            {
                AddinLog.Error("KeyboardHook", ex);
            }
            return NativeMethods.CallNextHookEx(_handle, code, wParam, lParam);
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }
}
