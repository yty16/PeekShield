using System.Threading.Tasks;

#if WINDOWS
using System;
using System.Runtime.InteropServices;

namespace PeekShield.Services;

public static class SystemUnlockService
{
    private const int ERROR_SUCCESS = 0;
    private const int ERROR_CANCELLED = 1223;
    private const int CREDUIWIN_ENUMERATE_CURRENT_USER = 0x200;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDUI_INFO
    {
        public int cbSize;
        public IntPtr hwndParent;
        public string pszMessageText;
        public string pszCaptionText;
        public IntPtr hbmBanner;
    }

    [DllImport("credui.dll", CharSet = CharSet.Unicode)]
    private static extern int CredUIPromptForWindowsCredentialsW(
        ref CREDUI_INFO pUiInfo, int dwAuthError, ref uint pulAuthPackage,
        IntPtr pvInAuthBuffer, uint ulInAuthBufferSize,
        out IntPtr ppvOutAuthBuffer, out uint pulOutAuthBufferSize,
        ref bool pfSave, int dwFlags);

    public static bool IsSupported() => OperatingSystem.IsWindows();

    public static async Task<(bool ok, string message)> VerifyAsync(string message)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                return (false, "当前平台暂不支持系统解锁，请使用密码或刷脸解锁。");

            var ui = new CREDUI_INFO
            {
                cbSize = Marshal.SizeOf<CREDUI_INFO>(),
                pszMessageText = message ?? "窥屿盾需要验证以解锁",
                pszCaptionText = "窥屿盾 - 系统解锁"
            };
            uint authPackage = 0;
            bool save = false;
            int r = CredUIPromptForWindowsCredentialsW(
                ref ui, 0, ref authPackage, IntPtr.Zero, 0,
                out IntPtr outBuf, out uint outSize, ref save, CREDUIWIN_ENUMERATE_CURRENT_USER);

            if (r == ERROR_CANCELLED)
            {
                if (outBuf != IntPtr.Zero) Marshal.FreeCoTaskMem(outBuf);
                return (false, "已取消系统验证。");
            }
            if (r != ERROR_SUCCESS)
            {
                if (outBuf != IntPtr.Zero) Marshal.FreeCoTaskMem(outBuf);
                return (false, "系统验证未通过（错误码 " + r + "）。");
            }

            if (outBuf != IntPtr.Zero) Marshal.FreeCoTaskMem(outBuf);
            return (true, "系统验证通过");
        }
        catch (Exception ex)
        {
            return (false, "系统解锁调用失败：" + ex.Message);
        }
    }
}
#else
namespace PeekShield.Services;

public static class SystemUnlockService
{
    public static bool IsSupported() => false;

    public static Task<(bool ok, string message)> VerifyAsync(string message) =>
        Task.FromResult((false, "当前平台暂不支持系统解锁，请使用密码或刷脸解锁。"));
}
#endif
