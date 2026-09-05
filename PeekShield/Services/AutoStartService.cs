using System;
using System.IO;
using System.Runtime.InteropServices;

namespace PeekShield.Services;

public static class AutoStartService
{
    private const string AppId = "com.peekshield.agent";
    private const string RegKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RegValue = "PeekShield";

    public static bool IsSupported =>
        Platform.IsWindows || Platform.IsMacOS || Platform.IsLinux;

    public static bool IsEnabled()
    {
        try
        {
            if (Platform.IsWindows) return CheckWindows();
            if (Platform.IsMacOS) return File.Exists(MacPlistPath());
            if (Platform.IsLinux) return File.Exists(LinuxDesktopPath());
        }
        catch { }
        return false;
    }

    public static void SetEnabled(bool enable)
    {
        try
        {
            if (Platform.IsWindows) SetWindows(enable);
            else if (Platform.IsMacOS) SetMac(enable);
            else if (Platform.IsLinux) SetLinux(enable);
        }
        catch { }
    }

#if WINDOWS
    private static bool CheckWindows()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegKey);
        return key?.GetValue(RegValue) != null;
    }

    private static void SetWindows(bool enable)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RegKey);
        if (enable)
        {
            var exe = Environment.ProcessPath ?? "";
            if (!string.IsNullOrEmpty(exe)) key.SetValue(RegValue, $"\"{exe}\"");
        }
        else
        {
            key.DeleteValue(RegValue, false);
        }
    }
#else
    private static bool CheckWindows() => false;
    private static void SetWindows(bool enable) { }
#endif

    private static string MacPlistPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", AppId + ".plist");

    private static void SetMac(bool enable)
    {
        var path = MacPlistPath();
        if (!enable) { if (File.Exists(path)) File.Delete(path); return; }
        var exe = Environment.ProcessPath ?? "";
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var plist = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
            + "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n"
            + "<plist version=\"1.0\"><dict>\n"
            + "  <key>Label</key><string>" + AppId + "</string>\n"
            + "  <key>ProgramArguments</key><array><string>" + SecurityEscape(exe) + "</string></array>\n"
            + "  <key>RunAtLoad</key><true/>\n"
            + "  <key>ProcessType</key><string>Interactive</string>\n"
            + "</dict></plist>\n";
        File.WriteAllText(path, plist);
    }

    private static string LinuxDesktopPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "autostart", "peekshield.desktop");

    private static void SetLinux(bool enable)
    {
        var path = LinuxDesktopPath();
        if (!enable) { if (File.Exists(path)) File.Delete(path); return; }
        var exe = Environment.ProcessPath ?? "";
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var entry = "[Desktop Entry]\n"
            + "Type=Application\n"
            + "Name=PeekShield 窥屿盾\n"
            + "Comment=本地离线隐私防窥工具\n"
            + "Exec=" + exe + "\n"
            + "X-GNOME-Autostart-enabled=true\n"
            + "Terminal=false\n";
        File.WriteAllText(path, entry);
    }

    private static string SecurityEscape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
