using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using PeekShield.Services;

namespace PeekShield.Views;

public sealed class PasswordWindow : Window
{
    public enum Outcome { None, Ok, Cancelled, Recovery }

    public Outcome Result { get; private set; } = Outcome.None;

    private readonly string _stored;
    private readonly string? _question;
    private readonly string? _answerHash;
    private readonly PeekShieldEngine? _engine;
    private readonly bool _faceUnlockAvailable;
    private readonly bool _quickVerifyAvailable;
    private TextBox? _pwd;
    private TextBlock? _hint;
    private StackPanel? _recovery;
    private TextBox? _ans;
    private Button? _ok;
    private Button? _faceBtn;
    private bool _busy;
    private readonly DispatcherTimer _lockTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public PasswordWindow(string title, string prompt, string storedHash, string? question = null, string? answerHash = null, PeekShieldEngine? engine = null, bool faceUnlockAvailable = false, bool quickVerifyAvailable = false)
    {
        _stored = storedHash;
        _question = question;
        _answerHash = answerHash;
        _engine = engine;
        _faceUnlockAvailable = faceUnlockAvailable;
        _quickVerifyAvailable = quickVerifyAvailable;

        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        CanResize = false;
        Background = Palette.PageBg;
        FontFamily = new FontFamily(
            OperatingSystem.IsWindows() ? "Microsoft YaHei UI" :
            OperatingSystem.IsMacOS() ? "PingFang SC" : "Noto Sans CJK SC");

        try
        {
            using var s = AssetLoader.Open(new Uri("avares://PeekShield/Resources/icon.png"));
            Icon = new WindowIcon(s);
        }
        catch { }

        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(14) };

        panel.Children.Add(new TextBlock
        {
            Text = prompt,
            Foreground = Palette.TextSecondary,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        });

        _pwd = new TextBox
        {
            PasswordChar = '*',
            Watermark = "请输入密码",
            FontSize = 14,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        _pwd.KeyDown += (_, e) => { if (e.Key == Key.Enter) TrySubmit(); };
        panel.Children.Add(_pwd);

        _hint = new TextBlock
        {
            Foreground = Palette.Danger,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };
        panel.Children.Add(_hint);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _ok = new Button { Content = "确定", MinWidth = 96, Background = new SolidColorBrush(Color.Parse("#2563EB")), Foreground = new SolidColorBrush(Colors.White), Padding = new Thickness(10, 5) };
        _ok.Click += (_, _) => TrySubmit();
        var cancel = new Button { Content = "取消", MinWidth = 96, Padding = new Thickness(10, 5) };
        cancel.Click += (_, _) => { Result = Outcome.Cancelled; Close(); };
        row.Children.Add(_ok);
        row.Children.Add(cancel);
        panel.Children.Add(row);

        if (!string.IsNullOrEmpty(_question))
        {
            var forget = MakeLink("忘记密码？");
            forget.PointerPressed += (_, _) => ToggleRecovery();
            panel.Children.Add(forget);

            _recovery = new StackPanel { Spacing = 6, IsVisible = false, Margin = new Thickness(0, 4, 0, 0) };
            _recovery.Children.Add(new TextBlock
            {
                Text = "保密问题：" + _question,
                Foreground = Palette.TextSecondary,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            });
            _ans = new TextBox { PasswordChar = '*', Watermark = "请输入保密问题答案", FontSize = 14, VerticalContentAlignment = VerticalAlignment.Center };
            _ans.KeyDown += (_, e) => { if (e.Key == Key.Enter) TryRecovery(); };
            _recovery.Children.Add(_ans);
            var ansBtn = new Button { Content = "提交", MinWidth = 96, Padding = new Thickness(10, 5) };
            ansBtn.Click += (_, _) => TryRecovery();
            _recovery.Children.Add(ansBtn);
            panel.Children.Add(_recovery);
        }

        if (_faceUnlockAvailable && _engine != null)
        {
            _faceBtn = new Button
            {
                Content = "使用人脸解锁",
                MinWidth = 120,
                Padding = new Thickness(10, 5),
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Color.Parse("#16A34A")),
                Foreground = new SolidColorBrush(Colors.White)
            };
            _faceBtn.Click += (_, _) => _ = TryFaceUnlock();
            panel.Children.Add(_faceBtn);
        }

