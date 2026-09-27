using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly AuthMethodEntry? _entry;
    private readonly bool _isNew;
    private TextBox? _pwd;
    private TextBox? _confirm;
    private TextBox? _qBox;
    private TextBox? _aBox;
    private CheckBox? _qCheck;
    private StackPanel? _qPanel;
    private TextBlock? _hint;

    public SetPasswordWindow(PeekShieldSettings s, AuthMethodEntry? entry = null, string title = "设置密码")
    {
        _s = s;
        _entry = entry;
        _isNew = entry == null;
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

        panel.Children.Add(MakeLabel(_isNew ? "设置新密码（至少 4 位）" : "重新设置密码（至少 4 位）"));
        _pwd = new TextBox { PasswordChar = '*', Watermark = "请输入密码", FontSize = 14, VerticalContentAlignment = VerticalAlignment.Center };
        panel.Children.Add(_pwd);
        _confirm = new TextBox { PasswordChar = '*', Watermark = "请再次输入密码", FontSize = 14, VerticalContentAlignment = VerticalAlignment.Center };
        panel.Children.Add(_confirm);

        var qToggle = new CheckBox { Content = "设置保密问题（防止忘记密码时无法找回）", Margin = new Thickness(0, 6, 0, 0) };
        _qCheck = qToggle;
        var opts = entry?.GetPasswordOptions();
        if (!string.IsNullOrEmpty(opts?.SecurityQuestion))
        {
            qToggle.IsChecked = true;
        }
        qToggle.IsCheckedChanged += (_, _) => { if (_qPanel != null) _qPanel.IsVisible = qToggle.IsChecked == true; };
        panel.Children.Add(qToggle);

        _qPanel = new StackPanel { Spacing = 6, IsVisible = qToggle.IsChecked == true, Margin = new Thickness(0, 2, 0, 0) };
        _qBox = new TextBox { Watermark = "保密问题，如：我小学的名字？", FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center, Text = opts?.SecurityQuestion ?? "" };
        _qPanel.Children.Add(_qBox);
        _aBox = new TextBox { PasswordChar = '*', Watermark = "保密问题答案", FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center };
        _qPanel.Children.Add(_aBox);
        panel.Children.Add(_qPanel);

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
        return _s.AuthMethods.Exists(m => m.Kind == AuthMethodKind.Password && m.Operations.Count > 0);
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

        var newOpts = new AuthPasswordOptions
        {
            PasswordHash = SecurityService.HashSecret(pwd),
            SecurityQuestion = q,
            SecurityAnswerHash = ah
        };

        if (_entry == null)
        {
            var count = _s.AuthMethods.Count(m => m.Kind == AuthMethodKind.Password);
            var isAdmin = count == 0;
            var entry = new AuthMethodEntry
            {
                Kind = AuthMethodKind.Password,
                Name = isAdmin ? "管理员密码" : $"用户密码 {count + 1}",
                Operations = BuildDefaultOperations(isAdmin)
            };
            entry.SetPasswordOptions(newOpts);
            _s.AuthMethods.Add(entry);
        }
        else
        {
            _entry.SetPasswordOptions(newOpts);
            // 修改密码时不改动保护范围，保护范围在 AuthMethodsDialog 统一管理
        }
        SecurityService.ResetLockout();
        _s.SyncLegacyAuthBooleans();
        _s.Save();
        Close();
    }

    private List<string> BuildDefaultOperations(bool isAdmin)
    {
        var ops = new List<string>();
        if (_s.ProtectExit) ops.Add("Exit");
        if (_s.ProtectUninstall) ops.Add("Uninstall");
        if (_s.ProtectOpenMain) ops.Add("OpenMain");
        if (_s.ProtectOpenSecurity) ops.Add("OpenSecurity");
        if (ops.Count == 0) ops = new List<string> { "Exit", "Uninstall", "OpenMain", "OpenSecurity" };
        return ops;
    }

    private void ShowHint(string text)
    {
        if (_hint != null) { _hint.Text = text; _hint.IsVisible = true; }
        if (_pwd != null) _pwd.Text = "";
        if (_confirm != null) _confirm.Text = "";
    }
}
