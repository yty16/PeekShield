using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using PeekShield.Models;

namespace PeekShield.Services;

public class TrayService
{
    private TrayIcon? _tray;
    private NativeMenuItem? _pauseItem;
    private NativeMenuItem? _manualItem;
    private string _baseTooltip = "PeekShield · 就绪";

    public static TrayService? Instance { get; private set; }

    public event Action? OnTogglePause;
    public event Action? OnToggleManual;
    public event Action? OnOpenSettings;
    public event Action? OnPrivacy;
    public event Action? OnSecurity;
    public event Action? OnHideTray;
    public event Action? OnExit;

    public void Start()
    {
        Instance = this;
        if (_tray != null) return;
        _tray = new TrayIcon
        {
            Icon = LoadIcon(),
            ToolTipText = _baseTooltip,
            IsVisible = true
        };
        _tray.Clicked += (_, _) => OnOpenSettings?.Invoke();
        BuildMenu();

        var app = Application.Current;
        if (app != null) TrayIcon.SetIcons(app, new TrayIcons { _tray });
    }

    public void RefreshMenu()
    {
        if (_tray == null) return;
        BuildMenu();
    }

    private void BuildMenu()
    {
        if (_tray == null) return;
        var settings = PeekShieldEngine.Instance?.Settings;
        var configured = settings?.TrayMenuItems;
        var itemList = (configured != null && configured.Count > 0)
            ? configured.ToList()
            : DefaultMenuItems();

        var menu = new NativeMenu();
        foreach (var item in itemList)
        {
            if (!item.Visible) continue;
            var native = CreateNativeItem(item.Id);
            if (native == null) continue;
            menu.Items.Add(native);
        }
        _tray.Menu = menu;
    }

    private NativeMenuItem? CreateNativeItem(string id)
    {
        return id switch
        {
            "open" => CreateItem("打开设置", () => OnOpenSettings?.Invoke()),
            "privacy" => CreateItem("隐私与授权", () => OnPrivacy?.Invoke()),
            "security" => CreateItem("安全设置", () => OnSecurity?.Invoke()),
            "pause" => CreatePauseItem(),
            "manual" => CreateManualItem(),
            "hide" => CreateItem("隐藏托盘图标（后台静默）", () => OnHideTray?.Invoke()),
            "exit" => CreateItem("退出", () => OnExit?.Invoke()),
            _ => null
        };
    }

    private NativeMenuItem CreateItem(string header, Action action)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => action();
        return item;
    }

    private NativeMenuItem CreatePauseItem()
    {
        _pauseItem = new NativeMenuItem("暂停防护");
        _pauseItem.Click += (_, _) => OnTogglePause?.Invoke();
        return _pauseItem;
    }

    private NativeMenuItem CreateManualItem()
    {
        _manualItem = new NativeMenuItem("手动防窥：关");
        _manualItem.Click += (_, _) => OnToggleManual?.Invoke();
        return _manualItem;
    }

    public static List<TrayMenuItemConfig> DefaultMenuItems() => new()
    {
        new() { Id = "open", Visible = true },
        new() { Id = "privacy", Visible = true },
        new() { Id = "security", Visible = true },
        new() { Id = "pause", Visible = true },
        new() { Id = "manual", Visible = true },
        new() { Id = "hide", Visible = true },
        new() { Id = "exit", Visible = true }
    };

    public static string MenuItemLabel(string id) => id switch
    {
        "open" => "打开设置",
        "privacy" => "隐私与授权",
        "security" => "安全设置",
        "pause" => "暂停/恢复防护",
        "manual" => "手动防窥开关",
        "hide" => "隐藏托盘图标",
        "exit" => "退出",
        _ => id
    };

    public void Show() { if (_tray != null) _tray.IsVisible = true; }
    public void Hide() { if (_tray != null) _tray.IsVisible = false; }

    public void Stop()
    {
        if (_tray != null)
        {
            var app = Application.Current;
            if (app != null) TrayIcon.SetIcons(app, null);
            _tray = null;
        }
    }

    public void SetTooltip(string text)
    {
        _baseTooltip = text;
        if (_tray != null) _tray.ToolTipText = text.Length > 127 ? text[..127] : text;
    }

    public void SetPauseLabel(bool paused) { if (_pauseItem != null) _pauseItem.Header = paused ? "恢复防护" : "暂停防护"; }
    public void SetManualLabel(bool on) { if (_manualItem != null) _manualItem.Header = on ? "手动防窥：开" : "手动防窥：关"; }

    public void ShowBalloon(string title, string message)
    {
        if (_tray != null) _tray.ToolTipText = $"{title}：{message}";
    }

    private static WindowIcon? LoadIcon()
    {
        try
        {
            using var s = AssetLoader.Open(new Uri("avares://PeekShield/Resources/icon.png"));
            return new WindowIcon(s);
        }
        catch
        {
            return null;
        }
    }
}
