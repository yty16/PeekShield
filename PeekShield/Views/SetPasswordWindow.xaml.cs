using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using PeekShield.Models;
using PeekShield.Services;

namespace PeekShield.Views;

public sealed class SetPasswordWindow : Window
{
    private readonly PeekShieldSettings _s;

    private TextBox? _pwd;
    private TextBox? _confirm;
    private TextBox? _qBox;
    private TextBox? _aBox;
    private CheckBox? _qCheck;
    private StackPanel? _qPanel;
    private CheckBox? _exitChk;
    private CheckBox? _uninstallChk;
    private CheckBox? _openMainChk;
    private CheckBox? _openSecChk;
    private TextBlock? _hint;
    private TextBlock? _uninstallWarn;

    public SetPasswordWindow(PeekShieldSettings s, string title)
    {
        _s = s;
        Title = title;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        CanResize = false;
        Background = Palette.PageBg;
        FontFamily = new FontFamily(
            OperatingSystem.IsWindows() ? "Microsoft YaHei UI" :
            OperatingSystem.IsMacOS() ? "PingFang SC" : "Noto Sans CJK SC");

        try
        {
            using var st = AssetLoader.Open(new Uri("avares://PeekShield/Resources/icon.png"));
            Icon = new WindowIcon(st);
        }
        catch { }

        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(14) };

        panel.Children.Add(MakeLabel("设置密码（至少 4 位）"));
        _pwd = new TextBox { PasswordChar = '*', Watermark = "请输入密码", FontSize = 14, VerticalContentAlignment = VerticalAlignment.Center };
        panel.Children.Add(_pwd);
        _confirm = new TextBox { PasswordChar = '*', Watermark = "请再次输入密码", FontSize = 14, VerticalContentAlignment = VerticalAlignment.Center };
        panel.Children.Add(_confirm);

        var qToggle = new CheckBox { Content = "设置保密问题（防止忘记密码时无法找回）", Margin = new Thickness(0, 6, 0, 0) };
        _qCheck = qToggle;
        qToggle.IsCheckedChanged += (_, _) => { if (_qPanel != null) _qPanel.IsVisible = qToggle.IsChecked == true; };
        panel.Children.Add(qToggle);

        _qPanel = new StackPanel { Spacing = 6, IsVisible = false, Margin = new Thickness(0, 2, 0, 0) };
        _qBox = new TextBox { Watermark = "保密问题，如：我小学的名字？", FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center };
        _qPanel.Children.Add(_qBox);
        _aBox = new TextBox { PasswordChar = '*', Watermark = "保密问题答案", FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center };
        _qPanel.Children.Add(_aBox);
        panel.Children.Add(_qPanel);

        panel.Children.Add(MakeLabel("密码保护范围（勾选后在对应操作前需验证密码）"));
        _exitChk = MakeScope("退出应用", false);
        _uninstallChk = MakeScope("卸载应用", false);
        _openMainChk = MakeScope("打开主页面", false);
        _openSecChk = MakeScope("打开安全设置", true);
        panel.Children.Add(_exitChk);
        panel.Children.Add(_uninstallChk);
        panel.Children.Add(_openMainChk);
        panel.Children.Add(_openSecChk);

        _uninstallChk.IsCheckedChanged += (_, _) => UpdateUninstallWarn();
        _uninstallWarn = new TextBlock
        {
            Foreground = Palette.Danger,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false
        };
        panel.Children.Add(_uninstallWarn);

        panel.Children.Add(new TextBlock
        {
            Foreground = Palette.TextMuted,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Text = "请牢记密码，系统仅保存密码的哈希、不会存储明文，遗忘后将无法找回（除非设置保密问题）。"
        });

        _hint = new TextBlock { Foreground = Palette.Danger, FontSize = 12, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        panel.Children.Add(_hint);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        var ok = new Button { Content = "确定", MinWidth = 96, Background = new SolidColorBrush(Color.Parse("#2563EB")), Foreground = new SolidColorBrush(Colors.White), Padding = new Thickness(10, 5) };
        ok.Click += (_, _) => Apply();
        var cancel = new Button { Content = "取消", MinWidth = 96, Padding = new Thickness(10, 5) };
        cancel.Click += (_, _) => Close();
        row.Children.Add(ok);
        row.Children.Add(cancel);
        panel.Children.Add(row);

        Content = panel;
        Loaded += (_, _) => _pwd?.Focus();
    }

    public bool ShowWithResult(Window owner)
    {
        ShowDialog(owner);
        return _s.PasswordEnabled;
    }

    private CheckBox MakeScope(string label, bool isChecked)
    {
        return new CheckBox { Content = label, IsChecked = isChecked, Margin = new Thickness(0, 2, 0, 2) };
    }

    private void UpdateUninstallWarn()
    {
        if (_uninstallWarn == null) return;
        bool on = _uninstallChk?.IsChecked == true;
        _uninstallWarn.Text = on ? "⚠ 启用卸载保护后，若忘记密码且未设置保密问题，将无法卸载本软件。" : "";
        _uninstallWarn.IsVisible = on;
    }

    private static TextBlock MakeLabel(string text) => new()
    {
        Text = text,
        FontSize = 13,
        FontWeight = FontWeight.SemiBold,
        Foreground = Palette.TextSecondary,
        Margin = new Thickness(0, 8, 0, 2)
    };

    private void Apply()
    {
        var pwd = _pwd?.Text ?? "";
        var confirm = _confirm?.Text ?? "";
        if (pwd.Length < 4) { ShowHint("密码至少 4 位。"); return; }
        if (pwd != confirm) { ShowHint("两次输入的密码不一致。"); return; }

        bool useQ = _qCheck?.IsChecked == true;
        string q = "", ah = "";
        if (useQ)
        {
            q = _qBox?.Text?.Trim() ?? "";
            var a = _aBox?.Text ?? "";
            if (q.Length == 0 || a.Length == 0) { ShowHint("请同时填写保密问题与答案。"); return; }
            ah = SecurityService.HashSecret(a);
        }

        _s.PasswordEnabled = true;
        _s.PasswordHash = SecurityService.HashSecret(pwd);
        _s.SecurityQuestion = q;
        _s.SecurityAnswerHash = ah;
        SecurityService.ResetLockout();
        _s.ProtectExit = _exitChk?.IsChecked == true;
        _s.ProtectUninstall = _uninstallChk?.IsChecked == true;
        _s.ProtectOpenMain = _openMainChk?.IsChecked == true;
        _s.ProtectOpenSecurity = _openSecChk?.IsChecked == true;
        _s.Save();
        Close();
    }

    private void ShowHint(string text)
    {
        if (_hint != null) { _hint.Text = text; _hint.IsVisible = true; }
        if (_pwd != null) _pwd.Text = "";
        if (_confirm != null) _confirm.Text = "";
    }
}
