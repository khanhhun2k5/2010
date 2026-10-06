using System;
using System.Runtime.InteropServices;

// Khai báo lại hai interface COM mà Office gọi vào add-in, để không phụ thuộc Extensibility.dll / office.dll (PIA).
namespace MathTypeX.WordAddin.Interop
{
    public enum ext_ConnectMode
    {
        ext_cm_AfterStartup = 0,
        ext_cm_Startup = 1,
        ext_cm_External = 2,
        ext_cm_CommandLine = 3,
        ext_cm_Solution = 4,
        ext_cm_UISetup = 5,
    }

    public enum ext_DisconnectMode
    {
        ext_dm_HostShutdown = 0,
        ext_dm_UserClosed = 1,
        ext_dm_UISetupComplete = 2,
        ext_dm_SolutionClosed = 3,
    }

    [ComImport]
    [Guid("B65AD801-ABAF-11D0-BB8B-00A0C90F2744")]
    [TypeLibType(4160)]
    public interface IDTExtensibility2
    {
        [DispId(1)]
        void OnConnection([MarshalAs(UnmanagedType.IDispatch)] object application, ext_ConnectMode connectMode,
            [MarshalAs(UnmanagedType.IDispatch)] object addInInst, [MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref Array custom);

        [DispId(2)]
        void OnDisconnection(ext_DisconnectMode removeMode, [MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref Array custom);

        [DispId(3)]
        void OnAddInsUpdate([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref Array custom);

        [DispId(4)]
        void OnStartupComplete([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref Array custom);

        [DispId(5)]
        void OnBeginShutdown([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref Array custom);
    }

    [ComImport]
    [Guid("000C0396-0000-0000-C000-000000000046")]
    [TypeLibType(4160)]
    public interface IRibbonExtensibility
    {
        [DispId(1)]
        [return: MarshalAs(UnmanagedType.BStr)]
        string GetCustomUI([MarshalAs(UnmanagedType.BStr)] string ribbonId);
    }
}
