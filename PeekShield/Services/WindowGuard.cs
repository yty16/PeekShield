using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using PeekShield.Models;

namespace PeekShield.Services;

public static class WindowGuard
{
#if WINDOWS
    public static void MinimizeProcesses(IEnumerable<ProtectedEntry> entries)
    {
        var set = new HashSet<string>(entries.Where(e => e.Enabled && !string.IsNullOrWhiteSpace(e.Name)).Select(e => e.Name), StringComparer.OrdinalIgnoreCase);
        if (set.Count == 0) return;
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd)) return true;
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return true;
            try
            {
                using var p = Process.GetProcessById((int)pid);
                if (!string.IsNullOrEmpty(p.ProcessName) && set.Contains(p.ProcessName))
                    Native.ShowWindow(hwnd, Native.SW_MINIMIZE);
            }
            catch { }
            return true;
        }, IntPtr.Zero);
    }

    public static void RestoreProcesses(IEnumerable<ProtectedEntry> entries)
    {
        var set = new HashSet<string>(entries.Where(e => e.Enabled && !string.IsNullOrWhiteSpace(e.Name)).Select(e => e.Name), StringComparer.OrdinalIgnoreCase);
        if (set.Count == 0) return;
        Native.EnumWindows((hwnd, _) =>
        {
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return true;
            try
            {
                using var p = Process.GetProcessById((int)pid);
                if (!string.IsNullOrEmpty(p.ProcessName) && set.Contains(p.ProcessName))
                    Native.ShowWindow(hwnd, Native.SW_RESTORE);
            }
            catch { }
            return true;
        }, IntPtr.Zero);
    }
#else
    public static void MinimizeProcesses(IEnumerable<ProtectedEntry> entries) { }
    public static void RestoreProcesses(IEnumerable<ProtectedEntry> entries) { }
#endif
}
