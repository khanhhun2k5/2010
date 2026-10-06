using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MathTypeX.WordAddin
{
    internal static class NativeMethods
    {
        public const int WH_KEYBOARD = 2;
        public const int HC_ACTION = 0;
        public const int VK_SHIFT = 0x10;
        public const int VK_CONTROL = 0x11;
        public const int VK_MENU = 0x12;
        public const int VK_M = 0x4D;
        /// <summary>Mã phím chưa được gán — dùng làm "menu mask" để nhả Alt không bật KeyTips (kỹ thuật của AutoHotkey).</summary>
        public const ushort VK_MENU_MASK = 0xE8;
        public const int ASFW_ANY = -1;

        public delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern short GetKeyState(int nVirtKey);

        /// <summary>Trạng thái phím vật lý ngay lúc gọi (dùng để dừng thao tác dài bằng Esc).</summary>
        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        public static extern IntPtr GetFocus();

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AllowSetForegroundWindow(int dwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // Có mặt để kích thước union đúng với INPUT của Win32.
        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public const uint INPUT_KEYBOARD = 1;
        public const uint KEYEVENTF_KEYUP = 0x0002;

        public static bool IsKeyDown(int vk) => (GetKeyState(vk) & 0x8000) != 0;

        public static string ClassNameOf(IntPtr hwnd)
        {
            var sb = new StringBuilder(64);
            return GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
        }

        /// <summary>Nhấn-nhả một phím chưa gán khi Alt đang giữ, để lúc nhả Alt Office không mở KeyTips.</summary>
        public static void SendMenuMask()
        {
            var inputs = new INPUT[2];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = VK_MENU_MASK;
            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].u.ki.wVk = VK_MENU_MASK;
            inputs[1].u.ki.dwFlags = KEYEVENTF_KEYUP;
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }
    }
}
