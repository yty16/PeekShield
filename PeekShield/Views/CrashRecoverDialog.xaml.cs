using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using PeekShield.Services;

namespace PeekShield.Views;

public sealed class CrashRecoverDialog : Window
{
    public bool? RecoverChoice { get; private set; }

    public CrashRecoverDialog()
    {
        Title = "异常退出恢复";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        CanResize = false;
        SystemDecorations = SystemDecorations.Full;
        Background = Palette.PageBg;

        try
        {
            using var s = AssetLoader.Open(new Uri("avares://PeekShield/Resources/icon.png"));
            Icon = new WindowIcon(s);
        }
        catch { }

        FontFamily = new FontFamily(
            OperatingSystem.IsWindows() ? "Microsoft YaHei UI" :
            OperatingSystem.IsMacOS() ? "PingFang SC" :
            "Noto Sans CJK SC");

        var head = new TextBlock
        {
            Text = "窥屿盾检测到软件崩溃",
            Foreground = Palette.TextPrimary,
            FontSize = 15,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var report = CrashReportService.Read();
        var reason = report?.Reason ?? "软件在运行时崩溃了。";
        var logText = report?.LogTail ?? "（没有找到崩溃时的日志）";
        CrashReportService.Clear();

        var reasonBox = new Border
        {
            Background = CardBgReason,
            BorderBrush = Palette.Accent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 0, 0, 6)
        };
        reasonBox.Child = new TextBlock
        {
            Text = reason,
            Foreground = Palette.TextPrimary,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20
        };

        var logTitle = new TextBlock
        {
            Text = "崩溃时的运行日志（供排查）",
            Foreground = Palette.TextMuted,
            FontSize = 12,
            Margin = new Thickness(0, 4, 0, 4)
        };

        var logBox = new TextBox
        {
            Text = logText,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas, Microsoft YaHei UI, monospace"),
            FontSize = 12,
            Foreground = Palette.TextSecondary,
            Background = Palette.PageBg,
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            MaxHeight = 220,
            Margin = new Thickness(0, 0, 0, 14)
        };

        var restart = new Button
        {
            Content = "重启应用",
            MinWidth = 130,
            Padding = new Thickness(14, 6),
            Background = Palette.Accent,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            IsDefault = true
        };
        restart.Click += (_, _) => { RecoverChoice = true; LoggerService.LogInfo("恢复提示：用户选择「重启应用」"); Close(true); };

        var exit = new Button
        {
            Content = "关闭应用",
            MinWidth = 110,
            Padding = new Thickness(14, 6),
            Background = Palette.ButtonBg,
            Foreground = Palette.TextPrimary,
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            IsCancel = true
        };
        exit.Click += (_, _) => { RecoverChoice = false; LoggerService.LogInfo("恢复提示：用户选择「关闭应用」"); Close(false); };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        row.Children.Add(exit);
        row.Children.Add(restart);

        var root = new StackPanel { Margin = new Thickness(22, 20, 22, 18) };
        root.Children.Add(head);
        root.Children.Add(reasonBox);
        root.Children.Add(logTitle);
        root.Children.Add(logBox);
        root.Children.Add(row);

        Content = root;
    }

    private static ISolidColorBrush CardBgReason =>
        (ISolidColorBrush)Brush.Parse(ThemeService.IsDark ? "#1E2733" : "#E8F0FE");
}
