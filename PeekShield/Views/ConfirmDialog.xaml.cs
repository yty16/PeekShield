using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using PeekShield.Services;

namespace PeekShield.Views;

public sealed class ConfirmDialog : Window
{
    public bool Confirmed { get; private set; }

    public ConfirmDialog(string title, string message, string confirmText = "确定", string cancelText = "取消")
    {
        Title = title;
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
            Text = title,
            Foreground = Palette.TextPrimary,
            FontSize = 15,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };

        var body = new TextBlock
        {
            Text = message,
            Foreground = Palette.TextSecondary,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 20,
            Margin = new Thickness(0, 0, 0, 14)
        };

        var ok = new Button
        {
            Content = confirmText,
            MinWidth = 110,
            Padding = new Thickness(14, 6),
            Background = Palette.Danger,
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        ok.Click += (_, _) => { Confirmed = true; Close(); };

        var cancel = new Button
        {
            Content = cancelText,
            MinWidth = 90,
            Padding = new Thickness(14, 6),
            Background = Palette.ButtonBg,
            Foreground = Palette.TextPrimary,
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            IsCancel = true
        };
        cancel.Click += (_, _) => Close();

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        row.Children.Add(cancel);
        row.Children.Add(ok);

        var root = new StackPanel { Margin = new Thickness(22, 20, 22, 18) };
        root.Children.Add(head);
        root.Children.Add(body);
        root.Children.Add(row);

        Content = root;
    }
}
