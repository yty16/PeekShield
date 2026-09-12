using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using PeekShield.Services;

namespace PeekShield.Views;

public sealed class InfoDialog : Window
{
    public InfoDialog(string title, string message, string okText = "我知道了")
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
            Content = okText,
            MinWidth = 110,
            Padding = new Thickness(14, 6),
            Background = new SolidColorBrush(Color.Parse("#2563EB")),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        ok.Click += (_, _) => Close();

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        row.Children.Add(ok);

        var root = new StackPanel { Margin = new Thickness(22, 20, 22, 18) };
        root.Children.Add(head);
        root.Children.Add(body);
        root.Children.Add(row);

        Content = root;
    }
}
