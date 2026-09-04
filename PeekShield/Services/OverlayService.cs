using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using PeekShield.Models;

namespace PeekShield.Services;

public class OverlayService
{
    private readonly List<Window> _fog = new();
    private readonly List<ProtectWindow> _protect = new();
    private PopupWindow? _popup;

    private bool _fogOn;
    private bool _protectOn;
    private bool _popupOn;

    public event Action? Dismissed;

    public bool IsFogOn => _fogOn;
    public bool IsProtectOn => _protectOn;

    public void ShowFog(string? message = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_fogOn)
            {
                foreach (var w in _fog) (w as FogWindow)?.SetMessage(message);
                return;
            }
            foreach (var s in AllScreens())
            {
                var w = new FogWindow();
                w.SetMessage(message);
                w.Position = s.Bounds.TopLeft;
                w.Width = s.Bounds.Width;
                w.Height = s.Bounds.Height;
                w.WindowState = WindowState.FullScreen;
                w.Show();
                _fog.Add(w);
            }
            _fogOn = true;
        });
    }

    public void ShowProtect()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_protectOn) return;
            foreach (var s in AllScreens())
            {
                var w = new ProtectWindow();
                w.ContinueRequested += OnContinue;
                w.Position = s.Bounds.TopLeft;
                w.Width = s.Bounds.Width;
                w.Height = s.Bounds.Height;
                w.WindowState = WindowState.FullScreen;
                w.Show();
                _protect.Add(w);
            }
            _protectOn = true;
        });
    }

    public void ShowPopup(string message, PeekShieldSettings? st = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ClosePopupInternal();
            _popup = new PopupWindow(message, st);
            _popup.Closing += OnPopupClosing;
            _popup.Show();
            _popupOn = true;
            LoggerService.LogInfo($"弹窗已显示 宽={_popup.Width} 高={_popup.Height} 字号={st?.PopupFontSize ?? 22} 位置=({_popup.Position.X},{_popup.Position.Y}) 模式={st?.PopupPosition ?? "center"}");
        });
    }

    private void ClosePopupInternal()
    {
        if (_popup == null) return;
        try
        {
            _popup.Closing -= OnPopupClosing;
            _popup.Close();
        }
        catch { }
        _popup = null;
        _popupOn = false;
    }

    private void OnPopupClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _popupOn = false;
    }

    public void HideAll()
    {
        if (!_fogOn && !_protectOn && !_popupOn) return;
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var w in _fog) { try { w.Close(); } catch { } }
            _fog.Clear();
            foreach (var w in _protect) { try { w.ContinueRequested -= OnContinue; w.Close(); } catch { } }
            _protect.Clear();
            ClosePopupInternal();
        });
        _fogOn = false;
        _protectOn = false;
        _popupOn = false;
    }

    private void OnContinue() => Dismissed?.Invoke();

    private static IReadOnlyList<Screen> AllScreens()
    {
        TopLevel? tl = null;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime life)
            tl = life.MainWindow;
        var screens = tl?.Screens;
        if (screens != null)
        {
            var all = screens.All;
            if (all != null && all.Count > 0) return all;
            var p = screens.Primary;
            if (p != null) return new[] { p };
        }
        return Array.Empty<Screen>();
    }

    private class FogWindow : Window
    {
        private readonly TextBlock _text;
        public FogWindow()
        {
            SystemDecorations = SystemDecorations.None;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Blur };
            Background = Brushes.Transparent;
            IsHitTestVisible = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Opened += (_, _) =>
            {
#if WINDOWS
                InstallClickThrough();
#endif
            };
            FontFamily = new FontFamily(
                OperatingSystem.IsWindows() ? "Microsoft YaHei UI" :
                OperatingSystem.IsMacOS() ? "PingFang SC" :
                "Noto Sans CJK SC");

            var grid = new Grid();
            grid.Children.Add(new Border
            {
                Background = new SolidColorBrush(Colors.Black, 0.45),
                IsHitTestVisible = false
            });
            _text = new TextBlock
            {
                Text = PeekShieldSettings.DefaultPeekAlertText,
                Foreground = Brushes.White,
                FontSize = 30,
                FontWeight = FontWeight.Bold,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsVisible = true
            };
            grid.Children.Add(new Border
            {
                Child = _text,
                Background = new SolidColorBrush(Colors.Black, 0.35),
                Padding = new Thickness(24, 12),
                CornerRadius = new CornerRadius(10),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            });
            Content = grid;
        }

#if WINDOWS
        private const int WM_NCHITTEST = 0x0084;
        private static readonly IntPtr HTTRANSPARENT = new IntPtr(-1);
        private Native.WndProcDelegate? _clickThroughProc;
        private IntPtr _prevWndProc;

        private void InstallClickThrough()
        {
            var h = this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (h == IntPtr.Zero) return;
            var ex = Native.GetWindowLongPtr(h, Native.GWL_EXSTYLE);
            Native.SetWindowLongPtr(h, Native.GWL_EXSTYLE, new IntPtr(ex.ToInt64() | Native.WS_EX_TRANSPARENT));
            _clickThroughProc = ClickThroughWndProc;
            _prevWndProc = Native.SetWindowLongPtr(h, Native.GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_clickThroughProc));
        }

        private IntPtr ClickThroughWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_NCHITTEST) return HTTRANSPARENT;
            return Native.CallWindowProc(_prevWndProc, hWnd, msg, wParam, lParam);
        }
