using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace PeekShield.Services;

#if WINDOWS
public sealed class HotkeyService : IDisposable
{
    private readonly Native.LowLevelKeyboardProc _callback;
    private IntPtr _hhk = IntPtr.Zero;
    private readonly int _key;
    private readonly int _modifiers;
    private readonly Action _handler;
    private bool _ctrl;
    private bool _shift;
    private bool _alt;
    private bool _firstCallLogged;

    public HotkeyService(string? modifiers, string? key, Action handler)
    {
        _handler = handler;
        _modifiers = ParseMods(modifiers);
        _key = ParseKey(key);
        _callback = HookCallback;
        try
        {
            var hMod = Marshal.GetHINSTANCE(typeof(Native).Module);
            if (hMod == IntPtr.Zero) hMod = Native.GetModuleHandle(null);
            _hhk = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _callback, hMod, 0);
            if (_hhk == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                LoggerService.LogInfo($"快捷键钩子安装失败，错误码 {err}（mods=0x{_modifiers:X} key=0x{_key:X}）");
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogInfo($"快捷键钩子异常：{ex.Message}");
        }
    }

    private static int ParseMods(string? m)
    {
        int r = 0;
        if (string.IsNullOrWhiteSpace(m)) return r;
        var parts = m.Split('+', StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) r |= Native.MOD_CONTROL;
            else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase)) r |= Native.MOD_SHIFT;
            else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase)) r |= Native.MOD_ALT;
        }
        return r;
    }

    private static int ParseKey(string? k)
    {
        if (string.IsNullOrWhiteSpace(k)) return 0x50;
        k = k.Trim();
        if (k.Length == 1 && char.IsLetter(k[0])) return 0x41 + (char.ToUpperInvariant(k[0]) - 'A');
        if (k.Length == 1 && char.IsDigit(k[0])) return 0x30 + (k[0] - '0');
        if (k.StartsWith("F", StringComparison.OrdinalIgnoreCase) && int.TryParse(k[1..], out int n) && n >= 1 && n <= 12) return 0x70 + (n - 1);
        if (k.Equals("Space", StringComparison.OrdinalIgnoreCase)) return 0x20;
        if (k.Equals("Escape", StringComparison.OrdinalIgnoreCase)) return 0x1B;
        return 0x50;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vk = Marshal.ReadInt32(lParam);
            bool down = (int)wParam == Native.WM_KEYDOWN || (int)wParam == Native.WM_SYSKEYDOWN;
            bool up = (int)wParam == Native.WM_KEYUP || (int)wParam == Native.WM_SYSKEYUP;

            if (!_firstCallLogged)
            {
                _firstCallLogged = true;
                LoggerService.LogInfo($"快捷键回调首次触发（mods=0x{_modifiers:X} key=0x{_key:X}）");
            }

            if (down)
            {
                if (IsCtrl(vk)) _ctrl = true;
                else if (IsShift(vk)) _shift = true;
                else if (IsAlt(vk)) _alt = true;
                else if (vk == _key && MatchMods())
                {
                    LoggerService.LogInfo($"快捷键匹配成功 vk=0x{vk:X}");
                    try { _handler(); } catch (Exception ex) { LoggerService.LogInfo($"快捷键处理异常：{ex.Message}"); }
                }
            }
            else if (up)
            {
                if (IsCtrl(vk)) _ctrl = false;
                else if (IsShift(vk)) _shift = false;
                else if (IsAlt(vk)) _alt = false;
            }
        }
        return Native.CallNextHookEx(_hhk, nCode, wParam, lParam);
    }

    private static bool IsCtrl(int vk) => vk == Native.VK_CONTROL || vk == Native.VK_LCONTROL || vk == Native.VK_RCONTROL;
    private static bool IsShift(int vk) => vk == Native.VK_SHIFT || vk == Native.VK_LSHIFT || vk == Native.VK_RSHIFT;
    private static bool IsAlt(int vk) => vk == Native.VK_MENU || vk == Native.VK_LMENU || vk == Native.VK_RMENU;

    private bool MatchMods()
    {
        bool wantCtrl = (_modifiers & Native.MOD_CONTROL) != 0;
        bool wantShift = (_modifiers & Native.MOD_SHIFT) != 0;
        bool wantAlt = (_modifiers & Native.MOD_ALT) != 0;
        return _ctrl == wantCtrl && _shift == wantShift && _alt == wantAlt;
    }

    public void Dispose()
    {
        if (_hhk != IntPtr.Zero)
        {
            Native.UnhookWindowsHookEx(_hhk);
            _hhk = IntPtr.Zero;
        }
    }
}
#else
public sealed class HotkeyService : IDisposable
{
    public HotkeyService(string? modifiers, string? key, Action handler) { }
    public void Dispose() { }
}
#endif