        Content = panel;
        Loaded += (_, _) =>
        {
            _pwd?.Focus();
            if (_quickVerifyAvailable && _engine != null) _ = StartQuickVerify();
        };
        _lockTimer.Tick += (_, _) => UpdateLockCountdown();
        Closed += (_, _) => StopLockTimer();
        RefreshLockoutUi();
    }

    public static async Task<Outcome> ShowVerify(Window? owner, string title, string prompt, string storedHash, string? question = null, string? answerHash = null, PeekShieldEngine? engine = null, bool faceUnlockAvailable = false, bool quickVerifyAvailable = false)
    {
        var w = new PasswordWindow(title, prompt, storedHash, question, answerHash, engine, faceUnlockAvailable, quickVerifyAvailable);
        if (owner != null) await w.ShowDialog(owner);
        else w.Show();
        return w.Result;
    }

    private void ToggleRecovery()
    {
        if (_recovery != null) _recovery.IsVisible = !_recovery.IsVisible;
    }

    private void TrySubmit()
    {
        var input = _pwd?.Text ?? "";
        if (SecurityService.TryUnlock(_stored, input, out var alt))
        {
            StopLockTimer();
            Result = Outcome.Ok;
            Close();
            return;
        }

        if (SecurityService.IsLocked(out var remaining))
        {
            ShowLockCountdown(TimeSpan.FromMinutes(remaining));
            StartLockTimer();
        }
        else
        {
            SecurityService.RecordFailure(out var remainingAttempts, out var lockoutMinutes);
            if (_hint != null)
            {
                if (remainingAttempts > 0)
                {
                    _hint.Text = $"密码错误，你还有 {remainingAttempts} 次机会";
                    _hint.IsVisible = true;
                    StopLockTimer();
                }
                else
                {
                    ShowLockCountdown(TimeSpan.FromMinutes(lockoutMinutes));
                    StartLockTimer();
                }
            }
        }
        if (_pwd != null) _pwd.Text = "";
    }

    private async Task TryFaceUnlock()
    {
        if (_busy || _engine == null) return;
        _busy = true;
        if (_ok != null) _ok.IsEnabled = false;
        if (_faceBtn != null) _faceBtn.IsEnabled = false;
        if (_hint != null) { _hint.Text = "正在调用摄像头进行人脸验证…"; _hint.IsVisible = true; }
        bool ok = false;
        try
        {
            ok = await _engine.VerifyUnlockAsync(msg => { if (_hint != null) { _hint.Text = msg; _hint.IsVisible = true; } });
        }
        catch { ok = false; }
        if (ok)
        {
            SecurityService.SetFaceUnlocked();
            StopLockTimer();
            Result = Outcome.Ok;
            Close();
            return;
        }
        if (_hint != null) { _hint.Text = "人脸未匹配，请重试或使用密码解锁。"; _hint.IsVisible = true; }
        if (_ok != null) _ok.IsEnabled = true;
        if (_faceBtn != null) _faceBtn.IsEnabled = true;
        _busy = false;
    }

    private async Task StartQuickVerify()
    {
        if (_busy || _engine == null) return;
        _busy = true;
        if (_faceBtn != null) _faceBtn.IsEnabled = false;
        if (_hint != null) { _hint.Text = "正在识别机主…（无需操作，识别成功将自动解锁）"; _hint.IsVisible = true; }
        bool ok = false;
        try
        {
            ok = await _engine.TryAutoVerifyAsync(msg => { if (_hint != null) { _hint.Text = msg; _hint.IsVisible = true; } });
        }
        catch { ok = false; }
        if (ok)
        {
            SecurityService.SetFaceUnlocked();
            StopLockTimer();
            Result = Outcome.Ok;
            Close();
            return;
        }
        _busy = false;
        if (_faceBtn != null) _faceBtn.IsEnabled = true;
        if (_hint != null) { _hint.Text = "未识别到机主，请输入密码或使用人脸解锁。"; _hint.IsVisible = true; }
    }

    private void RefreshLockoutUi()
    {
        if (SecurityService.IsLocked(out var remaining))
        {
            ShowLockCountdown(TimeSpan.FromMinutes(remaining));
            StartLockTimer();
        }
        else if (_hint != null)
        {
            _hint.IsVisible = false;
            StopLockTimer();
        }
    }

    private void UpdateLockCountdown()
    {
        var rem = SecurityService.GetLockRemaining();
        if (rem > TimeSpan.Zero)
        {
            ShowLockCountdown(rem);
            return;
        }
        SecurityService.IsLocked(out _);
        StopLockTimer();
        if (_hint != null) _hint.IsVisible = false;
    }

    private void ShowLockCountdown(TimeSpan rem)
    {
        if (_hint == null) return;
        _hint.Text = $"账号已锁定，剩余锁定时间 {FormatSpan(rem)}";
        _hint.IsVisible = true;
    }

    private static string FormatSpan(TimeSpan t)
    {
        int total = (int)Math.Ceiling(t.TotalSeconds);
        int m = total / 60;
        int s = total % 60;
        if (m > 0) return s == 0 ? $"{m} 分钟" : $"{m} 分 {s} 秒";
        return $"{s} 秒";
    }

    private void StartLockTimer()
    {
        if (!_lockTimer.IsEnabled) _lockTimer.Start();
    }

    private void StopLockTimer()
    {
        if (_lockTimer.IsEnabled) _lockTimer.Stop();
    }

    private void TryRecovery()
    {
        var ans = _ans?.Text ?? "";
        if (SecurityService.VerifySecret(_answerHash ?? "", ans))
        {
            Result = Outcome.Recovery;
            Close();
            return;
        }
        if (_hint != null) { _hint.Text = "保密问题答案错误。"; _hint.IsVisible = true; }
        if (_ans != null) _ans.Text = "";
    }

    private static TextBlock MakeLink(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(ThemeService.IsDark ? Color.Parse("#60A5FA") : Color.Parse("#2563EB")),
            FontSize = 12,
            Cursor = new Cursor(StandardCursorType.Hand),
            Margin = new Thickness(0, 4, 0, 0)
        };
    }
}
