using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PeekShield.Models;
using PeekShield.Services;

namespace PeekShield;

public partial class MainWindow : Window
{
    public static MainWindow? Instance { get; set; }

    private readonly PeekShieldEngine _engine = PeekShieldEngine.Instance;
    private PeekShieldSettings S => _engine.Settings;

    private readonly StackPanel _root = new();
    private ScrollViewer? _scroll;
    private readonly List<ProtectedEntry> _procList = new();
    private readonly List<ProtectedEntry> _titleList = new();

    private TextBlock? _statusText;
    private TextBlock? _enrollHint;
    private TextBlock? _camTestText;
    private ComboBox? _camComboBox;
    private ComboBox? _sensComboBox;
    private ComboBox? _themeComboBox;
    private StackPanel? _procHost;
    private StackPanel? _titleHost;
    private TextBox? _procInput;
    private TextBox? _titleInput;
    private TextBox? _hkModBox;
    private TextBox? _hkKeyBox;
    private Button? _enrollBtn;
    private Button? _clearBtn;
    private Button? _photoBtn;
    private CheckBox? _enableSmartPeekCheck;
    private CheckBox? _pausedCheck;
    private CheckBox? _manualModeCheck;
    private ComboBox? _popPosCombo;
    private NumberField? _popXBox;
    private NumberField? _popYBox;
    private bool _updatingUi;

    private class CamItem
    {
        public int Index;
        public string Name = "";
        public override string ToString() => Name;
    }

    public MainWindow()
    {
        try
        {
            using var s = Avalonia.Platform.AssetLoader.Open(new Uri("avares://PeekShield/Resources/icon.png"));
            Icon = new WindowIcon(s);
        }
        catch { }

        _scroll = new ScrollViewer { Content = _root, Background = Palette.PageBg };
        Content = _scroll;
        Background = Palette.PageBg;

        Title = "窥屿盾—PeekShield";
        MinWidth = 640;
        MinHeight = 520;
        FontFamily = new FontFamily(
            OperatingSystem.IsWindows() ? "Microsoft YaHei UI" :
            OperatingSystem.IsMacOS() ? "PingFang SC" :
            "Noto Sans CJK SC");

        _engine.StatusChanged += OnStatus;
        _engine.SettingsChanged += OnSettingsChanged;
        ThemeService.Changed += RebuildUi;

        Build();
        RefreshStatus();
    }

    public static void ShowSettings()
    {
        Instance?.Show();
    }

    private void Build()
    {
        _root.Spacing = 4;
        _root.Margin = new Thickness(8);

        _statusText = new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(10, 10, 10, 4)
        };
        _root.Children.Add(_statusText);

        BuildAppearanceSection();
        BuildEnrollSection();
        BuildCameraSection();
        BuildSensitivitySection();
        BuildActionsSection();
        BuildProtectSection();
        BuildSuppressSection();
        BuildAdvancedSection();
        BuildMasterSection();

