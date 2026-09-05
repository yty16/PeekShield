using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace PeekShield.Services;

public class TrayService
{
    private TrayIcon? _tray;
    private NativeMenuItem? _pauseItem;
    private NativeMenuItem? _manualItem;
    private string _baseTooltip = "PeekShield · 就绪";

    public event Action? OnTogglePause;
    public event Action? OnToggleManual;
    public event Action? OnOpenSettings;
    public event Action? OnPrivacy;
    public event Action? OnHideTray;
    public event Action? OnExit;

    public void Start()
    {
        if (_tray != null) return;
        _tray = new TrayIcon
        {
            Icon = LoadIcon(),
            ToolTipText = _baseTooltip,
            IsVisible = true
        };
        _tray.Clicked += (_, _) => OnOpenSettings?.Invoke();

        var menu = new NativeMenu();
        var open = new NativeMenuItem("打开设置");
        open.Click += (_, _) => OnOpenSettings?.Invoke();
        var privacy = new NativeMenuItem("隐私与授权");
        privacy.Click += (_, _) => OnPrivacy?.Invoke();
        _pauseItem = new NativeMenuItem("暂停防护");
        _pauseItem.Click += (_, _) => OnTogglePause?.Invoke();
        _manualItem = new NativeMenuItem("手动防窥：关");
        _manualItem.Click += (_, _) => OnToggleManual?.Invoke();
        var hide = new NativeMenuItem("隐藏托盘图标（后台静默）");
        hide.Click += (_, _) => OnHideTray?.Invoke();
        var exit = new NativeMenuItem("退出");
        exit.Click += (_, _) => OnExit?.Invoke();

        menu.Items.Add(open);
        menu.Items.Add(privacy);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(_manualItem);
        menu.Items.Add(hide);
        menu.Items.Add(exit);
        _tray.Menu = menu;

        var app = Application.Current;
        if (app != null) TrayIcon.SetIcons(app, new TrayIcons { _tray });
    }

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
