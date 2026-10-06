#if NETSTANDARD2_0 || NETFRAMEWORK
// Cho phép dùng `record` và `init` trên netstandard2.0/net48 (thư viện được add-in .NET Framework 4.8 dùng lại).
// File này được Directory.Build.targets tự thêm vào mọi project nhắm các framework cũ.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#endif
