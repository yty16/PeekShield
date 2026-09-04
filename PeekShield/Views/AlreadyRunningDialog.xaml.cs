using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using PeekShield.Services;

namespace PeekShield.Views;

public sealed class AlreadyRunningDialog : Window
{
    public enum Choice { None, BringToFront, Cancel, KillAndRelaunch }
    public Choice Result { get; private set; } = Choice.None;

    public AlreadyRunningDialog()
    {
        Title = "PeekShield 已在运行";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
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

        var title = new TextBlock
        {
            Text = "PeekShield 已经在运行。",
            Foreground = Palette.TextPrimary,
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 6),
        };

        var body = new TextBlock
        {
            Text = "如果您无法看到主界面，可能是因为您在托盘菜单里选择了【隐藏主界面】，或者有隐藏主界面的规则正在生效。\n\n本次启动可以唤起当前正在运行的实例，或直接取消。",
            Foreground = Palette.TextSecondary,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
            Margin = new Thickness(0, 0, 0, 14),
        };

        var btnBring = new Button
        {
            Content = "唤起当前实例",
            MinWidth = 120,
            Padding = new Thickness(14, 6),
            Background = Palette.Border,
            Foreground = Palette.TextPrimary,
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        btnBring.Click += (_, _) =>
        {
            Result = Choice.BringToFront;
            Close();
        };

        var btnCancel = new Button
        {
            Content = "取消",
            MinWidth = 96,
            Padding = new Thickness(14, 6),
            Background = Palette.Border,
            Foreground = Palette.TextPrimary,
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            IsCancel = true,
        };
        btnCancel.Click += (_, _) =>
        {
            Result = Choice.Cancel;
            Close();
        };

        var btnKill = new Button
        {
            Content = "结束旧实例并启动新实例",
            MinWidth = 180,
            Padding = new Thickness(14, 6),
            Background = Palette.Border,
            Foreground = Palette.TextPrimary,
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        btnKill.Click += (_, _) =>
        {
            Result = Choice.KillAndRelaunch;
            Close();
        };

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0),
        };
        btnRow.Children.Add(btnKill);
        btnRow.Children.Add(btnBring);
        btnRow.Children.Add(btnCancel);

        var root = new StackPanel
        {
            Margin = new Thickness(22, 20, 22, 18),
            Spacing = 6,
        };
        root.Children.Add(title);
        root.Children.Add(body);
        root.Children.Add(btnRow);

        Content = root;
    }
}
