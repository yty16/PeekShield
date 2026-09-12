using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using PeekShield.Services;

namespace PeekShield.Views;

public sealed class PasswordWindow : Window
{
    public enum Outcome { None, Ok, Cancelled, Recovery }

    public Outcome Result { get; private set; } = Outcome.None;
    public bool IsAlt { get; private set; }

    private readonly string _stored;
    private readonly string? _question;
    private readonly string? _answerHash;
    private TextBox? _pwd;
    private TextBlock? _hint;
    private StackPanel? _recovery;
    private TextBox? _ans;

    public PasswordWindow(string title, string prompt, string storedHash, string? question = null, string? answerHash = null)
    {
        _stored = storedHash;
        _question = question;
        _answerHash = answerHash;

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
        var ok = new Button { Content = "确定", MinWidth = 96, Background = new SolidColorBrush(Color.Parse("#2563EB")), Foreground = new SolidColorBrush(Colors.White), Padding = new Thickness(10, 5) };
        ok.Click += (_, _) => TrySubmit();
        var cancel = new Button { Content = "取消", MinWidth = 96, Padding = new Thickness(10, 5) };
        cancel.Click += (_, _) => { Result = Outcome.Cancelled; Close(); };
        row.Children.Add(ok);
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

        Content = panel;
        Loaded += (_, _) => _pwd?.Focus();
    }

    public static async Task<Outcome> ShowVerify(Window? owner, string title, string prompt, string storedHash, string? question = null, string? answerHash = null)
    {
        var w = new PasswordWindow(title, prompt, storedHash, question, answerHash);
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
        if (SecurityService.TryUnlock(_stored, input, out var super))
        {
            IsAlt = super;
            Result = Outcome.Ok;
            Close();
            return;
        }
        if (_hint != null) { _hint.Text = "密码错误，请重试。"; _hint.IsVisible = true; }
        if (_pwd != null) _pwd.Text = "";
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
