using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using PeekShield.Models;
using PeekShield.Services;

namespace PeekShield.Views;

public sealed class UpdateDialog : Window
{
    private readonly UpdateInfo _info;

    public UpdateDialog(UpdateInfo info, Action? onUpdate = null)
    {
        _info = info;
        Title = info.HasUpdate ? "发现新版本" : "检查更新";
        Width = 520;
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

        var root = new StackPanel { Margin = new Thickness(22, 20, 22, 18), Spacing = 10 };

        var head = new TextBlock
        {
            Text = info.HasUpdate ? "发现新版本 v" + (info.Version ?? info.TagName ?? "?") : "检查更新",
            Foreground = Palette.TextPrimary,
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            TextWrapping = TextWrapping.Wrap
        };
        root.Children.Add(head);

        if (info.HasUpdate)
        {
            root.Children.Add(new TextBlock
            {
                Text = "当前 v" + UpdateService.CurrentVersion + " → 最新 v" + (info.Version ?? info.TagName ?? "?") + "。是否立即在应用内更新（覆盖安装）？",
                Foreground = Palette.TextSecondary,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 19
            });

            var log = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(info.Changelog) ? "（未提供更新说明）" : info.Changelog,
                Foreground = Palette.TextSecondary,
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 18
            };
            var scroller = new ScrollViewer
            {
                Content = log,
                MaxHeight = 230,
                Margin = new Thickness(0, 4, 0, 4),
                BorderBrush = Palette.Border,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10)
            };
            root.Children.Add(scroller);

            var link = new TextBlock
            {
                Text = "查看完整发布页：" + info.ReleaseUrl,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse(ThemeService.IsDark ? "#60A5FA" : "#2563EB")),
                TextWrapping = TextWrapping.Wrap,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            link.PointerPressed += (_, _) => Platform.OpenUrl(info.ReleaseUrl);
            root.Children.Add(link);

            var update = new Button
            {
                Content = "应用内更新",
                MinWidth = 120,
                Padding = new Thickness(14, 6),
                Background = Brush.Parse("#2563EB"),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(4),
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            update.Click += (_, _) => { onUpdate?.Invoke(); Close(); };

            var later = new Button
            {
                Content = "稍后提醒",
                MinWidth = 100,
                Padding = new Thickness(14, 6),
                Background = Palette.ButtonBg,
                Foreground = Palette.TextPrimary,
                BorderBrush = Palette.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                IsCancel = true
            };
            later.Click += (_, _) => Close();

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
            row.Children.Add(later);
            row.Children.Add(update);
            root.Children.Add(row);
        }
        else
        {
            root.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(info.Error)
                    ? "已是最新版本（v" + UpdateService.CurrentVersion + "）。"
                    : info.Error,
                Foreground = Palette.TextSecondary,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 19
            });

            var ok = new Button
            {
                Content = "确定",
                MinWidth = 100,
                Padding = new Thickness(14, 6),
                Background = Palette.ButtonBg,
                Foreground = Palette.TextPrimary,
                BorderBrush = Palette.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                IsDefault = true,
                IsCancel = true
            };
            ok.Click += (_, _) => Close();
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            row.Children.Add(ok);
            root.Children.Add(row);
        }

        Content = root;
    }
}
