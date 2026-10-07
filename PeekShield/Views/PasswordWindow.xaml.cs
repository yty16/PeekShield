using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using PeekShield.Models;
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
    private readonly PeekShieldSettings _settings;
    private readonly List<AuthMethodEntry> _methods;
    private readonly string _operation;
    private readonly bool _quickVerify;

    private StackPanel? _methodList;
    private Panel? _contentHost;
    private TextBlock? _hint;
    private TextBlock? _titleText;
    private Button? _cancelBtn;
    private readonly List<Button> _methodButtons = new();
    private int _selectedIndex = 0;
    private bool _busy;
    private AuthMethodKind _selectedKind;
    private AuthMethodEntry? _selectedMethod;

    private readonly HashSet<AuthMethodKind> _passedKinds = new();
    private readonly HashSet<string> _passedCategories = new();
    private readonly List<string> _methodLabels = new();
    private readonly bool _requireTwo;
    private readonly bool _allowRecovery;

    private TextBox? _pwdBox;
    private Image? _facePreview;
    private TextBlock? _faceStatus;
    private Button? _faceRetryBtn;
    private CancellationTokenSource? _faceCts;

    private readonly DispatcherTimer _lockTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public PasswordWindow(string operation, string title, string prompt, string storedHash, string? question, string? answerHash, PeekShieldEngine? engine, PeekShieldSettings settings, List<AuthMethodEntry> methods, bool quickVerify, bool allowRecovery = true)
    {
        _operation = operation;
        _stored = storedHash;
        _question = question;
        _answerHash = answerHash;
        _engine = engine;
        _settings = settings;
        _methods = methods;
        _quickVerify = quickVerify;
        _allowRecovery = allowRecovery;
        _requireTwo = _settings.TwoFactorEnabled && _methods.Select(m => FactorCategory(m.Kind)).Distinct().Count() >= 2;

        Title = title;
        Width = 640;
        Height = 420;
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

        var grid = new Grid { Margin = new Thickness(0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });

        var left = new Border
        {
            Background = Palette.CardBg,
            BorderThickness = new Thickness(0, 0, 1, 0),
            BorderBrush = Palette.Border,
            Padding = new Thickness(12)
        };
        Grid.SetColumn(left, 0);

        var leftStack = new StackPanel { Spacing = 10 };
        leftStack.Children.Add(new TextBlock
        {
            Text = "验证方式",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = Palette.TextPrimary,
            Margin = new Thickness(0, 0, 0, 4)
        });

        _methodList = new StackPanel { Spacing = 4 };
        leftStack.Children.Add(_methodList);
        left.Child = leftStack;

        var right = new Grid { Margin = new Thickness(20) };
        Grid.SetColumn(right, 1);
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _titleText = new TextBlock
        {
            Text = prompt,
            Foreground = Palette.TextSecondary,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(_titleText, 0);
        right.Children.Add(_titleText);

        _contentHost = new Panel();
        Grid.SetRow(_contentHost, 1);
        right.Children.Add(_contentHost);

        var bottom = new StackPanel { Spacing = 8, Margin = new Thickness(0, 12, 0, 0) };
        Grid.SetRow(bottom, 2);
        _hint = new TextBlock
        {
            Foreground = Palette.Danger,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };
        bottom.Children.Add(_hint);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        _cancelBtn = new Button { Content = "取消", MinWidth = 80, Padding = new Thickness(10, 5) };
        _cancelBtn.Click += (_, _) => { Result = Outcome.Cancelled; Close(); };
        btnRow.Children.Add(_cancelBtn);
        bottom.Children.Add(btnRow);

        var firstPwdWithQuestion = _methods.FirstOrDefault(m => m.Kind == AuthMethodKind.Password && !string.IsNullOrEmpty(m.GetPasswordOptions().SecurityQuestion));
        if (_allowRecovery && firstPwdWithQuestion != null)
        {
            var forget = new TextBlock
            {
                Text = "忘记密码？",
                Foreground = new SolidColorBrush(ThemeService.IsDark ? Color.Parse("#60A5FA") : Color.Parse("#2563EB")),
                FontSize = 12,
                Cursor = new Cursor(StandardCursorType.Hand),
                Margin = new Thickness(0, 4, 0, 0)
            };
            forget.PointerPressed += (_, _) => ToggleRecovery(firstPwdWithQuestion);
            bottom.Children.Add(forget);
        }

        right.Children.Add(bottom);

        grid.Children.Add(left);
        grid.Children.Add(right);
        Content = grid;

        BuildMethodList();
        SelectMethod(0, false);
        if (_requireTwo && _titleText != null)
            _titleText.Text = "已启用双因子验证：请先用一种方式验证，再换一种不同的方式完成解锁。";

        Loaded += (_, _) =>
        {
            TryFocusInput();
            if (_methods.Count > 0 && _methods[0].Kind == AuthMethodKind.QuickFace) _ = TryFaceVerify(auto: true);
        };
        _lockTimer.Tick += (_, _) => UpdateLockCountdown();
        Closed += (_, _) => Cleanup();
        RefreshLockoutUi();
    }

    public static PasswordWindow Create(string operation, string title, string prompt, string storedHash, string? question, string? answerHash, PeekShieldEngine? engine, PeekShieldSettings settings, bool quickVerify)
    {
        var methods = settings.GetAuthMethodsForOperation(operation);
        if (methods.Count == 0)
        {
            var fallback = new List<AuthMethodEntry>();
            if (settings.PasswordEnabled) fallback.Add(new AuthMethodEntry { Id = "pwd", Kind = AuthMethodKind.Password });
            if (settings.FaceUnlockEnabled && !settings.QuickVerifyEnabled) fallback.Add(new AuthMethodEntry { Id = "face", Kind = AuthMethodKind.Face });
            if (settings.QuickVerifyEnabled) fallback.Add(new AuthMethodEntry { Id = "quickface", Kind = AuthMethodKind.QuickFace });
            if (settings.SystemUnlockEnabled) fallback.Add(new AuthMethodEntry { Id = "sys", Kind = AuthMethodKind.System });
            if (settings.UsbUnlockEnabled) fallback.Add(new AuthMethodEntry { Id = "usb", Kind = AuthMethodKind.Usb });
            methods = fallback;
        }
        methods = methods.Where(m => IsMethodAvailable(m, engine)).ToList();
        if (methods.Count == 0 && settings.PasswordEnabled)
            methods = new List<AuthMethodEntry> { new AuthMethodEntry { Id = "pwd", Kind = AuthMethodKind.Password } };
        quickVerify = methods.Count > 0 && methods[0].Kind == AuthMethodKind.QuickFace;
        return new PasswordWindow(operation, title, prompt, storedHash, question, answerHash, engine, settings, methods, quickVerify);
    }

    public static PasswordWindow CreatePasswordOnly(string title, string prompt, string storedHash, string? question, string? answerHash, PeekShieldEngine? engine, PeekShieldSettings settings)
    {
        var methods = new List<AuthMethodEntry> { new AuthMethodEntry { Id = "adminpwd", Kind = AuthMethodKind.Password, Name = "管理员密码" } };
        return new PasswordWindow("Install", title, prompt, storedHash, question, answerHash, engine, settings, methods, false, false);
    }

    private static bool IsMethodAvailable(AuthMethodEntry m, PeekShieldEngine? engine)
    {
        return m.Kind switch
        {
            AuthMethodKind.Face => engine != null && engine.IsFaceAuthEnrolled(m.Id),
            AuthMethodKind.QuickFace => engine != null && engine.QuickVerifyAvailable,
            AuthMethodKind.System => OperatingSystem.IsWindows(),
            AuthMethodKind.Usb => true,
            AuthMethodKind.Password => true,
            _ => true
        };
    }

    public static async Task<Outcome> ShowVerify(Window? owner, string operation, string title, string prompt, string storedHash, string? question, string? answerHash, PeekShieldEngine? engine, PeekShieldSettings settings, bool quickVerify)
    {
        var w = Create(operation, title, prompt, storedHash, question, answerHash, engine, settings, quickVerify);
        if (owner != null) await w.ShowDialog(owner);
        else w.Show();
        return w.Result;
    }

    private static string KindLabel(AuthMethodKind k) => k switch
    {
        AuthMethodKind.Password => "密码",
        AuthMethodKind.Face => "人脸识别",
        AuthMethodKind.QuickFace => "快捷验证",
        AuthMethodKind.System => "系统解锁",
        AuthMethodKind.Usb => "U盘",
        _ => "未知"
    };

    private void BuildMethodList()
    {
        if (_methodList == null) return;
        _methodList.Children.Clear();
        _methodButtons.Clear();
        _methodLabels.Clear();
        for (int i = 0; i < _methods.Count; i++)
        {
            var m = _methods[i];
            var label = m.Kind == AuthMethodKind.Password && !string.IsNullOrEmpty(m.Name) ? m.Name : $"用{KindLabel(m.Kind)}继续";
            _methodLabels.Add(label);
            var btn = new Button
            {
                Content = label,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 8),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0)
            };
            var idx = i;
            btn.Click += (_, _) => SelectMethod(idx, true);
            btn.PointerEntered += (_, _) => OnMethodHover(btn, idx, true);
            btn.PointerExited += (_, _) => OnMethodHover(btn, idx, false);
            _methodList.Children.Add(btn);
            _methodButtons.Add(btn);
        }
    }

    private void OnMethodHover(Button btn, int index, bool entered)
    {
        if (index == _selectedIndex) return;
        if (entered)
        {
            btn.Background = Palette.ButtonBg;
            btn.Foreground = Palette.TextPrimary;
        }
        else
        {
            btn.Background = Brushes.Transparent;
            btn.Foreground = Palette.TextPrimary;
        }
    }

    private void SelectMethod(int index, bool userAction = false)
    {
        if (index < 0 || index >= _methods.Count) return;
        _selectedIndex = index;
        _selectedMethod = _methods[index];
        _selectedKind = _selectedMethod.Kind;
        for (int i = 0; i < _methodButtons.Count; i++)
        {
            var b = _methodButtons[i];
            if (i == index)
            {
                b.Background = Palette.AccentBg;
                b.Foreground = Palette.AccentFg;
            }
            else
            {
                b.Background = Brushes.Transparent;
                b.Foreground = Palette.TextPrimary;
            }
        }
        RenderContent();
        TryFocusInput();
        if (_selectedKind == AuthMethodKind.QuickFace || (userAction && _selectedKind == AuthMethodKind.Face)) _ = TryFaceVerify(auto: true);
    }

    private void TryFocusInput()
    {
        if (_selectedKind == AuthMethodKind.Password)
        {
            _pwdBox?.Focus();
        }
    }

    private void RenderContent()
    {
        if (_contentHost == null) return;
        _contentHost.Children.Clear();
        _facePreview = null;
        _faceStatus = null;
        _faceRetryBtn = null;
        _pwdBox = null;
        StopFaceVerify();

        var kind = _selectedKind;
        var panel = new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Center };

        if (kind == AuthMethodKind.Password)
        {
            panel.VerticalAlignment = VerticalAlignment.Top;
            _pwdBox = new TextBox
            {
                PasswordChar = '*',
                Watermark = "请输入密码",
                FontSize = 14,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = Palette.CardBg,
                Foreground = Palette.TextPrimary,
                BorderBrush = Palette.Border,
                BorderThickness = new Thickness(1)
            };
            _pwdBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) TrySubmit(); };
            panel.Children.Add(_pwdBox);

            var ok = new Button { Content = "确定", MinWidth = 96, Background = new SolidColorBrush(Color.Parse("#2563EB")), Foreground = new SolidColorBrush(Colors.White), Padding = new Thickness(10, 5) };
            ok.Click += (_, _) => TrySubmit();
            panel.Children.Add(ok);
        }
        else if (kind == AuthMethodKind.Face || kind == AuthMethodKind.QuickFace)
        {
            _facePreview = new Image
            {
                Width = 280,
                Height = 210,
                Stretch = Stretch.Fill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var previewBorder = new Border
            {
                Width = 280,
                Height = 210,
                Background = new SolidColorBrush(Color.Parse("#1F2937")),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = _facePreview
            };
            panel.Children.Add(previewBorder);

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            _faceStatus = new TextBlock { Text = "人脸识别验证中…", VerticalAlignment = VerticalAlignment.Center };
            _faceRetryBtn = new Button { Content = "手动重新扫描", Padding = new Thickness(8, 4) };
            _faceRetryBtn.Click += (_, _) => _ = TryFaceVerify(auto: false);
            row.Children.Add(_faceStatus);
            row.Children.Add(_faceRetryBtn);
            panel.Children.Add(row);
        }
        else if (kind == AuthMethodKind.System)
        {
            var txt = new TextBlock { Text = "使用 Windows 系统凭据（PIN / 指纹 / 面容 / 密码）完成验证。", TextWrapping = TextWrapping.Wrap, Foreground = Palette.TextSecondary };
            panel.Children.Add(txt);
            var sys = new Button { Content = "使用系统解锁", MinWidth = 120, Padding = new Thickness(10, 5), Background = new SolidColorBrush(Color.Parse("#7C3AED")), Foreground = new SolidColorBrush(Colors.White) };
            sys.Click += (_, _) => _ = TrySystemUnlock();
            panel.Children.Add(sys);
        }
        else if (kind == AuthMethodKind.Usb)
        {
            var txt = new TextBlock { Text = "请插入已登记的U盘开始验证。", TextWrapping = TextWrapping.Wrap, Foreground = Palette.TextSecondary };
            panel.Children.Add(txt);
            var usb = new Button { Content = "扫描U盘", MinWidth = 120, Padding = new Thickness(10, 5), Background = new SolidColorBrush(Color.Parse("#0EA5E9")), Foreground = new SolidColorBrush(Colors.White) };
            usb.Click += (_, _) => _ = TryUsbUnlock();
            panel.Children.Add(usb);
        }

        _contentHost.Children.Add(panel);
    }

    private static string FactorCategory(AuthMethodKind k) => k switch
    {
        AuthMethodKind.Password => "knowledge",
        AuthMethodKind.Face => "biometric",
        AuthMethodKind.QuickFace => "biometric",
        AuthMethodKind.System => "platform",
        AuthMethodKind.Usb => "possession",
        _ => "other"
    };

    private bool RecordFactor(AuthMethodKind kind)
    {
        _passedKinds.Add(kind);
        _passedCategories.Add(FactorCategory(kind));
        return _requireTwo ? _passedCategories.Count >= 2 : true;
    }

    private void OnFactorSatisfied(AuthMethodKind kind)
    {
        _busy = false;
        SetAllEnabled(true);
        if (RecordFactor(kind))
        {
            FinalizeUnlock();
            return;
        }
        SecurityService.ClearSessionUnlock();
        var remainingKinds = string.Join("、", _methods.Where(m => !_passedKinds.Contains(m.Kind)).Select(m => KindLabel(m.Kind)).Distinct());
        if (_titleText != null)
            _titleText.Text = $"双因子验证进行中：已通过{KindLabel(kind)}（{_passedCategories.Count}/2），请继续使用{remainingKinds}验证。";
        if (_hint != null)
        {
            _hint.Text = $"已通过{KindLabel(kind)}，还需另一种不同的验证方式才能完成解锁。剩余可选：{remainingKinds}。";
            _hint.IsVisible = true;
        }
        RefreshMethodListPassed();
        AdvanceToUnpassedKind();
        TryFocusInput();
    }

    private void FinalizeUnlock()
    {
        if (_passedKinds.Contains(AuthMethodKind.Password))
            SecurityService.SetSessionUnlocked();
        if (_passedKinds.Contains(AuthMethodKind.Face) || _passedKinds.Contains(AuthMethodKind.QuickFace))
            SecurityService.SetFaceUnlocked();
        if (_passedKinds.Contains(AuthMethodKind.System))
            SecurityService.SetSystemUnlocked();
        if (_passedKinds.Contains(AuthMethodKind.Usb))
            SecurityService.SetUsbUnlocked();
        StopLockTimer();
        Result = Outcome.Ok;
        Close();
    }

    private void RefreshMethodListPassed()
    {
        for (int i = 0; i < _methodButtons.Count && i < _methodLabels.Count; i++)
        {
            var b = _methodButtons[i];
            var baseLabel = _methodLabels[i];
            b.Content = _passedKinds.Contains(_methods[i].Kind) ? baseLabel + " ✅" : baseLabel;
        }
    }

    private void AdvanceToUnpassedKind()
    {
        for (int i = 0; i < _methods.Count; i++)
        {
            if (!_passedKinds.Contains(_methods[i].Kind))
            {
                SelectMethod(i, true);
                return;
            }
        }
    }

    private void TrySubmit()
    {
        var input = _pwdBox?.Text ?? "";
        var opts = _selectedMethod?.GetPasswordOptions();
        var stored = !string.IsNullOrEmpty(opts?.PasswordHash) ? opts.PasswordHash : _stored;
        if (SecurityService.TryUnlock(stored, input))
        {
            StopLockTimer();
            OnFactorSatisfied(AuthMethodKind.Password);
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
        if (_pwdBox != null) _pwdBox.Text = "";
    }

    private async Task TryFaceVerify(bool auto)
    {
        if (_busy || _engine == null || _faceStatus == null) return;
        _busy = true;
        _faceStatus.Text = "人脸识别验证中…";
        if (_facePreview != null) _facePreview.Source = null;
        _faceCts = new CancellationTokenSource();
        var cts = _faceCts;
        try
        {
            var methodId = _selectedMethod?.Id ?? "";
            bool ok = await _engine.VerifyFaceAuthAsync(
                methodId,
                msg => Dispatcher.UIThread.Post(() => { if (_faceStatus != null && !cts.IsCancellationRequested) _faceStatus.Text = msg; }),
                mat => Dispatcher.UIThread.Post(() => UpdatePreview(mat)));
            if (cts.IsCancellationRequested) return;
            if (ok)
            {
                OnFactorSatisfied(_selectedKind);
                return;
            }
            _faceStatus.Text = auto ? "未识别到机主，请手动点击重新扫描。" : "人脸未匹配，请重试。";
        }
        catch (Exception ex)
        {
            LoggerService.LogInfo("人脸解锁弹窗异常：" + ex.Message);
            _faceStatus.Text = "人脸验证异常，请换用其它方式。";
        }
        finally
        {
            _busy = false;
            if (_faceCts == cts) _faceCts = null;
        }
    }

    private void UpdatePreview(OpenCvSharp.Mat mat)
    {
        try
        {
            if (_facePreview == null || mat == null || mat.Empty()) return;
            var bytes = mat.ToBytes(".png");
            if (bytes == null || bytes.Length == 0) return;
            using var ms = new MemoryStream(bytes);
            var old = _facePreview.Source as IDisposable;
            _facePreview.Source = new Bitmap(ms);
            old?.Dispose();
        }
        catch { }
        finally
        {
            mat.Dispose();
        }
    }

    private void StopFaceVerify()
    {
        _faceCts?.Cancel();
        _faceCts = null;
    }

    private async Task TrySystemUnlock()
    {
        if (_busy) return;
        _busy = true;
        SetAllEnabled(false);
        if (_hint != null) { _hint.Text = "正在调用系统验证…"; _hint.IsVisible = true; }
        var (ok, msg) = await SystemUnlockService.VerifyAsync("窥屿盾需要验证以解锁");
        if (ok)
        {
            OnFactorSatisfied(AuthMethodKind.System);
            return;
        }
        if (_hint != null) { _hint.Text = msg; _hint.IsVisible = true; }
        SetAllEnabled(true);
        _busy = false;
    }

    private async Task TryUsbUnlock()
    {
        if (_busy) return;
        _busy = true;
        SetAllEnabled(false);
        if (_hint != null) { _hint.Text = "正在检测U盘…"; _hint.IsVisible = true; }
        var method = _methods[_selectedIndex];
        var opts = method.GetUsbOptions();
        bool ok = false;
        try
        {
            ok = await Task.Run(() => UsbUnlockService.TryUnlock(opts.UseFileMode ? opts.TokenHash : opts.SerialHash, opts.UseFileMode));
        }
        catch { ok = false; }
        if (ok)
        {
            OnFactorSatisfied(AuthMethodKind.Usb);
            return;
        }
        if (_hint != null) { _hint.Text = "未检测到已登记的U盘，请插入后重试。"; _hint.IsVisible = true; }
        SetAllEnabled(true);
        _busy = false;
    }

    private void SetAllEnabled(bool enabled)
    {
        _cancelBtn?.SetValue(IsEnabledProperty, enabled);
        foreach (var b in _methodButtons) b.IsEnabled = enabled;
        _pwdBox?.SetValue(IsEnabledProperty, enabled);
        _faceRetryBtn?.SetValue(IsEnabledProperty, enabled);
    }

    private void ToggleRecovery(AuthMethodEntry method)
    {
        if (_contentHost == null || _hint == null) return;
        var opts = method.GetPasswordOptions();
        _contentHost.Children.Clear();
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "保密问题：" + opts.SecurityQuestion, Foreground = Palette.TextSecondary, FontSize = 13, TextWrapping = TextWrapping.Wrap });
        var ans = new TextBox { PasswordChar = '*', Watermark = "请输入保密问题答案", FontSize = 14, VerticalContentAlignment = VerticalAlignment.Center, Background = Palette.CardBg, Foreground = Palette.TextPrimary, BorderBrush = Palette.Border, BorderThickness = new Thickness(1) };
        ans.KeyDown += (_, e) => { if (e.Key == Key.Enter) TryRecovery(ans, method); };
        panel.Children.Add(ans);
        var submit = new Button { Content = "提交", MinWidth = 96, Padding = new Thickness(10, 5) };
        submit.Click += (_, _) => TryRecovery(ans, method);
        panel.Children.Add(submit);
        _contentHost.Children.Add(panel);
    }

    private void TryRecovery(TextBox ans, AuthMethodEntry method)
    {
        var a = ans.Text ?? "";
        var opts = method.GetPasswordOptions();
        if (SecurityService.VerifySecret(opts.SecurityAnswerHash, a) || SecurityService.VerifyAlt(a))
        {
            Result = Outcome.Recovery;
            Close();
            return;
        }
        if (_hint != null) { _hint.Text = "保密问题答案错误。"; _hint.IsVisible = true; }
        ans.Text = "";
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

    private void Cleanup()
    {
        StopLockTimer();
        StopFaceVerify();
        if (_facePreview?.Source is IDisposable d) d.Dispose();
    }
}
