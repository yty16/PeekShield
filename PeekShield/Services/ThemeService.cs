using System;
using System.Runtime.InteropServices;
#if WINDOWS
using Microsoft.Win32;
#endif
using Avalonia.Threading;

namespace PeekShield.Services;

public enum ThemeMode
{
    System,
    Light,
    Dark
}

public static class ThemeService
{
    public static ThemeMode Mode { get; private set; } = ThemeMode.System;
    public static bool IsDark { get; private set; }
    public static event Action? Changed;

#if !WINDOWS
    private static DispatcherTimer? _timer;
#endif

    public static void Init(ThemeMode mode)
    {
        Mode = mode;
        IsDark = Resolve(mode);
        StartWatcher();
    }

    public static void SetMode(ThemeMode mode)
    {
        if (Mode == mode) return;
        Mode = mode;
        bool dark = Resolve(mode);
        if (dark != IsDark)
        {
            IsDark = dark;
            Changed?.Invoke();
        }
    }

    private static bool Resolve(ThemeMode mode) =>
        mode == ThemeMode.Dark ? true :
        mode == ThemeMode.Light ? false :
        DetectOsTheme();

    public static void RecheckOs()
    {
        if (Mode != ThemeMode.System) return;
        bool dark = DetectOsTheme();
        if (dark != IsDark)
        {
            IsDark = dark;
            Changed?.Invoke();
        }
    }

    private static bool DetectOsTheme()
    {
#if WINDOWS
        try
        {
            const int HKEY_CURRENT_USER = unchecked((int)0x80000001);
            const int KEY_READ = 0x20019;
            const int ERROR_SUCCESS = 0;
            if (RegOpenKeyEx(HKEY_CURRENT_USER, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", 0, KEY_READ, out IntPtr hKey) == ERROR_SUCCESS)
            {
                int size = 4;
                var data = new byte[4];
                int value = 1;
                if (RegQueryValueEx(hKey, "AppsUseLightTheme", 0, IntPtr.Zero, data, ref size) == ERROR_SUCCESS)
                    value = data[0] | (data[1] << 8) | (data[2] << 16) | (data[3] << 24);
                RegCloseKey(hKey);
                return value == 0;
            }
        }
        catch { }
        return false;
#elif MACOS
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("defaults", "read -g AppleInterfaceStyle")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            string? s = p?.StandardOutput.ReadToEnd();
            p?.WaitForExit();
            return !string.IsNullOrWhiteSpace(s) && s.Contains("Dark", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
#else
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("gsettings", "get org.gnome.desktop.interface color-scheme")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            string? s = p?.StandardOutput.ReadToEnd();
            p?.WaitForExit();
            if (!string.IsNullOrWhiteSpace(s) && s.Contains("dark", StringComparison.OrdinalIgnoreCase)) return true;
        }
        catch { }
        return false;
#endif
    }

    private static void StartWatcher()
    {
#if WINDOWS
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
#else
        if (_timer == null)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _timer.Tick += (_, _) => RecheckOs();
        }
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _timer.Tick += (_, _) => RecheckOs();
        }
        if (Mode == ThemeMode.System) _timer.Start();
        else _timer.Stop();
#endif
    }

#if WINDOWS
    private static void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General) RecheckOs();
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegOpenKeyEx(int hKey, string subKey, int options, int samDesired, out IntPtr phkResult);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegQueryValueEx(IntPtr hKey, string lpValueName, int lpReserved, IntPtr lpType, byte[] lpData, ref int lpcbData);

    [DllImport("advapi32.dll")]
    private static extern int RegCloseKey(IntPtr hKey);
#endif
}