        var exitCard = AddCard("退出");
        var exitNote = new TextBlock
        {
            Text = "关闭窗口会最小化到系统托盘后台运行；如需完全退出请点下方按钮。",
            FontSize = 12,
            Foreground = Palette.TextMuted,
            TextWrapping = TextWrapping.Wrap
        };
        var exitBtn = MakeButton("退出程序", (_) => App.RequestExit(), Palette.Danger);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        row.Children.Add(exitBtn);
        exitCard.Children.Add(exitNote);
        exitCard.Children.Add(row);
    }

    private StackPanel AddCard(string title)
    {
        var body = new StackPanel { Spacing = 6 };
        var header = new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = Palette.TextPrimary,
            Margin = new Thickness(0, 0, 0, 4)
        };
        var inner = new StackPanel { Spacing = 6, Children = { header, body } };
        var card = new Border
        {
            Background = Palette.CardBg,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = new Thickness(6, 4, 6, 4),
            Child = inner
        };
        _root.Children.Add(card);
        return body;
    }

    private void BuildAppearanceSection()
    {
        var body = AddCard("外观");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _themeComboBox = new ComboBox { Width = 200, Margin = new Thickness(0, 4, 0, 0) };
        _themeComboBox.Items.Add("跟随系统");
        _themeComboBox.Items.Add("明亮");
        _themeComboBox.Items.Add("深色");
        _themeComboBox.SelectedIndex = S.ThemeMode == ThemeMode.Light ? 1 : S.ThemeMode == ThemeMode.Dark ? 2 : 0;
        _themeComboBox.SelectionChanged += (_, _) =>
        {
            var m = _themeComboBox.SelectedIndex switch { 1 => ThemeMode.Light, 2 => ThemeMode.Dark, _ => ThemeMode.System };
            S.ThemeMode = m;
            S.Save();
            ThemeService.SetMode(m);
        };
        row.Children.Add(_themeComboBox);
        body.Children.Add(row);
        body.Children.Add(new TextBlock
        {
            FontSize = 12,
            Foreground = Palette.TextMuted,
            Margin = new Thickness(0, 4, 0, 0),
            Text = "默认跟随系统外观，可手动固定为明亮或深色。"
        });
    }

    private void BuildEnrollSection()
    {
        var body = AddCard("人脸录入（本地存储，禁止上传）");
        _enrollHint = new TextBlock
        {
            FontSize = 12,
            Foreground = Palette.TextMuted,
            TextWrapping = TextWrapping.Wrap,
            Text = _engine.IsEnrolled ? "已录入机主人脸，可重新录入或清空。" : "尚未录入，可点击「录入人脸」正对摄像头，或点「上传照片录入」选择一张正脸照片完成录入。"
        };
        body.Children.Add(_enrollHint);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        _enrollBtn = MakeButton(_engine.IsEnrolled ? "重新录入" : "录入人脸", async (_) => await DoEnroll());
        _clearBtn = MakeButton("清空人脸数据", (_) => { _engine.ClearEnrollment(); RefreshStatus(); UpdateEnrollHint(); RefreshEnrollButton(); });
        _photoBtn = MakeButton("上传照片录入", async (_) => await DoEnrollPhoto());
        row.Children.Add(_enrollBtn);
        row.Children.Add(_clearBtn);
        row.Children.Add(_photoBtn);
        body.Children.Add(row);
    }

    private async Task DoEnroll()
    {
        if (_enrollBtn != null) _enrollBtn.IsEnabled = false;
        if (_clearBtn != null) _clearBtn.IsEnabled = false;
        UpdateEnrollHint("录入中… 请正对摄像头保持静止（约 3 秒）");
        bool ok = await _engine.EnrollAsync(12, (n) => UpdateEnrollHint($"已采集 {n} 张人脸样本…"));
        UpdateEnrollHint(ok ? "✓ 录入成功" : "✗ 录入失败：未采集到足够清晰的人脸，请重试");
        if (_enrollBtn != null) _enrollBtn.IsEnabled = true;
        if (_clearBtn != null) _clearBtn.IsEnabled = true;
        RefreshStatus();
        RefreshEnrollButton();
    }

    private async Task DoEnrollPhoto()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择一张包含你正脸的人脸照片",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("图片") { Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.bmp" } } }
        });
        if (files == null || files.Count == 0) return;
        var path = files[0].Path.LocalPath;
        if (_enrollBtn != null) _enrollBtn.IsEnabled = false;
        if (_clearBtn != null) _clearBtn.IsEnabled = false;
        if (_photoBtn != null) _photoBtn.IsEnabled = false;
        UpdateEnrollHint("照片录入中… 正在本地分析人脸特征");
        bool ok = await _engine.EnrollFromPhotoAsync(path, (n) => UpdateEnrollHint($"已生成 {n} 个人脸特征样本…"));
        UpdateEnrollHint(ok ? "✓ 照片录入成功" : "✗ 未从照片中检测到清晰正脸，请换一张重新上传");
        if (_enrollBtn != null) _enrollBtn.IsEnabled = true;
        if (_clearBtn != null) _clearBtn.IsEnabled = true;
        if (_photoBtn != null) _photoBtn.IsEnabled = true;
        RefreshStatus();
        RefreshEnrollButton();
    }

    private void UpdateEnrollHint(string? text = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_enrollHint == null) return;
            _enrollHint.Text = text ?? (_engine.IsEnrolled ? "已录入机主人脸，可重新录入或清空。" : "尚未录入，可点击「录入人脸」正对摄像头，或点「上传照片录入」选择一张正脸照片完成录入。");
        });
    }

    private void RefreshEnrollButton()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_enrollBtn == null) return;
            _enrollBtn.Content = _engine.IsEnrolled ? "重新录入" : "录入人脸";
        });
    }

    private void BuildCameraSection()
    {
        var body = AddCard("摄像头设备");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        _camComboBox = new ComboBox { Width = 260, Margin = new Thickness(0, 4, 0, 0) };
        var cams = CameraService.Enumerate();
        foreach (var c in cams) _camComboBox.Items.Add(new CamItem { Index = c.index, Name = c.name });
        if (_camComboBox.Items.Count == 0)
            _camComboBox.Items.Add(new CamItem { Index = 0, Name = "默认摄像头 (0)" });

        for (int i = 0; i < _camComboBox.Items.Count; i++)
            if (((CamItem)_camComboBox.Items[i]!).Index == S.CameraIndex) { _camComboBox.SelectedIndex = i; break; }
        if (_camComboBox.SelectedIndex < 0) _camComboBox.SelectedIndex = 0;
        _camComboBox.SelectionChanged += (_, _) =>
        {
            if (_camComboBox.SelectedItem is CamItem ci)
            {
                S.CameraIndex = ci.Index; S.CameraName = ci.Name; S.Save(); _engine.RestartCamera();
            }
        };
        row.Children.Add(_camComboBox);

        var testBtn = MakeButton("测试打开", (_) =>
        {
            try
            {
                using var cam = new CameraService();
                bool ok = cam.Open(S.CameraIndex);
                _camTestText!.Text = ok ? $"✓ 摄像头可用：{S.CameraIndex}" : $"✗ {cam.LastError}";
                cam.Close();
            }
            catch (Exception ex) { _camTestText!.Text = $"✗ 测试异常：{ex.Message}"; }
        });
        row.Children.Add(testBtn);
        body.Children.Add(row);

        _camTestText = new TextBlock { FontSize = 12, Foreground = Palette.TextMuted, Margin = new Thickness(0, 4, 0, 0) };
        body.Children.Add(_camTestText);
    }

    private void BuildSensitivitySection()
    {
        var body = AddCard("偷窥灵敏度（距离 / 角度）");
        _sensComboBox = new ComboBox { Width = 220 };
        _sensComboBox.Items.Add("低（较宽松）");
        _sensComboBox.Items.Add("中（推荐）");
        _sensComboBox.Items.Add("高（最严格）");
        _sensComboBox.SelectedIndex = Math.Clamp(S.Sensitivity, 0, 2);
        _sensComboBox.SelectionChanged += (_, _) =>
        {
            S.Sensitivity = _sensComboBox.SelectedIndex;
            S.Save();
        };
        body.Children.Add(_sensComboBox);
        body.Children.Add(new TextBlock
        {
            FontSize = 12,
            Foreground = Palette.TextMuted,
            Margin = new Thickness(0, 4, 0, 0),
            Text = "档位越高，判定偷窥的距离更近、偏航角更小，误报更少但可能漏报。"
        });
    }

    private void BuildActionsSection()
    {
        var body = AddCard("触发后的防护动作（可自定义）");
        body.Children.Add(MakeCheck("顶部弹窗提示（任何场景都显示）", S.EnableTopBanner, v => { S.EnableTopBanner = v; Commit(); }));
        body.Children.Add(MakeCheck("受保护应用前台时全屏置顶保护（点击 / 空格 / 回车 关闭）", S.EnableFullscreenProtect, v => { S.EnableFullscreenProtect = v; Commit(); }));
        body.Children.Add(MakeCheck("扬声器短促提醒音", S.ActionSound, v => { S.ActionSound = v; Commit(); }));
        body.Children.Add(MakeCheck("最小化受保护隐私软件", S.ActionMinimize, v => { S.ActionMinimize = v; Commit(); }));

        body.Children.Add(new TextBlock
        {
            Text = "提醒弹窗样式（大小 / 字号 / 位置）",
            FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Palette.TextSecondary,
            Margin = new Thickness(0, 10, 0, 2)
        });

        body.Children.Add(new TextBlock
        {
            Text = "提醒内容（弹窗显示的文案，留空则使用默认）",
            FontSize = 12, Foreground = Palette.TextMuted, Margin = new Thickness(0, 6, 0, 2)
        });
        var alertRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 2, 0, 2) };
        var alertBox = new TextBox
        {
            Text = S.PeekAlertText,
            Width = 480,
            MaxLength = 120,
            Watermark = PeekShieldSettings.DefaultPeekAlertText,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        alertBox.TextChanged += (_, _) => { S.PeekAlertText = alertBox.Text; S.Save(); };
        alertRow.Children.Add(alertBox);
        alertRow.Children.Add(MakeButton("恢复默认", (_) =>
        {
            S.PeekAlertText = PeekShieldSettings.DefaultPeekAlertText;
            alertBox.Text = S.PeekAlertText;
            S.Save();
        }));
        body.Children.Add(alertRow);

        var sizeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        sizeRow.Children.Add(MakeLabel("宽度"));
        var wBox = MakeNumberBox(240, 2000, S.PopupWidth, 20);
        wBox.ValueChanged += (_, _) => { S.PopupWidth = (int)wBox.Value; S.Save(); };
        sizeRow.Children.Add(wBox);
        sizeRow.Children.Add(MakeMiniButton("默认", () =>
        {
            S.PopupWidth = PeekShieldSettings.DefaultPopupWidth;
            wBox.SetValueClamped(S.PopupWidth);
            S.Save();
        }));
        sizeRow.Children.Add(MakeLabel("高度"));
        var hBox = MakeNumberBox(100, 1000, S.PopupHeight, 10);
        hBox.ValueChanged += (_, _) => { S.PopupHeight = (int)hBox.Value; S.Save(); };
        sizeRow.Children.Add(hBox);
        sizeRow.Children.Add(MakeMiniButton("默认", () =>
        {
            S.PopupHeight = PeekShieldSettings.DefaultPopupHeight;
            hBox.SetValueClamped(S.PopupHeight);
            S.Save();
        }));
        sizeRow.Children.Add(MakeLabel("字号"));
        var fBox = MakeNumberBox(12, 80, S.PopupFontSize, 1);
        fBox.ValueChanged += (_, _) => { S.PopupFontSize = (int)fBox.Value; S.Save(); };
        sizeRow.Children.Add(fBox);
        sizeRow.Children.Add(MakeMiniButton("默认", () =>
        {
            S.PopupFontSize = PeekShieldSettings.DefaultPopupFontSize;
            fBox.SetValueClamped(S.PopupFontSize);
            S.Save();
        }));
        body.Children.Add(sizeRow);

        var posRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        posRow.Children.Add(MakeLabel("位置"));
        _popPosCombo = new ComboBox { Width = 130 };
        _popPosCombo.Items.Add("屏幕居中");
        _popPosCombo.Items.Add("顶部居中");
        _popPosCombo.Items.Add("底部居中");
        _popPosCombo.Items.Add("自定义坐标");
        _popPosCombo.SelectedIndex = S.PopupPosition switch { "top" => 1, "bottom" => 2, "custom" => 3, _ => 0 };
        _popPosCombo.SelectionChanged += (_, _) =>
        {
            S.PopupPosition = _popPosCombo.SelectedIndex switch { 1 => "top", 2 => "bottom", 3 => "custom", _ => "center" };
            UpdatePopupPosBoxesEnabled();
            S.Save();
        };
        posRow.Children.Add(_popPosCombo);
        posRow.Children.Add(MakeLabel("X"));
        var xBox = MakeNumberBox(-8192, 16384, S.PopupX, 20);
        xBox.ValueChanged += (_, _) => { S.PopupX = (int)xBox.Value; S.Save(); };
        _popXBox = xBox;
        posRow.Children.Add(xBox);
        posRow.Children.Add(MakeMiniButton("默认", () =>
        {
            S.PopupX = PeekShieldSettings.DefaultPopupX;
            xBox.SetValueClamped(S.PopupX);
            S.Save();
        }));
        posRow.Children.Add(MakeLabel("Y"));
        var yBox = MakeNumberBox(-8192, 16384, S.PopupY, 20);
        yBox.ValueChanged += (_, _) => { S.PopupY = (int)yBox.Value; S.Save(); };
        _popYBox = yBox;
        posRow.Children.Add(yBox);
        posRow.Children.Add(MakeMiniButton("默认", () =>
        {
            S.PopupY = PeekShieldSettings.DefaultPopupY;
            yBox.SetValueClamped(S.PopupY);
            S.Save();
        }));
        posRow.Children.Add(MakeButton("预览效果", (_) => _engine.PreviewPopup()));
        UpdatePopupPosBoxesEnabled();
        body.Children.Add(posRow);

        body.Children.Add(new TextBlock
        {
            FontSize = 12, Foreground = Palette.TextMuted, Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Text = "自定义坐标以主屏幕左上角为原点（像素）；选「自定义坐标」后 X/Y 可编辑。点「预览效果」按当前样式显示 2 秒。"
        });
    }

    private void BuildProtectSection()
    {
        var body = AddCard("受保护程序 / 窗口（仅查看这些时才触发）");
        body.Children.Add(MakeCheck("仅当受保护程序处于前台时启用识别（其余普通软件不触发 / 失焦暂停）",
            S.OnlyProtectForeground, v => { S.OnlyProtectForeground = v; Commit(); }));

        body.Children.Add(new TextBlock
        {
            Text = "进程名（exe）— 勾选框可单独启用 / 关闭该程序的保护",
            FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Palette.TextSecondary,
            Margin = new Thickness(0, 6, 0, 2)
        });
        _procHost = new StackPanel { Spacing = 2, Margin = new Thickness(0, 2, 0, 2) };
        foreach (var p in S.ProtectedProcesses) _procList.Add(p);
        body.Children.Add(_procHost);
        RebuildProcList();

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        _procInput = new TextBox { Width = 220, Watermark = "例如 WeChat.exe" };
        var addBtn = MakeButton("添加", (_) =>
        {
            var t = (_procInput!.Text ?? string.Empty).Trim();
            if (t.Length == 0) return;
            if (!_procList.Any(x => string.Equals(x.Name, t, StringComparison.OrdinalIgnoreCase)))
            {
                _procList.Add(new ProtectedEntry { Name = t, Enabled = true });
                SyncProc(); S.Save(); RebuildProcList();
            }
            _procInput.Text = "";
        });
        row.Children.Add(_procInput);
        row.Children.Add(addBtn);
        body.Children.Add(row);
        body.Children.Add(new TextBlock
        {
            FontSize = 12, Foreground = Palette.TextMuted, Margin = new Thickness(0, 4, 0, 0),
            Text = "支持微信/QQ/浏览器/支付类网页/聊天软件等 exe 进程名（不区分大小写）。想覆盖所有窗口，可加入 explorer.exe。"
        });

        body.Children.Add(new Border
        {
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 10, 0, 4)
        });

        body.Children.Add(new TextBlock
        {
            Text = "窗口标题关键字（匹配桌面、文件夹等具体窗口）— 可单独启用 / 关闭",
            FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Palette.TextSecondary,
            Margin = new Thickness(0, 4, 0, 4)
        });

        _titleHost = new StackPanel { Spacing = 2, Margin = new Thickness(0, 2, 0, 2) };
        foreach (var t in S.ProtectedWindowTitles) _titleList.Add(t);
        body.Children.Add(_titleHost);
        RebuildTitleList();

        var tRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        _titleInput = new TextBox { Width = 220, Watermark = "例如 桌面 / 下载 / 私人" };
        var tAdd = MakeButton("添加", (_) =>
        {
            var t = (_titleInput!.Text ?? string.Empty).Trim();
            if (t.Length == 0) return;
            if (!_titleList.Any(x => string.Equals(x.Name, t, StringComparison.OrdinalIgnoreCase)))
            {
                _titleList.Add(new ProtectedEntry { Name = t, Enabled = true });
                SyncTitle(); S.Save(); RebuildTitleList();
            }
            _titleInput.Text = "";
        });
        tRow.Children.Add(_titleInput);
        tRow.Children.Add(tAdd);
        body.Children.Add(tRow);
        body.Children.Add(new TextBlock
        {
            FontSize = 12, Foreground = Palette.TextMuted, Margin = new Thickness(0, 4, 0, 0),
            Text = "前台窗口标题（如文件夹名、桌面）包含此处任意关键字即触发（不区分大小写）。默认已含“桌面”。"
        });
    }

    private Grid MakeEntryRow(ProtectedEntry entry, Action onRemove)
    {
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var cb = new CheckBox
        {
            IsChecked = entry.Enabled,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        cb.IsCheckedChanged += (_, _) => { entry.Enabled = cb.IsChecked == true; S.Save(); };
        Grid.SetColumn(cb, 0);

        var tb = new TextBlock
        {
            Text = entry.Name,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap
        };
        Grid.SetColumn(tb, 1);

        var del = MakeButton("删除", (_) => onRemove());
        Grid.SetColumn(del, 2);

        grid.Children.Add(cb);
        grid.Children.Add(tb);
        grid.Children.Add(del);
        return grid;
    }

    private void RebuildProcList()
    {
        if (_procHost == null) return;
        _procHost.Children.Clear();
        foreach (var e in _procList)
        {
            _procHost.Children.Add(MakeEntryRow(e, () =>
            {
                _procList.Remove(e); SyncProc(); S.Save(); RebuildProcList();
            }));
        }
        if (_procList.Count == 0)
            _procHost.Children.Add(new TextBlock { Text = "（暂无，添加后此处显示）", FontSize = 12, Foreground = Palette.TextFaint, Margin = new Thickness(2, 2, 0, 2) });
    }

    private void RebuildTitleList()
    {
        if (_titleHost == null) return;
        _titleHost.Children.Clear();
        foreach (var e in _titleList)
        {
            _titleHost.Children.Add(MakeEntryRow(e, () =>
            {
                _titleList.Remove(e); SyncTitle(); S.Save(); RebuildTitleList();
            }));
        }
        if (_titleList.Count == 0)
            _titleHost.Children.Add(new TextBlock { Text = "（暂无，添加后此处显示）", FontSize = 12, Foreground = Palette.TextFaint, Margin = new Thickness(2, 2, 0, 2) });
    }

    private void SyncProc() => S.ProtectedProcesses = _procList.ToList();
    private void SyncTitle() => S.ProtectedWindowTitles = _titleList.ToList();

    private void BuildSuppressSection()
    {
        var body = AddCard("误触抑制");
        body.Children.Add(MakeCheck("暗光增强（提升暗光下检出率）", S.LowLightEnhance, v => { S.LowLightEnhance = v; Commit(); }));
        body.Children.Add(MakeCheck("镜子反光 / 海报人脸过滤（降低误识别）", S.MirrorPosterFilter, v => { S.MirrorPosterFilter = v; Commit(); }));
    }

    private void BuildAdvancedSection()
    {
        var body = AddCard("高级选项");
        body.Children.Add(MakeCheck("启用快捷键一键开关智能防窥", S.EnableHotkey, v => { S.EnableHotkey = v; Commit(); }));
        var hkRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        hkRow.Children.Add(new TextBlock { Text = "修饰键", VerticalAlignment = VerticalAlignment.Center, FontSize = 13 });
        _hkModBox = new TextBox { Text = S.HotkeyModifiers, Width = 120 };
        hkRow.Children.Add(_hkModBox);
        hkRow.Children.Add(new TextBlock { Text = "主键", VerticalAlignment = VerticalAlignment.Center, FontSize = 13 });
        _hkKeyBox = new TextBox { Text = S.HotkeyKey, Width = 80 };
        hkRow.Children.Add(_hkKeyBox);
        var hkApply = MakeButton("应用快捷键", (_) =>
        {
            S.HotkeyModifiers = _hkModBox!.Text.Trim();
            S.HotkeyKey = _hkKeyBox!.Text.Trim();
            S.Save(); _engine.ApplySettings();
        });
        hkRow.Children.Add(hkApply);
        body.Children.Add(hkRow);
        body.Children.Add(new TextBlock
        {
            FontSize = 12, Foreground = Palette.TextMuted, Margin = new Thickness(0, 2, 0, 0),
            Text = "快捷键用于一键暂停/恢复防护（等同于托盘菜单的暂停/恢复）。修饰键填 Ctrl+Shift / Ctrl / Alt 等；主键填单个字母，如 P。"
        });
        body.Children.Add(MakeCheck("自动保存截屏到本地日志（偷窥截图+调试帧；关闭后不保存任何图像）", S.ScreenshotOnPeek, v => { S.ScreenshotOnPeek = v; Commit(); }));
        body.Children.Add(MakeCheck("防护结束后自动恢复被最小化的窗口", S.RestoreOnSafe, v => { S.RestoreOnSafe = v; Commit(); }));

        body.Children.Add(new TextBlock
        {
            Text = "陌生人提醒频率",
            FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Palette.TextSecondary,
            Margin = new Thickness(0, 10, 0, 2)
        });
        var cdRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        cdRow.Children.Add(MakeLabel("同一陌生人提醒上限"));
        var limitBox = MakeNumberBox(1, 20, S.StrangerAlertLimit, 1);
        limitBox.ValueChanged += (_, _) => { S.StrangerAlertLimit = (int)limitBox.Value; S.Save(); };
        cdRow.Children.Add(limitBox);
        cdRow.Children.Add(MakeLabel("次，冷却"));
        var coolBox = MakeNumberBox(1, 720, S.StrangerAlertCooldownMinutes, 5);
        coolBox.ValueChanged += (_, _) => { S.StrangerAlertCooldownMinutes = (int)coolBox.Value; S.Save(); };
        cdRow.Children.Add(coolBox);
        cdRow.Children.Add(MakeLabel("分钟"));
        body.Children.Add(cdRow);
        body.Children.Add(new TextBlock
        {
            FontSize = 12, Foreground = Palette.TextMuted, Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Text = "同一陌生人达到提醒上限后进入冷却，冷却时间内不再重复提醒，冷却结束自动重新计数。上限越大越不容易漏报，冷却越长越安静。"
        });

        body.Children.Add(MakeButton("立即清空陌生人提醒记录", (_) => _engine.ClearStrangerRecords()));
        body.Children.Add(new TextBlock
        {
            FontSize = 12, Foreground = Palette.TextMuted, Margin = new Thickness(0, 2, 0, 0),
            Text = "陌生人提醒记录仅在内存中临时保存，退出程序或重新录入机主人脸后会自动清空，不会写入磁盘。"
        });
    }

    private void BuildMasterSection()
    {
        var body = AddCard("总控");
        body.Children.Add(MakeCheck("开机自动启动", S.AutoStart, v => { S.AutoStart = v; Commit(); }));
        body.Children.Add(MakeCheck("显示托盘图标（关闭后完全后台静默）", S.ShowTrayIcon, v => { S.ShowTrayIcon = v; Commit(); }));
        _enableSmartPeekCheck = MakeCheck("智能防窥总开关", S.EnableSmartPeek, v => { S.EnableSmartPeek = v; Commit(); });
        _pausedCheck = MakeCheck("暂停全部防护", S.Paused, v => { S.Paused = v; Commit(); });
        body.Children.Add(_enableSmartPeekCheck);
        body.Children.Add(_pausedCheck);
        _manualModeCheck = MakeCheck("手动固定防窥（侧面视角变暗模糊，按 Esc 退出）", S.ManualMode, v => { S.ManualMode = v; Commit(); });
        body.Children.Add(_manualModeCheck);
    }

    private CheckBox MakeCheck(string label, bool initial, Action<bool> onChange)
    {
        var cb = new CheckBox { Content = label, IsChecked = initial, Margin = new Thickness(0, 2, 0, 2) };
        cb.IsCheckedChanged += (_, _) => { if (!_updatingUi) onChange(cb.IsChecked == true); };
        return cb;
    }

    private static TextBlock MakeLabel(string text) => new()
    {
        Text = text,
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = 13
    };

    private static NumberField MakeNumberBox(decimal min, decimal max, decimal value, decimal increment, int width = 150)
    {
        return new NumberField(min, max, value, increment, width);
    }

    private sealed class NumberField : Border
    {
        private readonly decimal _min;
        private readonly decimal _max;
        private readonly decimal _increment;
        public decimal Value { get; private set; }
        public event EventHandler? ValueChanged;

        public void SetValueClamped(decimal v)
        {
            var d = Clamp(v);
            Value = d;
            _tb.Text = d.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            _tb.CaretIndex = _tb.Text.Length;
        }

        private readonly TextBox _tb;
        private readonly NumberField _self;

        public NumberField(decimal min, decimal max, decimal value, decimal increment, int width)
        {
            _min = min; _max = max; _increment = increment;
            Value = Clamp(value);
            _self = this;

            int textWidth = Math.Max(60, width - 30);

            _tb = new TextBox
            {
                Text = Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture),
                Width = textWidth,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch,
                TextAlignment = TextAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(2, 0),
                Margin = new Thickness(0),
                Foreground = Palette.TextPrimary,
                Background = Palette.CardBg,
                BorderThickness = new Thickness(0),
                FontSize = 14,
                AcceptsReturn = false,
                MaxLength = 9,
                CaretBrush = Palette.TextPrimary,
            };

            void CommitFromText()
            {
                var raw = (_tb.Text ?? string.Empty).Trim();
                if (!decimal.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var d))
                {
                    _tb.Text = Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
                    return;
                }
                d = Clamp(d);
                Value = d;
                _tb.Text = d.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
                _tb.CaretIndex = _tb.Text.Length;
                ValueChanged?.Invoke(_self, EventArgs.Empty);
            }
            _tb.LostFocus += (_, _) => CommitFromText();
            _tb.KeyDown += (_, e) =>
            {
                if (e.Key == Avalonia.Input.Key.Enter) { CommitFromText(); e.Handled = true; }
                else if (e.Key == Avalonia.Input.Key.Up) { _self.Bump(+1); e.Handled = true; }
                else if (e.Key == Avalonia.Input.Key.Down) { _self.Bump(-1); e.Handled = true; }
            };

            var up = new RepeatButton
            {
                Content = "▲",
                FontSize = 9,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                Background = Palette.ButtonBg,
                BorderThickness = new Thickness(0),
                Foreground = Palette.TextPrimary,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Delay = 350,
                Interval = 80,
            };
            var down = new RepeatButton
            {
                Content = "▼",
                FontSize = 9,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                Background = Palette.ButtonBg,
                BorderThickness = new Thickness(0),
                Foreground = Palette.TextPrimary,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Delay = 350,
                Interval = 80,
            };
            up.Click += (_, _) => _self.Bump(+1);
            down.Click += (_, _) => _self.Bump(-1);

            var spinnerGrid = new Grid();
            spinnerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            spinnerGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(up, 0);
            Grid.SetRow(down, 1);
            spinnerGrid.Children.Add(up);
            spinnerGrid.Children.Add(down);

            var spinnerBorder = new Border
            {
                BorderBrush = Palette.Border,
                BorderThickness = new Thickness(1, 0, 0, 0),
                Width = 28,
                MinHeight = 24,
                Child = spinnerGrid,
            };

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(textWidth) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            Grid.SetColumn(_tb, 0);
            Grid.SetColumn(spinnerBorder, 1);
            row.Children.Add(_tb);
            row.Children.Add(spinnerBorder);

            BorderBrush = Palette.Border;
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(3);
            ClipToBounds = true;
            Width = width;
            MinWidth = 100;
            MinHeight = 26;
            Padding = new Thickness(0);
            Margin = new Thickness(0);
            VerticalAlignment = VerticalAlignment.Center;
            Child = row;
        }

        private decimal Clamp(decimal d)
        {
            if (d < _min) d = _min;
            if (d > _max) d = _max;
            return d;
        }

        private void Bump(int sign)
        {
            var d = Clamp(Value + _increment * sign);
            Value = d;
            _tb.Text = d.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
            _tb.CaretIndex = _tb.Text.Length;
            ValueChanged?.Invoke(_self, EventArgs.Empty);
        }
    }

    private void UpdatePopupPosBoxesEnabled()
    {
        bool custom = _popPosCombo?.SelectedIndex == 3;
        if (_popXBox != null) _popXBox.IsEnabled = custom;
        if (_popYBox != null) _popYBox.IsEnabled = custom;
    }

    private Button MakeMiniButton(string label, Action onClick)
    {
        var b = new Button
        {
            Content = label,
            FontSize = 11,
            Padding = new Thickness(7, 2),
            Background = Palette.ButtonBg,
            CornerRadius = new CornerRadius(3),
            VerticalAlignment = VerticalAlignment.Center
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    private Button MakeButton(string label, Action<object?> onClick, IBrush? bg = null)
    {
        var b = new Button
        {
            Content = label,
            FontSize = 12.5,
            Padding = new Thickness(10, 4),
            Background = bg ?? Palette.ButtonBg,
            CornerRadius = new CornerRadius(3)
        };
        b.Click += (_, _) => onClick(b);
        return b;
    }

    private void Commit() => _engine.ApplySettings();

    private void RebuildUi()
    {
        Dispatcher.UIThread.Post(() =>
        {
            Background = Palette.PageBg;
            if (_scroll != null) _scroll.Background = Palette.PageBg;
            _root.Children.Clear();
            Build();
            RefreshStatus();
        });
    }

    private void OnStatus(EngineStatus st) => Dispatcher.UIThread.Post(RefreshStatus);

    private void RefreshStatus()
    {
        var tb = _statusText;
        if (tb == null) return;
        var st = _engine.Status;
        var color = st switch
        {
            EngineStatus.Peek => "#F44336",
            EngineStatus.Secure => "#4CAF50",
            EngineStatus.Monitoring => "#2196F3",
            _ => "#FF9800"
        };
        var on = S.EnableSmartPeek ? "开" : "关";
        var paused = S.Paused ? "是" : "否";
        tb.Text = $"智能防窥：{on} ｜ 暂停：{paused} ｜ 状态：{PeekShieldEngine.StatusText(st)} ｜ 人脸数：{_engine.FaceCount} ｜ 已录入：{(_engine.IsEnrolled ? "是" : "否")}";
        tb.Foreground = Brush.Parse(color);
    }

    private void OnSettingsChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _updatingUi = true;
            try
            {
                if (_enableSmartPeekCheck != null) _enableSmartPeekCheck.IsChecked = S.EnableSmartPeek;
                if (_pausedCheck != null) _pausedCheck.IsChecked = S.Paused;
                if (_manualModeCheck != null) _manualModeCheck.IsChecked = S.ManualMode;
                RefreshStatus();
            }
            finally { _updatingUi = false; }
        });
    }
}
