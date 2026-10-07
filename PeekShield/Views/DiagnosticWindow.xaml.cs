using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using PeekShield.Models;
using PeekShield.Services;

namespace PeekShield.Views;

public sealed class DiagnosticWindow : Window
{
    private readonly PeekShieldSettings _settings;
    private readonly PeekShieldEngine? _engine;
    private readonly StackPanel _list = new() { Spacing = 8 };
    private readonly TextBlock _summary = new();

    public DiagnosticWindow(PeekShieldSettings settings, PeekShieldEngine? engine)
    {
        _settings = settings;
        _engine = engine;

        Title = "诊断自检 · 窥屿盾";
        Width = 580;
        Height = 480;
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

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new TextBlock
        {
            Text = "应用内诊断自检",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = Palette.TextPrimary,
            Margin = new Thickness(0, 0, 0, 4)
        };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        _summary.Foreground = Palette.TextSecondary;
        _summary.FontSize = 12;
        _summary.TextWrapping = TextWrapping.Wrap;
        _summary.Margin = new Thickness(0, 0, 0, 8);
        Grid.SetRow(_summary, 0);
        root.Children.Add(_summary);

        var scroller = new ScrollViewer
        {
            Content = _list,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        Grid.SetRow(scroller, 1);
        root.Children.Add(scroller);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var recheck = new Button { Content = "重新检测", MinWidth = 96, Padding = new Thickness(10, 5) };
        recheck.Click += (_, _) => RunChecks();
        var copy = new Button { Content = "复制报告", MinWidth = 96, Padding = new Thickness(10, 5) };
        copy.Click += (_, _) => CopyReport();
        btnRow.Children.Add(recheck);
        btnRow.Children.Add(copy);
        Grid.SetRow(btnRow, 2);
        root.Children.Add(btnRow);

        Content = root;
        Loaded += (_, _) => RunChecks();
    }

    private void RunChecks()
    {
        _list.Children.Clear();
        var items = DiagnosticService.Run(_settings, _engine);
        int errors = 0, warns = 0;
        var sb = new StringBuilder();
        sb.AppendLine($"窥屿盾诊断报告 @ {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"版本 {BuildConstants.Version} · {Platform.OsLabel}");
        sb.AppendLine();
        foreach (var it in items)
        {
            if (it.Status == DiagnosticStatus.Error) errors++;
            else if (it.Status == DiagnosticStatus.Warn) warns++;
            AddRow(it);
            sb.AppendLine($"[{StatusChar(it.Status)}] {it.Name}：{it.Detail}");
        }
        sb.AppendLine();
        sb.AppendLine($"合计 {items.Count} 项，错误 {errors}，警告 {warns}。");

        _summary.Text = $"共 {items.Count} 项检查：{errors} 个错误，{warns} 个警告。"
            + (errors > 0 ? " 存在需要修复的问题。" : (warns > 0 ? " 基本正常，有少量提示。" : " 全部正常。"));
        _reportText = sb.ToString();

        try
        {
            _settings.LastDiagnosticAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            _settings.Save();
        }
        catch { }
    }

    private string? _reportText;

    private static char StatusChar(DiagnosticStatus s) => s switch
    {
        DiagnosticStatus.Ok => '✓',
        DiagnosticStatus.Warn => '!',
        DiagnosticStatus.Error => '✗',
        _ => '?'
    };

    private static Color DotColor(DiagnosticStatus s) => s switch
    {
        DiagnosticStatus.Ok => Color.Parse("#22C55E"),
        DiagnosticStatus.Warn => Color.Parse("#F59E0B"),
        DiagnosticStatus.Error => Color.Parse("#EF4444"),
        _ => Color.Parse("#9CA3AF")
    };

    private void AddRow(DiagnosticItem it)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("16,8,*"), Margin = new Thickness(0, 4, 0, 4) };
        var dot = new Border
        {
            Width = 12,
            Height = 12,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(DotColor(it.Status)),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(2, 4, 0, 0)
        };
        Grid.SetColumn(dot, 0);
        row.Children.Add(dot);

        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock
        {
            Text = it.Name,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = Palette.TextPrimary
        });
        text.Children.Add(new TextBlock
        {
            Text = it.Detail,
            FontSize = 12,
            Foreground = it.Status == DiagnosticStatus.Error ? Palette.Danger : Palette.TextSecondary,
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetColumn(text, 2);
        row.Children.Add(text);

        _list.Children.Add(row);
    }

    private async void CopyReport()
    {
        if (string.IsNullOrEmpty(_reportText)) return;
        try
        {
            if (Clipboard != null) await Clipboard.SetTextAsync(_reportText);
        }
        catch { }
    }
}