#endif

        public void SetMessage(string? msg) => Dispatcher.UIThread.Post(() =>
        {
            if (!string.IsNullOrEmpty(msg)) _text.Text = msg;
        });
    }

    private class ProtectWindow : Window
    {
        public event Action? ContinueRequested;

        public ProtectWindow()
        {
            SystemDecorations = SystemDecorations.None;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = true;
            Focusable = true;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x22));
            FontFamily = new FontFamily(
                OperatingSystem.IsWindows() ? "Microsoft YaHei UI" :
                OperatingSystem.IsMacOS() ? "PingFang SC" :
                "Noto Sans CJK SC");

            var icon = new TextBlock
            {
                Text = "🛡",
                FontSize = 72,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = Brushes.White
            };

            var title = new TextBlock
            {
                Text = "隐私保护中",
                FontSize = 24,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 18, 0, 0)
            };

            var btn = new Button
            {
                Content = "继续查看",
                FontSize = 14,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x25, 0x76, 0xF6)),
                Padding = new Thickness(32, 10),
                CornerRadius = new CornerRadius(20),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 28, 0, 0)
            };
            btn.Click += (_, _) => Continue();

            var panel = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Children = { icon, title, btn }
            };

            Content = new Grid
            {
                Background = new SolidColorBrush(Color.FromRgb(0x15, 0x15, 0x22)),
                Children = { panel }
            };

            KeyDown += (_, e) =>
            {
                if (e.Key == Key.Space || e.Key == Key.Enter) Continue();
            };
            PointerPressed += (_, e) =>
            {
                if (e.Source is Button) return;
                Continue();
            };
        }

        private void Continue()
        {
            ContinueRequested?.Invoke();
            Close();
        }
    }

    private static Screen? PrimaryScreen()
    {
        TopLevel? tl = null;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime life)
            tl = life.MainWindow;
        var screens = tl?.Screens;
        if (screens == null) return null;
        var p = screens.Primary;
        if (p != null) return p;
        var all = screens.All;
        return all != null && all.Count > 0 ? all[0] : null;
    }

private static PixelPoint ComputePopupPosition(PeekShieldSettings? st, double w, double h)
    {
        var mode = st?.PopupPosition ?? "center";
        if (mode == "custom")
            return new PixelPoint(st?.PopupX ?? 240, st?.PopupY ?? 240);
        var scr = PrimaryScreen();
        var b = scr?.Bounds ?? new PixelRect(0, 0, 1280, 720);
        int x, y;
        if (mode == "top")
        {
            x = (int)Math.Round(b.X + (b.Width - w) / 2);
            y = (int)Math.Round(b.Y + Math.Max(b.Height * 0.06, 12));
        }
        else if (mode == "bottom")
        {
            x = (int)Math.Round(b.X + (b.Width - w) / 2);
            y = (int)Math.Round(b.Y + b.Height - h - Math.Max(b.Height * 0.08, 24));
        }
        else
        {
            x = (int)Math.Round(b.X + (b.Width - w) / 2);
            y = (int)Math.Round(b.Y + (b.Height - h) / 2);
        }
        if (x < b.X) x = b.X;
        if (y < b.Y) y = b.Y;
        return new PixelPoint(x, y);
    }

    private class PopupWindow : Window
    {
        private readonly TextBlock _text;
        public PopupWindow(string initialMessage, PeekShieldSettings? st)
        {
            SystemDecorations = SystemDecorations.None;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            Background = Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.Manual;
            CanResize = false;
            int w = Math.Clamp(st?.PopupWidth ?? 520, 240, 1600);
            int h = Math.Clamp(st?.PopupHeight ?? 170, 80, 800);
            int fontSize = Math.Clamp(st?.PopupFontSize ?? 28, 10, 96);
            Width = w;
            Height = h;
            MinWidth = w;
            MaxWidth = w;
            MinHeight = h;
            MaxHeight = h;
            FontFamily = new FontFamily(
                OperatingSystem.IsWindows() ? "Microsoft YaHei UI" :
                OperatingSystem.IsMacOS() ? "PingFang SC" :
                "Noto Sans CJK SC");
            _text = new TextBlock
            {
                Text = initialMessage,
                Foreground = Brushes.White,
                FontSize = fontSize,
                FontWeight = FontWeight.Bold,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = w - 64
            };
            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28), 0.94),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(24, 18),
                Child = _text
            };
            Position = ComputePopupPosition(st, w, h);
            Opened += (_, _) =>
            {
                if (st != null)
                {
                    LoggerService.LogInfo($"弹窗应用样式 宽={Width} 高={Height} 字号={fontSize} 位置=({Position.X},{Position.Y}) 模式={st.PopupPosition}");
                }
            };
        }
        public void SetMessage(string msg) => Dispatcher.UIThread.Post(() => _text.Text = msg);
    }
}
