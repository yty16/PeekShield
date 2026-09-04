using System;
using System.Diagnostics;

namespace PeekShield.Services;

public class ForegroundWatcher
{
    public bool IsSupported { get; private set; }

    public bool IsLocked { get; private set; }
    public string? ForegroundProcessName { get; private set; }
    public string? ForegroundWindowTitle { get; private set; }
    public event Action? StateChanged;

#if WINDOWS
    public ForegroundWatcher()
    {
        IsSupported = true;
    }

    public void Start()
    {
        Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
        RefreshForeground();
    }

    public void Stop()
    {
        Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
    }

    private void OnSessionSwitch(object? sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        IsLocked = e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock;
        RefreshForeground();
        StateChanged?.Invoke();
    }

    public void RefreshForeground()
    {
        try
        {
            var hwnd = Native.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) { ForegroundProcessName = null; ForegroundWindowTitle = null; return; }
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) { ForegroundProcessName = null; ForegroundWindowTitle = null; return; }
            using var p = Process.GetProcessById((int)pid);
            ForegroundProcessName = p.ProcessName;
            ForegroundWindowTitle = GetWindowTitle(hwnd);
        }
        catch
        {
            ForegroundProcessName = null;
            ForegroundWindowTitle = null;
        }
    }

    private static string? GetWindowTitle(IntPtr hwnd)
    {
        try
        {
            int len = Native.GetWindowTextLength(hwnd);
            if (len <= 0) return string.Empty;
            var sb = new System.Text.StringBuilder(len + 1);
            Native.GetWindowText(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }
#else
    public ForegroundWatcher()
    {
        IsSupported = false;
        IsLocked = false;
        ForegroundProcessName = null;
        ForegroundWindowTitle = null;
    }

    public void Start() { }
    public void Stop() { }
    public void RefreshForeground() { }
#endif
}
