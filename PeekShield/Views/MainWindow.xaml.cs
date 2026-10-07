using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PeekShield.Models;
using PeekShield.Services;
using PeekShield.Views;

namespace PeekShield;

public partial class MainWindow : Window
{
    public static MainWindow? Instance { get; set; }

    private readonly PeekShieldEngine _engine = PeekShieldEngine.Instance;
    private PeekShieldSettings S => _engine.Settings;

    private readonly Grid _mainLayout = new();
    private readonly StackPanel _navPanel = new();
    private readonly ScrollViewer _navScroll = new();
    private readonly StackPanel _contentPanel = new();
    private ScrollViewer? _contentScroll;
    private readonly List<ProtectedEntry> _procList = new();
    private readonly List<ProtectedEntry> _titleList = new();

    private string _selectedNavId = "overview";
    private readonly List<(string Id, string Label, string Icon, Action Build)> _navItems = new();
    private readonly Dictionary<string, Border> _navButtonBorders = new();

    private TextBlock? _contentTitle;

    private StackPanel? _wlHost;
    private TextBox? _wlInput;
    private TextBlock? _wlHint;
    private CheckBox? _wlEnabledCheck;
    private Button? _wlAddBtn;
    private bool _wlBusy;

    private TextBlock? _statusText;
    private TextBlock? _enrollHint;
    private TextBlock? _faceHint;
    private TextBlock? _guardHint;
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
    private Image? _enrollPreview;
    private Action<OpenCvSharp.Mat>? _enrollPreviewHandler;
    private bool _enrollPreviewBusy;
    private Image? _wlPreview;
    private Action<OpenCvSharp.Mat>? _wlPreviewHandler;
    private bool _wlPreviewBusy;
    private Dictionary<ThemeSkin, (Border Circle, TextBlock Check)> _skinMarks = new();
    private CheckBox? _enableSmartPeekCheck;
    private CheckBox? _pausedCheck;
    private CheckBox? _manualModeCheck;
    private ComboBox? _popPosCombo;
    private NumberField? _popXBox;
    private NumberField? _popYBox;
    private bool _updatingUi;
    private TextBlock? _privacyStatus;

    private Grid? _lockHost;
    private StackPanel? _lockPanel;
    private StackPanel? _securityBody;
    private bool _securityUnlocked;
    private bool _mainLocked;
    private bool _hidden;
    private bool _securityRenderedUnlocked;
    private DispatcherTimer? _sessionTimer;

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

        _contentScroll = new ScrollViewer { Content = _contentPanel, Background = Palette.PageBg };
        _lockHost = new Grid();
        _lockHost.Children.Add(_mainLayout);
        _lockPanel = CreateOpenMainLockPanel();
        _lockHost.Children.Add(_lockPanel);
        _lockPanel.IsVisible = false;
        Content = _lockHost;
        Background = Palette.PageBg;

        BuildLayout();

        Title = "窥屿盾—PeekShield";
        MinWidth = 640;
        MinHeight = 520;
        FontFamily = new FontFamily(
            OperatingSystem.IsWindows() ? "Microsoft YaHei UI" :
            OperatingSystem.IsMacOS() ? "PingFang SC" :
            "Noto Sans CJK SC");

        _engine.StatusChanged += OnStatus;
        _engine.SettingsChanged += OnSettingsChanged;

        if (NeedsOpenMainGate())
        {
            _mainLocked = true;
            ShowOpenMainLockPanel();
        }
        else
        {
            _mainLocked = false;
            BuildLayout();
        }
        RefreshStatus();

        Activated += (_, _) => OnReopen();
        Opened += (_, _) => OnReopen();
        _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _sessionTimer.Tick += (_, _) => EnforceSessionExpiry();
        _sessionTimer.Start();
    }

    public static void ShowSettings()
    {
        var w = Instance;
        if (w == null) return;
        w.Show();
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
    }

    public static void ShowSecurity()
    {
        var w = Instance;
        if (w == null) return;
        w.Show();
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
        w.RevealSecurity();
    }

    private async void RevealSecurity()
    {
        if (_mainLocked)
        {
            var r = await PasswordWindow.ShowVerify(this, "OpenMain", "解锁主页面", "输入密码以打开窥屿盾主页面。", S.PasswordHash, S.SecurityQuestion, S.SecurityAnswerHash, _engine, S, _engine.QuickVerifyAvailable);
            if (r == PasswordWindow.Outcome.Recovery)
            {
                HandlePasswordRecovery();
                return;
            }
            if (r != PasswordWindow.Outcome.Ok) return;
            UnlockMainView();
            BuildLayout();
            RefreshStatus();
        }
        if (S.PasswordEnabled && S.ProtectOpenSecurity && !IsSecurityUnlockedNow())
        {
            var r = await PasswordWindow.ShowVerify(this, "OpenSecurity", "安全设置验证", "请输入密码以管理安全设置。", S.PasswordHash, S.SecurityQuestion, S.SecurityAnswerHash, _engine, S, _engine.QuickVerifyAvailable);
            if (r == PasswordWindow.Outcome.Recovery)
            {
                HandlePasswordRecovery();
                return;
            }
            if (r == PasswordWindow.Outcome.Ok)
            {
                _securityUnlocked = true;
                RenderSecurityContent();
            }
            else
                return;
        }
        ScrollToSecurity();
    }

    private void ScrollToSecurity()
    {
        SelectNav("security");
    }

    private void BuildLayout()
    {
        try
        {
            _mainLayout.Children.Clear();
            _mainLayout.ColumnDefinitions.Clear();
            _mainLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            _mainLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            BuildNavPanel();
            Grid.SetColumn(_navScroll, 0);
            _mainLayout.Children.Add(_navScroll);

            var contentHost = new Grid { Background = Palette.PageBg };
            contentHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            contentHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            _contentTitle = new TextBlock
            {
                FontSize = 20,
                FontWeight = FontWeight.Bold,
                Foreground = Palette.TextPrimary,
                Margin = new Thickness(20, 16, 20, 6)
            };
            Grid.SetRow(_contentTitle, 0);
            contentHost.Children.Add(_contentTitle);

            _contentScroll = new ScrollViewer
            {
                Content = _contentPanel,
                Background = Palette.PageBg,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
            };
            _contentPanel.SetValue(TextBlock.ForegroundProperty, Palette.TextPrimary);
            _contentPanel.Styles.Clear();
            var textStyle = new Style(x => x.OfType<TextBlock>());
            textStyle.Add(new Setter(TextBlock.ForegroundProperty, Palette.TextPrimary));
            _contentPanel.Styles.Add(textStyle);
            Grid.SetRow(_contentScroll, 1);
            contentHost.Children.Add(_contentScroll);

            Grid.SetColumn(contentHost, 1);
            _mainLayout.Children.Add(contentHost);

            BuildNavItems();
            SelectNav(_selectedNavId);
        }
        catch (Exception ex)
        {
            try { LoggerService.LogInfo("BuildLayout 重建主界面异常：" + ex); } catch { }
            var fallback = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 12 };
            fallback.Children.Add(new TextBlock { Text = "主界面加载失败", FontSize = 18, FontWeight = FontWeight.Bold, Foreground = Palette.Danger, HorizontalAlignment = HorizontalAlignment.Center });
            fallback.Children.Add(new TextBlock { Text = ex.Message, FontSize = 12, Foreground = Palette.TextSecondary, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center });
            _mainLayout.Children.Clear();
            _mainLayout.Children.Add(fallback);
        }
    }

    private void BuildNavPanel()
    {
        _navPanel.Children.Clear();
        _navPanel.Background = Palette.PageBg;
        _navPanel.Spacing = 4;
        _navPanel.Margin = new Thickness(0);

        var header = new StackPanel { Spacing = 6, Margin = new Thickness(16, 16, 16, 12) };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        try
        {
            using var s = Avalonia.Platform.AssetLoader.Open(new Uri("avares://PeekShield/Resources/icon.png"));
            var iconImg = new Image { Source = new Avalonia.Media.Imaging.Bitmap(s), Width = 22, Height = 22 };
            titleRow.Children.Add(iconImg);
        }
        catch { }
        titleRow.Children.Add(new TextBlock { Text = "应用设置", FontSize = 15, FontWeight = FontWeight.Bold, Foreground = Palette.TextPrimary, VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(titleRow);
        header.Children.Add(new TextBlock { Text = "v" + BuildConstants.Version, FontSize = 12, Foreground = Palette.TextMuted });
        _navPanel.Children.Add(header);

        _navScroll.Content = _navPanel;
        _navScroll.Background = Palette.PageBg;
        _navScroll.BorderBrush = Palette.Border;
        _navScroll.BorderThickness = new Thickness(0, 0, 1, 0);
    }

    private void BuildNavItems()
    {
        _navItems.Clear();
        _navItems.Add(("overview", "总览", "◈", BuildOverviewPage));
        _navItems.Add(("enroll", "人脸录入", "◎", BuildEnrollPage));
#if PEEKSHIELD_WHITELIST
        _navItems.Add(("whitelist", "白名单", "☺", BuildWhitelistPage));
#endif
        _navItems.Add(("camera", "摄像头", "◉", BuildCameraPage));
        _navItems.Add(("peek", "防窥设置", "🛡", BuildPeekPage));
        _navItems.Add(("advanced", "高级", "⚙", BuildAdvancedPage));
        _navItems.Add(("security", "安全", "🔒", BuildSecurityPage));
        _navItems.Add(("tray", "托盘", "▣", BuildTrayPage));
        _navItems.Add(("backup", "备份恢复", "📦", BuildBackupPage));
        _navItems.Add(("profile", "配置文件", "👥", BuildProfilePage));
        _navItems.Add(("log", "日志", "📝", BuildLogPage));
        _navItems.Add(("diagnostic", "诊断", "🩺", BuildDiagnosticPage));
        _navItems.Add(("about", "关于", "ℹ", BuildAboutPage));

        _navButtonBorders.Clear();
        foreach (var item in _navItems)
        {
            var indicator = new Border
            {
                Width = 3,
                CornerRadius = new CornerRadius(2),
                Background = Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0, 6, 0, 6)
            };
            var iconBlock = new TextBlock { Text = item.Icon, FontSize = 13, Foreground = Palette.TextMuted, VerticalAlignment = VerticalAlignment.Center, Width = 22 };
            var labelBlock = new TextBlock { Text = item.Label, FontSize = 13, Foreground = Palette.TextPrimary, VerticalAlignment = VerticalAlignment.Center };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            row.Children.Add(iconBlock);
            row.Children.Add(labelBlock);
            var content = new Border
            {
                Background = Brushes.Transparent,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8),
                Margin = new Thickness(4, 1, 8, 1),
                Child = row,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            Grid.SetColumn(indicator, 0);
            Grid.SetColumn(content, 1);
            grid.Children.Add(indicator);
            grid.Children.Add(content);
            content.PointerPressed += (_, _) => SelectNav(item.Id);
            content.PointerEntered += (_, _) => { if (_selectedNavId != item.Id) content.Background = Palette.ButtonBg; };
            content.PointerExited += (_, _) => { if (_selectedNavId != item.Id) content.Background = Brushes.Transparent; };
            _navPanel.Children.Add(grid);
            _navButtonBorders[item.Id] = content;
            content.Tag = (indicator, iconBlock, labelBlock);
        }
    }

    private void SelectNav(string id)
    {
        if (_selectedNavId != id && _selectedNavId == "enroll")
            UnsubscribeEnrollPreview();
        if (_selectedNavId != id && _selectedNavId == "whitelist")
            UnsubscribeWhitelistPreview();
        _selectedNavId = id;
        foreach (var kv in _navButtonBorders)
        {
            var selected = kv.Key == id;
            var content = kv.Value;
            content.Background = selected ? Palette.AccentBg : Brushes.Transparent;
            if (content.Tag is ValueTuple<Border, TextBlock, TextBlock> tag)
            {
                tag.Item1.Background = selected ? Palette.Accent : Brushes.Transparent;
                tag.Item2.Foreground = selected ? Palette.AccentFg : Palette.TextMuted;
                tag.Item3.Foreground = selected ? Palette.AccentFg : Palette.TextPrimary;
            }
        }
        var item = _navItems.FirstOrDefault(x => x.Id == id);
        if (_contentTitle != null) _contentTitle.Text = item.Label;
        _contentPanel.Children.Clear();
        _contentPanel.Spacing = 4;
        _contentPanel.Margin = new Thickness(8, 4, 8, 12);
        item.Build();
    }

    private void UnsubscribeEnrollPreview()
    {
        if (_enrollPreviewHandler != null)
        {
            _engine.PreviewFrame -= _enrollPreviewHandler;
            _enrollPreviewHandler = null;
        }
        _enrollPreview = null;
        _engine.ReleasePreviewRef();
    }

    private void OnEnrollPreviewFrame(OpenCvSharp.Mat mat)
    {
        if (_enrollPreview == null || mat == null || mat.Empty()) { mat?.Dispose(); return; }
        if (_enrollPreviewBusy) { mat.Dispose(); return; }
        _enrollPreviewBusy = true;
        // 在后台线程做 PNG 编码，避免阻塞 UI 线程；loop 给的 clone 由 handler 负责 dispose。
        _ = Task.Run(() =>
        {
            try
            {
                var bytes = mat.ToBytes(".png");
                mat.Dispose();
                if (bytes == null || bytes.Length == 0) { _enrollPreviewBusy = false; return; }
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        if (_enrollPreview == null) return;
                        using var ms = new MemoryStream(bytes);
                        var old = _enrollPreview.Source as IDisposable;
                        _enrollPreview.Source = new Bitmap(ms);
                        old?.Dispose();
                    }
                    catch { }
                    finally { _enrollPreviewBusy = false; }
                });
            }
            catch
            {
                mat.Dispose();
                _enrollPreviewBusy = false;
            }
        });
    }

    private void BuildOverviewPage()
    {
        _statusText = new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(10, 10, 10, 4)
        };
        _contentPanel.Children.Add(_statusText);
        RefreshStatus();

        var global = AddCard("总控");
        global.Children.Add(MakeCheck("开机自动启动", S.AutoStart, v => { S.AutoStart = v; Commit(); }));
        global.Children.Add(MakeCheck("显示托盘图标（关闭后完全后台静默）", S.ShowTrayIcon, v => { S.ShowTrayIcon = v; Commit(); }));
        _enableSmartPeekCheck = MakeCheck("智能防窥总开关", S.EnableSmartPeek, v => { S.EnableSmartPeek = v; Commit(); });
        _pausedCheck = MakeCheck("暂停全部防护", S.Paused, v => { S.Paused = v; Commit(); });
        _manualModeCheck = MakeCheck("手动固定防窥（侧面视角变暗模糊，按 Esc 退出）", S.ManualMode, v => { S.ManualMode = v; Commit(); });
        global.Children.Add(_enableSmartPeekCheck);
        global.Children.Add(_pausedCheck);
        global.Children.Add(_manualModeCheck);

        var appearance = AddCard("外观");
        var themeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _themeComboBox = new ComboBox { Width = 200, Margin = new Thickness(0, 4, 0, 0) };
        _themeComboBox.Items.Add("跟随系统");
        _themeComboBox.Items.Add("明亮");
        _themeComboBox.Items.Add("深色");
        _themeComboBox.SelectedIndex = S.ThemeMode == ThemeMode.Light ? 1 : S.ThemeMode == ThemeMode.Dark ? 2 : 0;
        _themeComboBox.SelectionChanged += (_, _) =>
        {
            var m = _themeComboBox.SelectedIndex switch { 1 => ThemeMode.Light, 2 => ThemeMode.Dark, _ => ThemeMode.System };
            S.ThemeMode = m; S.Save(); ThemeService.SetMode(m);
        };
        themeRow.Children.Add(_themeComboBox);
        appearance.Children.Add(themeRow);
        appearance.Children.Add(new TextBlock { FontSize = 12, Foreground = Palette.TextMuted, Margin = new Thickness(0, 4, 0, 0), Text = "默认跟随系统外观，可手动固定为明亮或深色。" });

        appearance.Children.Add(new TextBlock { Text = "主题皮肤", FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Palette.TextSecondary, Margin = new Thickness(0, 12, 0, 4) });
        var skinWrap = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        _skinMarks.Clear();
        foreach (var def in SkinDef.All)
        {
            var isSel = def.Skin == S.Skin;
            var circle = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = Brush.Parse(def.Preview),
                BorderThickness = new Thickness(isSel ? 3 : 1),
                BorderBrush = isSel ? Palette.TextPrimary : Palette.Border
            };
            var check = new TextBlock
            {
                Text = "✓",
                Foreground = Brushes.White,
                FontSize = 16,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                IsVisible = isSel
            };
            var cell = new Grid { Width = 34, Height = 34, Margin = new Thickness(2) };
            cell.Children.Add(circle);
            cell.Children.Add(check);
            var btn = new Button
            {
                Content = cell,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(18),
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            ToolTip.SetTip(btn, def.Name);
            var captured = def;
            btn.Click += (_, _) =>
            {
                if (S.Skin == captured.Skin) return;
                S.Skin = captured.Skin;
                S.Save();
                ThemeService.SetSkin(captured.Skin);
                RefreshSkinSelection();
            };
            skinWrap.Children.Add(btn);
            _skinMarks[def.Skin] = (circle, check);
        }
        appearance.Children.Add(skinWrap);
        appearance.Children.Add(new TextBlock { FontSize = 12, Foreground = Palette.TextMuted, Margin = new Thickness(0, 4, 0, 0), Text = "点击色块即可切换皮肤，强调色会即时应用到本界面与弹窗。" });

        var exitCard = AddCard("退出");
        exitCard.Children.Add(new TextBlock { Text = "关闭窗口会最小化到系统托盘后台运行；如需完全退出请点下方按钮。", FontSize = 12, Foreground = Palette.TextMuted, TextWrapping = TextWrapping.Wrap });
        var exitRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        exitRow.Children.Add(MakeButton("退出程序", (_) => App.RequestExit(), Palette.Danger));
        exitCard.Children.Add(exitRow);
    }

    private void RefreshSkinSelection()
    {
        foreach (var kv in _skinMarks)
        {
            var isSel = kv.Key == S.Skin;
            kv.Value.Circle.BorderThickness = new Thickness(isSel ? 3 : 1);
            kv.Value.Circle.BorderBrush = isSel ? Palette.TextPrimary : Palette.Border;
            kv.Value.Check.IsVisible = isSel;
        }
    }

    private void BuildEnrollPage() { BuildEnrollSection(); }
    private void BuildWhitelistPage() { BuildWhitelistSection(); }
    private void BuildCameraPage() { BuildCameraSection(); }

    private void BuildPeekPage()
    {
        BuildSensitivitySection();
        BuildSmartRecognizeSection();
        BuildActionsSection();
        BuildProtectSection();
        BuildSuppressSection();
    }

    private void BuildSmartRecognizeSection()
    {
        var body = AddCard("智能识别增强");
        body.Children.Add(MakeCheck("视线 / 低头检测（仅正对屏幕者计为注视，侧身交谈、低头看手机不误报）", S.EnableGazeDetection, v => { S.EnableGazeDetection = v; Commit(); }));
        body.Children.Add(MakeCheck("多人同屏提醒（区分机主 / 白名单 / 陌生人，温和弹窗提示）", S.EnableMultiFaceAlert, v => { S.EnableMultiFaceAlert = v; Commit(); }));
        body.Children.Add(new TextBlock
        {
            FontSize = 12, Foreground = Palette.TextMuted, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap,
            Text = "视线检测基于摄像头 68 点人脸关键点估算头部偏航/俯仰角度，仅在本地计算。开启后，陌生人需正对屏幕才触发遮挡保护；多人同屏提醒会在检测到 2 人及以上（含非机主）时给出提示，不影响既有偷窥遮挡逻辑。"
        });
    }

    private void BuildAdvancedPage()
    {
        BuildAdvancedSection();
    }

    private void BuildSecurityPage()
    {
        _securityBody = _contentPanel;
        RenderSecurityContent();
    }

    private void BuildPrivacyPage() { BuildPrivacySection(); }
    private void BuildUpdatePage() { BuildUpdateSection(); }

    private void BuildTrayPage()
    {
        var body = AddCard("托盘右键菜单");
        body.Children.Add(new TextBlock { Text = "选择要在系统托盘右键菜单中显示的条目（拖拽可排序）。", FontSize = 12, Foreground = Palette.TextMuted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        var list = new StackPanel { Spacing = 4 };
        body.Children.Add(list);
        var items = S.TrayMenuItems?.Count > 0 ? S.TrayMenuItems : TrayService.DefaultMenuItems();
        var working = items.Select(x => new TrayMenuItemConfig { Id = x.Id, Visible = x.Visible }).ToList();
        var allIds = TrayService.DefaultMenuItems().Select(x => x.Id).ToList();
        foreach (var id in allIds.Where(x => !working.Any(w => w.Id == x))) working.Add(new TrayMenuItemConfig { Id = id, Visible = true });

        void Rebuild()
        {
            list.Children.Clear();
            for (int i = 0; i < working.Count; i++)
            {
                var idx = i;
                var item = working[i];
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var cb = new CheckBox { IsChecked = item.Visible, VerticalAlignment = VerticalAlignment.Center };
                cb.IsCheckedChanged += (_, _) => { item.Visible = cb.IsChecked == true; };
                Grid.SetColumn(cb, 0);

                var name = new TextBlock { Text = TrayService.MenuItemLabel(item.Id), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Foreground = Palette.TextPrimary };
                Grid.SetColumn(name, 1);

                var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
                btnRow.Children.Add(MakeMiniButton("↑", () => { if (idx > 0) { (working[idx], working[idx - 1]) = (working[idx - 1], working[idx]); Rebuild(); } }));
                btnRow.Children.Add(MakeMiniButton("↓", () => { if (idx < working.Count - 1) { (working[idx], working[idx + 1]) = (working[idx + 1], working[idx]); Rebuild(); } }));
                Grid.SetColumn(btnRow, 2);

                row.Children.Add(cb); row.Children.Add(name); row.Children.Add(btnRow);
                list.Children.Add(row);
            }
        }
        Rebuild();
        body.Children.Add(MakeButton("保存并应用", (_) => { S.TrayMenuItems = working.ToList(); S.Save(); TrayService.Instance?.RefreshMenu(); }));
    }

    private void BuildAboutPage()
    {
        var hero = new StackPanel { Spacing = 10, Margin = new Thickness(10, 10, 10, 4) };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        try
        {
            using var s = Avalonia.Platform.AssetLoader.Open(new Uri("avares://PeekShield/Resources/icon.png"));
            titleRow.Children.Add(new Image { Source = new Avalonia.Media.Imaging.Bitmap(s), Width = 48, Height = 48 });
        }
        catch { }
        var titleStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(new TextBlock { Text = BuildConstants.AppNameZh, FontSize = 24, FontWeight = FontWeight.Bold, Foreground = Palette.TextPrimary });
        titleStack.Children.Add(new TextBlock { Text = "本地离线隐私防窥工具", FontSize = 13, Foreground = Palette.TextSecondary });
        titleRow.Children.Add(titleStack);
        hero.Children.Add(titleRow);
        hero.Children.Add(new TextBlock { Text = "一款通过本机摄像头实时检测人脸、智能识别偷窥行为并自动触发雾化遮罩与告警的桌面隐私保护工具。", FontSize = 12, Foreground = Palette.TextMuted, TextWrapping = TextWrapping.Wrap });
        _contentPanel.Children.Add(hero);

        var card = new Border
        {
            Background = Palette.CardBg,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(18),
            Margin = new Thickness(6, 4, 6, 4)
        };
        var stack = new StackPanel { Spacing = 12 };
        var infoRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        try
        {
            using var s2 = Avalonia.Platform.AssetLoader.Open(new Uri("avares://PeekShield/Resources/icon.png"));
            infoRow.Children.Add(new Border { Width = 44, Height = 44, CornerRadius = new CornerRadius(10), Background = Palette.AccentBg, Child = new Image { Source = new Avalonia.Media.Imaging.Bitmap(s2), Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
        }
        catch { }
        var infoStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        infoStack.Children.Add(new TextBlock { Text = BuildConstants.AppName, FontSize = 16, FontWeight = FontWeight.Bold, Foreground = Palette.TextPrimary });
        infoStack.Children.Add(new TextBlock { Text = "v" + BuildConstants.Version, FontSize = 13, Foreground = Palette.TextSecondary });
        infoRow.Children.Add(infoStack);
        var expand = new TextBlock { Text = "⌃", FontSize = 14, Foreground = Palette.TextMuted, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        infoRow.Children.Add(expand);
        stack.Children.Add(infoRow);

        stack.Children.Add(new TextBlock { Text = "Copyright © yty16\n本项目基于 GNU General Public License v3.0 获得许可。", FontSize = 12, Foreground = Palette.TextMuted, TextWrapping = TextWrapping.Wrap });
        var linkRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        linkRow.Children.Add(MakeLink("项目主页", BuildConstants.GitHubRepoUrl));
        linkRow.Children.Add(MakeLink("帮助文档", BuildConstants.GitHubRepoUrl + "/blob/main/README.md"));
        linkRow.Children.Add(MakeLink("GitHub", BuildConstants.GitHubRepoUrl));
        linkRow.Children.Add(MakeLink("问题反馈", BuildConstants.GitHubIssuesUrl));
        stack.Children.Add(linkRow);
        card.Child = stack;
        _contentPanel.Children.Add(card);

        var helpCard = AddCard("遇到问题？");
        helpCard.Children.Add(new TextBlock { Text = "若在使用过程中遇到异常、崩溃或功能疑问，可通过 GitHub Issues 提交反馈，或查看 README.md 与 PRIVACY.md 获取更多信息。", FontSize = 12, Foreground = Palette.TextMuted, TextWrapping = TextWrapping.Wrap });
        helpCard.Children.Add(MakeButton("打开 GitHub Issues", (_) => OpenUrl(BuildConstants.GitHubIssuesUrl)));

        BuildUpdateSection();
        BuildPrivacySection();
    }

    private void BuildBackupPage()
    {
        var desc = new TextBlock
        {
            Text = "将当前应用配置导出为 .kyd 备份文件，可自由勾选要包含的内容；也可从 .kyd 文件导入配置（若本软件已设置安全密码，导入前需先验证当前密码）。",
            FontSize = 12,
            Foreground = Palette.TextMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(6, 4, 6, 6)
        };
        _contentPanel.Children.Add(desc);

        var exportCard = AddCard("导出配置（.kyd）");
        exportCard.Children.Add(new TextBlock { Text = "选择要导出的内容：", FontSize = 12, Foreground = Palette.TextSecondary, Margin = new Thickness(0, 0, 0, 2) });

        var cbSettings = MakeCheck("应用设置（所有配置项，含安全密码与认证方式）", true, _ => { });
        var cbOwner = MakeCheck("机主人脸", false, _ => { });
        var cbWhitelist = MakeCheck("白名单人脸", false, _ => { });
        var cbAuth = MakeCheck("认证方式人脸（除机主外的独立人脸）", false, _ => { });
        exportCard.Children.Add(cbSettings);
        exportCard.Children.Add(cbOwner);
        exportCard.Children.Add(cbWhitelist);
        exportCard.Children.Add(cbAuth);

        exportCard.Children.Add(new TextBlock { Text = "导出文件密码（留空则不加密；设置后导入时需输入该密码）", FontSize = 12, Foreground = Palette.TextSecondary, Margin = new Thickness(0, 8, 0, 2) });
        var pwdBox = new TextBox { PasswordChar = '●', Width = 260, Watermark = "可选，用于加密 .kyd 文件", Foreground = Palette.TextPrimary, Background = Palette.CardBg };
        var pwdConfirm = new TextBox { PasswordChar = '●', Width = 260, Watermark = "再次输入密码", Foreground = Palette.TextPrimary, Background = Palette.CardBg };
        exportCard.Children.Add(pwdBox);
        exportCard.Children.Add(pwdConfirm);
        var exportHint = new TextBlock { Text = "", FontSize = 12, Foreground = Palette.TextMuted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        exportCard.Children.Add(exportHint);
        exportCard.Children.Add(MakeButton("导出为 .kyd 文件", async (_) =>
        {
            bool incSettings = cbSettings.IsChecked == true;
            bool incOwner = cbOwner.IsChecked == true;
            bool incWl = cbWhitelist.IsChecked == true;
            bool incAuth = cbAuth.IsChecked == true;
            if (!incSettings && !incOwner && !incWl && !incAuth)
            {
                exportHint.Text = "请至少选择一项要导出的内容。";
                return;
            }
            var pw = pwdBox.Text ?? "";
            var pw2 = pwdConfirm.Text ?? "";
            if (pw.Length > 0 && pw != pw2)
            {
                exportHint.Text = "两次输入的密码不一致。";
                return;
            }
            var req = new BackupExportRequest
            {
                IncludeSettings = incSettings,
                IncludeOwnerFace = incOwner,
                IncludeWhitelistFaces = incWl,
                IncludeAuthFaces = incAuth,
                FilePassword = pw.Length > 0 ? pw : null
            };
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出配置为 .kyd 文件",
                DefaultExtension = "kyd",
                SuggestedFileName = "PeekShield-Backup-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".kyd",
                FileTypeChoices = new[] { new FilePickerFileType("PeekShield 备份 (*.kyd)") { Patterns = new[] { "*.kyd" } } }
            });
            if (file == null) return;
            var res = ConfigBackupService.Export(file.Path.LocalPath, req);
            if (!res.Success)
            {
                exportHint.Foreground = Palette.Danger;
                exportHint.Text = "导出失败：" + res.Error;
                return;
            }
            exportHint.Foreground = Palette.TextSecondary;
            exportHint.Text = "✓ 已导出到 " + file.Path.LocalPath + (res.Encrypted ? "（已加密）" : "（未加密）");
        }));

        var importCard = AddCard("导入配置（.kyd）");
        importCard.Children.Add(new TextBlock
        {
            Text = "从 .kyd 文件恢复配置。仅导入文件中包含的内容；文件中未包含的项目（如未勾选的人脸）将保持目标软件现有状态不变。若目标软件已设置安全密码，导入前需先验证当前密码。导入完成后需重启应用生效。",
            FontSize = 12,
            Foreground = Palette.TextMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        });
        importCard.Children.Add(MakeButton("选择 .kyd 文件并导入", async (_) => await DoImportConfig()));

        var autoCard = AddCard("自动定时备份");
        autoCard.Children.Add(new TextBlock
        {
            Text = "按设定间隔自动将配置与机密数据备份为 .kyd 文件到指定目录，无需手动操作；也可随时点击「立即备份」。",
            FontSize = 12, Foreground = Palette.TextMuted, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        });
        autoCard.Children.Add(MakeCheck("启用自动定时备份", S.AutoBackupEnabled, v => { S.AutoBackupEnabled = v; S.Save(); _engine.ApplySettings(); }));
        var intRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        intRow.Children.Add(MakeLabel("备份间隔"));
        var intBox = MakeNumberBox(1, 8760, S.AutoBackupIntervalHours, 1, 120);
        intBox.ValueChanged += (_, _) => { S.AutoBackupIntervalHours = (int)intBox.Value; S.Save(); _engine.ApplySettings(); };
        intRow.Children.Add(intBox);
        intRow.Children.Add(MakeLabel("小时"));
        autoCard.Children.Add(intRow);

        var bakDirRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        var bakDirBox = new TextBox { Text = S.AutoBackupDir, Width = 320, Watermark = "留空则备份到应用数据目录", Foreground = Palette.TextPrimary, Background = Palette.CardBg };
        bakDirBox.TextChanged += (_, _) => { S.AutoBackupDir = bakDirBox.Text.Trim(); S.Save(); };
        bakDirRow.Children.Add(bakDirBox);
        bakDirRow.Children.Add(MakeButton("选择目录", async (_) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false, Title = "选择自动备份目录" });
            if (folders != null && folders.Count > 0)
            {
                bakDirBox.Text = folders[0].Path.LocalPath;
                S.AutoBackupDir = bakDirBox.Text.Trim();
                S.Save();
                _engine.ApplySettings();
            }
        }));
        autoCard.Children.Add(bakDirRow);

        autoCard.Children.Add(new TextBlock { Text = "自动备份文件密码（可选，留空则不加密；加密后导入需输入该密码）", FontSize = 12, Foreground = Palette.TextSecondary, Margin = new Thickness(0, 8, 0, 2) });
        var bakPwd = new TextBox { PasswordChar = '●', Width = 320, Watermark = "可选，用于加密自动备份文件", Foreground = Palette.TextPrimary, Background = Palette.CardBg };
        bakPwd.TextChanged += (_, _) => { S.AutoBackupPassword = bakPwd.Text ?? ""; S.Save(); };
        autoCard.Children.Add(bakPwd);

        var autoHint = new TextBlock { Text = "", FontSize = 12, Foreground = Palette.TextMuted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        autoCard.Children.Add(autoHint);
        autoCard.Children.Add(MakeButton("立即备份", (_) =>
        {
            _engine.RunAutoBackupNow();
            autoHint.Foreground = Palette.TextSecondary;
            autoHint.Text = "✓ 已执行一次备份（详见日志目录 engine.log）";
        }));
    }

    private void BuildProfilePage()
    {
        var desc = new TextBlock
        {
            Text = "配置文件让同一台电脑上保存多套完全独立的设置、人脸与认证数据（例如「办公」与「家庭」）。切换配置文件后需重启应用，各自的数据互不干扰。",
            FontSize = 12, Foreground = Palette.TextMuted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 4, 6, 6)
        };
        _contentPanel.Children.Add(desc);

        var cur = Platform.ProfileName;
        var curCard = AddCard("当前配置文件");
        curCard.Children.Add(new TextBlock
        {
            Text = string.IsNullOrEmpty(cur) ? "默认（default）" : cur,
            FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Palette.TextPrimary,
            Margin = new Thickness(0, 0, 0, 4)
        });

        var profiles = Platform.ListProfiles();
        var listCard = AddCard("已有配置文件");
        if (profiles.Count == 0)
            listCard.Children.Add(new TextBlock { Text = "（暂无其他配置文件，当前使用默认配置）", FontSize = 12, Foreground = Palette.TextMuted });
        foreach (var p in profiles)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 4, 0, 4) };
            row.Children.Add(new TextBlock { Text = p, VerticalAlignment = VerticalAlignment.Center, FontSize = 13, Foreground = Palette.TextPrimary, Width = 180 });
            row.Children.Add(MakeButton("切换", (_) => _ = SwitchProfile(p)));
            listCard.Children.Add(row);
        }

        var newCard = AddCard("新建配置文件");
        var nameBox = new TextBox { Width = 240, Watermark = "配置文件名称，如 办公 / 家庭", Foreground = Palette.TextPrimary, Background = Palette.CardBg };
        newCard.Children.Add(nameBox);
        newCard.Children.Add(MakeButton("创建并切换", async (_) =>
        {
            var nm = (nameBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(nm)) return;
            var safe = Platform.SanitizeProfileName(nm);
            if (Platform.ListProfiles().Any(x => string.Equals(x, safe, StringComparison.OrdinalIgnoreCase)))
            {
                new InfoDialog("已存在", "该名称的配置文件已存在。").ShowDialog(this);
                return;
            }
            await SwitchProfile(nm);
        }));

        if (!string.IsNullOrEmpty(cur))
        {
            var delCard = AddCard("删除当前配置文件");
            delCard.Children.Add(new TextBlock
            {
                Text = "删除将永久清除该配置下的设置、人脸与认证数据，且不可恢复。当前正在使用的默认配置无法删除。",
                FontSize = 12, Foreground = Palette.TextMuted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4)
            });
            delCard.Children.Add(MakeButton("删除当前配置文件", async (_) =>
            {
                var ok = await ShowConfirm("删除配置文件", "确定删除当前配置文件「" + cur + "」及其全部数据？此操作不可恢复。");
                if (!ok) return;
                try
                {
                    var dir = Path.Combine(Platform.ProfilesRoot, "profiles", Platform.SanitizeProfileName(cur));
                    if (Directory.Exists(dir)) Directory.Delete(dir, true);
                }
                catch (Exception ex)
                {
                    new InfoDialog("删除失败", ex.Message).ShowDialog(this);
                    return;
                }
                App.SwitchProfileAndRestart("");
            }, Palette.Danger));
        }
    }

    private async Task SwitchProfile(string name)
    {
        var ok = await ShowConfirm("切换配置文件", "切换配置文件需要重启应用，是否立即重启并切换到「" + (string.IsNullOrEmpty(name) ? "默认" : name) + "」？");
        if (!ok) return;
        App.SwitchProfileAndRestart(name);
    }

    private void BuildLogPage()
    {
        var desc = new TextBlock
        {
            Text = "查看本机运行日志与陌生人取证文件。日志与取证图片仅保存在本机，不会上传任何服务器。",
            FontSize = 12, Foreground = Palette.TextMuted, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(6, 4, 6, 6)
        };
        _contentPanel.Children.Add(desc);

        var infoCard = AddCard("概览");
        var infoText = new TextBlock { FontSize = 12, Foreground = Palette.TextSecondary, TextWrapping = TextWrapping.Wrap };
        infoCard.Children.Add(infoText);

        var logCard = AddCard("运行日志");
        var logBox = new TextBox
        {
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas, Menlo, Courier New, monospace"),
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            Height = 380,
            Foreground = Palette.TextPrimary,
            Background = Palette.CardBg
        };
        logCard.Children.Add(logBox);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 8, 0, 0) };
        btnRow.Children.Add(MakeButton("刷新", (_) => LoadLogs(logBox, infoText)));
        btnRow.Children.Add(MakeButton("打开日志目录", (_) => Platform.OpenFolder(Platform.LogsDir)));
        btnRow.Children.Add(MakeButton("清空日志与截图", async (_) =>
        {
            var ok = await ShowConfirm("清空日志", "确定清空全部运行日志、偷窥截图与陌生人取证图片？此操作不可恢复。");
            if (!ok) return;
            try { LoggerService.DeleteLogsAndSnapshots(); } catch { }
            LoadLogs(logBox, infoText);
        }, Palette.Danger));
        logCard.Children.Add(btnRow);

        LoadLogs(logBox, infoText);
    }

    private void BuildDiagnosticPage()
    {
        var desc = new TextBlock
        {
            Text = "对运行环境、模型文件、摄像头、人脸数据、开机自启与设置有效性进行本地自检。所有检查均在本地完成，不上传任何数据。",
            FontSize = 12, Foreground = Palette.TextMuted, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(6, 4, 6, 6)
        };
        _contentPanel.Children.Add(desc);

        var last = string.IsNullOrEmpty(S.LastDiagnosticAt) ? "尚未运行自检" : $"上次自检：{S.LastDiagnosticAt}";
        var lastText = new TextBlock
        {
            Text = last,
            FontSize = 12, Foreground = Palette.TextSecondary, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(6, 0, 6, 8)
        };
        _contentPanel.Children.Add(lastText);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(MakeButton("运行诊断工具", (_) =>
        {
            var w = new DiagnosticWindow(S, _engine);
            w.Closed += (_, _) => { lastText.Text = string.IsNullOrEmpty(S.LastDiagnosticAt) ? "尚未运行自检" : $"上次自检：{S.LastDiagnosticAt}"; };
            w.ShowDialog(this);
        }, Palette.AccentBg));
        _contentPanel.Children.Add(row);

        _contentPanel.Children.Add(new TextBlock
        {
            Text = "提示：若解锁时遇到「被锁死」或人脸无法识别，可先在此处排查认证方式与模型文件是否完整。",
            FontSize = 12, Foreground = Palette.TextMuted, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(6, 10, 6, 0)
        });
    }

    private void LoadLogs(TextBox box, TextBlock info)
    {
        try
        {
            var dir = Platform.LogsDir;
            var sb = new System.Text.StringBuilder();
            int evidence = 0;
            try
            {
                var ev = Path.Combine(dir, "evidence");
                if (Directory.Exists(ev)) evidence = Directory.GetFiles(ev, "*.png").Length;
            }
            catch { }
            var engineLog = Path.Combine(dir, "engine.log");
            var peekLog = Path.Combine(dir, "peek.log");
            sb.AppendLine("=== engine.log ===");
            sb.AppendLine(File.Exists(engineLog) ? File.ReadAllText(engineLog) : "（无）");
            sb.AppendLine();
            sb.AppendLine("=== peek.log ===");
            sb.AppendLine(File.Exists(peekLog) ? File.ReadAllText(peekLog) : "（无）");
            box.Text = sb.ToString();
            info.Text = $"日志目录：{dir}\n陌生人取证图片：{evidence} 张";
        }
        catch (Exception ex)
        {
            box.Text = "读取日志失败：" + ex.Message;
        }
    }

    private async Task DoImportConfig()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 .kyd 配置文件",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("PeekShield 备份 (*.kyd)") { Patterns = new[] { "*.kyd" } } }
        });
        if (files == null || files.Count == 0) return;
        var path = files[0].Path.LocalPath;

        string? filePassword = null;
        if (ConfigBackupService.FileRequiresPassword(path))
        {
            filePassword = await PromptText("解密备份文件", "该配置文件已加密，请输入导出时设置的密码：", true);
            if (filePassword == null) return;
        }

        var (backup, err) = ConfigBackupService.Parse(path, filePassword);
        if (backup == null)
        {
            new InfoDialog("导入失败", err).ShowDialog(this);
            return;
        }

        if (S.PasswordEnabled)
        {
            var outcome = await PasswordWindow.ShowVerify(this, "OpenSecurity", "验证当前安全密码", "导入配置会覆盖当前安全密码与全部设置，需先验证当前安全密码。", S.PasswordHash, S.SecurityQuestion, S.SecurityAnswerHash, _engine, S, _engine.QuickVerifyAvailable);
            if (outcome != PasswordWindow.Outcome.Ok && outcome != PasswordWindow.Outcome.Recovery)
            {
                new InfoDialog("已取消", "未通过当前安全密码验证，导入已取消。").ShowDialog(this);
                return;
            }
        }

        var res = ConfigBackupService.Apply(backup);
        if (!res.Success)
        {
            new InfoDialog("导入失败", res.Error).ShowDialog(this);
            return;
        }

        var restart = await ShowConfirm("导入成功", "配置已成功导入。需要重启应用才能生效，是否立即重启？");
        if (restart) App.RestartForImport();
    }

    private async Task<string?> PromptText(string title, string prompt, bool password)
    {
        var tcs = new TaskCompletionSource<string?>();
        var win = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            CanResize = false,
            Background = Palette.PageBg
        };
        var box = new TextBox { Width = 400, Watermark = "请输入", Foreground = Palette.TextPrimary, Background = Palette.CardBg };
        if (password) box.PasswordChar = '●';
        var hint = new TextBlock { Text = prompt, FontSize = 12, Foreground = Palette.TextSecondary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        var ok = new Button { Content = "确定", MinWidth = 90, Padding = new Thickness(14, 6), Background = new SolidColorBrush(Color.Parse("#2563EB")), Foreground = Brushes.White, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(4) };
        var cancel = new Button { Content = "取消", MinWidth = 90, Padding = new Thickness(14, 6), Background = Palette.ButtonBg, Foreground = Palette.TextPrimary, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(4) };
        ok.Click += (_, _) => { tcs.TrySetResult(box.Text ?? ""); win.Close(); };
        cancel.Click += (_, _) => { tcs.TrySetResult(null); win.Close(); };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        row.Children.Add(cancel);
        row.Children.Add(ok);
        var root = new StackPanel { Margin = new Thickness(22, 20, 22, 18), Spacing = 0 };
        root.Children.Add(hint);
        root.Children.Add(box);
        root.Children.Add(row);
        win.Content = root;
        _ = win.ShowDialog(this);
        return await tcs.Task;
    }

    private async Task<bool> ShowConfirm(string title, string message)
    {
        var tcs = new TaskCompletionSource<bool>();
        var win = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            CanResize = false,
            Background = Palette.PageBg
        };
        var msg = new TextBlock { Text = message, FontSize = 13, Foreground = Palette.TextSecondary, TextWrapping = TextWrapping.Wrap, LineHeight = 20, Margin = new Thickness(0, 0, 0, 14) };
        var yes = new Button { Content = "立即重启", MinWidth = 110, Padding = new Thickness(14, 6), Background = new SolidColorBrush(Color.Parse("#2563EB")), Foreground = Brushes.White, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(4) };
        var later = new Button { Content = "稍后重启", MinWidth = 110, Padding = new Thickness(14, 6), Background = Palette.ButtonBg, Foreground = Palette.TextPrimary, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(4) };
        yes.Click += (_, _) => { tcs.TrySetResult(true); win.Close(); };
        later.Click += (_, _) => { tcs.TrySetResult(false); win.Close(); };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Right };
        row.Children.Add(later);
        row.Children.Add(yes);
        var root = new StackPanel { Margin = new Thickness(22, 20, 22, 18), Spacing = 0 };
        root.Children.Add(msg);
        root.Children.Add(row);
        win.Content = root;
        _ = win.ShowDialog(this);
        return await tcs.Task;
    }

    private TextBlock MakeLink(string text, string url)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeService.IsDark ? Color.Parse("#60A5FA") : Color.Parse("#2563EB")),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        tb.PointerPressed += (_, _) => OpenUrl(url);
        return tb;
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
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = new Thickness(6, 4, 6, 4),
            Child = inner
        };
        _contentPanel.Children.Add(card);
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
        UnsubscribeEnrollPreview();
        _engine.AddPreviewRef();

        var body = AddCard("人脸录入（本地存储，禁止上传）");

        _enrollPreview = new Image { Stretch = Stretch.Uniform };
        var previewBorder = new Border
        {
            Width = 320,
            Height = 240,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Palette.CardBg,
            CornerRadius = new CornerRadius(8),
            Child = _enrollPreview
        };
        body.Children.Add(previewBorder);

        _enrollPreviewHandler = OnEnrollPreviewFrame;
        _engine.PreviewFrame += _enrollPreviewHandler;

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
        body.Children.Add(row);
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
            VerticalContentAlignment = VerticalAlignment.Center,
            Foreground = Palette.TextPrimary,
            Background = Palette.CardBg
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
        _procList.Clear();
        foreach (var p in S.ProtectedProcesses)
            if (!_procList.Any(x => string.Equals(x.Name, p.Name, StringComparison.OrdinalIgnoreCase)))
                _procList.Add(p);
        body.Children.Add(_procHost);
        RebuildProcList();

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        _procInput = new TextBox { Width = 220, Watermark = "例如 WeChat.exe", Foreground = Palette.TextPrimary, Background = Palette.CardBg };
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
        _titleList.Clear();
        foreach (var t in S.ProtectedWindowTitles)
            if (!_titleList.Any(x => string.Equals(x.Name, t.Name, StringComparison.OrdinalIgnoreCase)))
                _titleList.Add(t);
        body.Children.Add(_titleHost);
        RebuildTitleList();

        var tRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        _titleInput = new TextBox { Width = 220, Watermark = "例如 桌面 / 下载 / 私人", Foreground = Palette.TextPrimary, Background = Palette.CardBg };
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
        _hkModBox = new TextBox { Text = S.HotkeyModifiers, Width = 120, Foreground = Palette.TextPrimary, Background = Palette.CardBg };
        hkRow.Children.Add(_hkModBox);
        hkRow.Children.Add(new TextBlock { Text = "主键", VerticalAlignment = VerticalAlignment.Center, FontSize = 13 });
        _hkKeyBox = new TextBox { Text = S.HotkeyKey, Width = 80, Foreground = Palette.TextPrimary, Background = Palette.CardBg };
        hkRow.Children.Add(_hkKeyBox);
        var hkApply = MakeButton("应用快捷键", (_) =>
        {
            S.HotkeyModifiers = _hkModBox!.Text.Trim();
            S.HotkeyKey = _hkKeyBox!.Text.Trim();
            S.Save(); _engine.ApplySettings();
        });
        hkRow.Children.Add(hkApply);
        body.Children.Add(hkRow);

        var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        actionRow.Children.Add(new TextBlock { Text = "快捷键功能", VerticalAlignment = VerticalAlignment.Center, FontSize = 13 });
        var actionCombo = new ComboBox { Width = 240 };
        actionCombo.Items.Add("暂停 / 恢复防护");
        actionCombo.Items.Add("切换手动防窥");
        actionCombo.SelectedIndex = S.HotkeyAction == 1 ? 1 : 0;
        actionCombo.SelectionChanged += (_, _) =>
        {
            if (actionCombo.SelectedIndex is 0 or 1)
            {
                S.HotkeyAction = actionCombo.SelectedIndex;
                S.Save();
                _engine.ApplySettings();
            }
        };
        actionRow.Children.Add(actionCombo);
        body.Children.Add(actionRow);

        body.Children.Add(new TextBlock
        {
            FontSize = 12, Foreground = Palette.TextMuted, Margin = new Thickness(0, 2, 0, 0),
            Text = "快捷键用于快速切换防护状态（等同于托盘菜单的对应项）。修饰键填 Ctrl+Shift / Ctrl / Alt 等；主键填单个字母，如 P。"
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

        var capCard = AddCard("静默取证（仅陌生人）");
        capCard.Children.Add(MakeCheck("检测到偷窥时静默保存陌生人面部截图（存于本机，不上传）", S.SilentCaptureStrangers, v => { S.SilentCaptureStrangers = v; Commit(); }));
        capCard.Children.Add(new TextBlock
        {
            FontSize = 12, Foreground = Palette.TextMuted, Margin = new Thickness(0, 2, 0, 4),
            TextWrapping = TextWrapping.Wrap,
            Text = "仅在判定为「陌生人注视屏幕」时裁剪并保存其面部图像到下方目录，机主与白名单不会被保存。所有取证文件仅存于本机，不会上传任何服务器。"
        });
        var capDirRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        var capDirBox = new TextBox { Text = S.SilentCaptureDir, Width = 320, Watermark = "留空则保存在 日志目录/evidence", Foreground = Palette.TextPrimary, Background = Palette.CardBg };
        capDirBox.TextChanged += (_, _) => { S.SilentCaptureDir = capDirBox.Text.Trim(); S.Save(); };
        capDirRow.Children.Add(capDirBox);
        capDirRow.Children.Add(MakeButton("选择目录", async (_) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false, Title = "选择陌生人取证保存目录" });
            if (folders != null && folders.Count > 0)
            {
                capDirBox.Text = folders[0].Path.LocalPath;
                S.SilentCaptureDir = capDirBox.Text.Trim();
                S.Save();
            }
        }));
        capDirRow.Children.Add(MakeButton("打开取证目录", (_) => Platform.OpenFolder(_engine.ResolveSilentCaptureDir())));
        capCard.Children.Add(capDirRow);

        body.Children.Add(new Border { Height = 1, Background = Palette.Border, Margin = new Thickness(0, 12, 0, 8) });
        body.Children.Add(new TextBlock
        {
            Text = "崩溃后处理方式",
            FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Palette.TextPrimary,
            Margin = new Thickness(0, 6, 0, 4)
        });
        body.Children.Add(new TextBlock
        {
            Text = "仅处理软件自身原因造成的崩溃：提示崩溃（弹出提示，可选重启应用或关闭应用）/ 静默重启应用 / 自动退出应用。强制结束进程仍由进程保护看门狗处理，两者互不影响。",
            FontSize = 12, Foreground = Palette.TextMuted, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var crashCombo = new ComboBox { Width = 360, Margin = new Thickness(0, 4, 0, 0) };
        crashCombo.Items.Add("提示崩溃（重启应用 / 关闭应用）");
        crashCombo.Items.Add("静默重启应用");
        crashCombo.Items.Add("自动退出应用");
        var map = new[] { CrashBehavior.PromptRestart, CrashBehavior.SilentRestart, CrashBehavior.ExitApp };
        int sel = Array.IndexOf(map, S.CrashBehaviorOnCrash);
        crashCombo.SelectedIndex = sel < 0 ? 0 : sel;
        crashCombo.SelectionChanged += (_, _) =>
        {
            if (crashCombo.SelectedIndex is < 0 or > 2) return;
            S.CrashBehaviorOnCrash = map[crashCombo.SelectedIndex];
            S.Save();
        };
        body.Children.Add(crashCombo);

    }

    private void BuildGlobalSection()
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
        var cb = new CheckBox { Content = label, IsChecked = initial, Margin = new Thickness(0, 2, 0, 2), Foreground = Palette.TextPrimary };
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
        tb.Text = $"智能防窥：{on} ｜ 暂停：{paused} ｜ 状态：{PeekShieldEngine.StatusText(st)} ｜ {_engine.FaceDetail} ｜ 已录入：{(_engine.IsEnrolled ? "是" : "否")}";
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
                RefreshPrivacyStatus();
            }
            finally { _updatingUi = false; }
        });
    }

    private void BuildUpdateSection()
    {
        var body = AddCard("软件更新");

        body.Children.Add(new TextBlock
        {
            Text = "当前版本 v" + UpdateService.CurrentVersion,
            FontSize = 12,
            Foreground = Palette.TextMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        });

        Button? checkBtn = null;
        checkBtn = MakeButton("检查更新", async (_) =>
        {
            if (checkBtn != null) checkBtn.IsEnabled = false;
            try
            {
                var info = await UpdateService.CheckAsync();
                UpdateService.ShowUpdateDialog(this, info);
            }
            finally { if (checkBtn != null) checkBtn.IsEnabled = true; }
        });
        body.Children.Add(checkBtn);

        var srcRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 2) };
        srcRow.Children.Add(MakeButton("GitHub Releases 下载", (_) => Platform.OpenUrl(BuildConstants.GitHubReleasesUrl)));
        srcRow.Children.Add(MakeButton("123 网盘下载", (_) => Platform.OpenUrl(BuildConstants.Pan123Url)));
        body.Children.Add(srcRow);

        body.Children.Add(new TextBlock
        {
            Text = "发布源：GitHub Releases（含版本校验与在应用内更新）、123 网盘（提取码 " + BuildConstants.Pan123ExtractCode + "）。",
            FontSize = 11.5,
            Foreground = Palette.TextMuted,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 4)
        });

        body.Children.Add(MakeCheck("启动时自动检查更新", S.AutoCheckUpdate, v => { S.AutoCheckUpdate = v; S.Save(); }));
        body.Children.Add(MakeCheck("有更新时自动静默更新（覆盖安装）", S.AutoSilentUpdate, v => { S.AutoSilentUpdate = v; S.Save(); }));
    }

    private void BuildPrivacySection()
    {
        var body = AddCard("隐私与授权");
        _privacyStatus = new TextBlock
        {
            FontSize = 12,
            Foreground = Palette.TextSecondary,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        };
        body.Children.Add(_privacyStatus);

        var row1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 2, 0, 4) };
        row1.Children.Add(MakeButton("查看隐私政策 / 管理授权", (_) => _ = OpenPrivacyDialog()));
        row1.Children.Add(MakeButton("撤回全部同意", (_) => RevokeConsent()));
        body.Children.Add(row1);

        var row2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 2, 0, 4) };
        row2.Children.Add(MakeButton("打开数据目录", (_) => OpenDataDir()));
        body.Children.Add(row2);

        var linkFg = new SolidColorBrush(ThemeService.IsDark ? Color.Parse("#60A5FA") : Color.Parse("#2563EB"));
        var repoLink = new TextBlock
        {
            Text = "项目仓库（含完整《隐私政策》PRIVACY.md）：" + BuildConstants.GitHubRepoUrl,
            FontSize = 12,
            Foreground = linkFg,
            TextWrapping = TextWrapping.Wrap,
            Cursor = new Cursor(StandardCursorType.Hand),
            Margin = new Thickness(0, 6, 0, 0)
        };
        repoLink.PointerPressed += (_, _) => OpenUrl(BuildConstants.GitHubRepoUrl);
        body.Children.Add(repoLink);

        RefreshPrivacyStatus();
    }

    private async Task OpenPrivacyDialog()
    {
        Show();
        await ConsentService.RunAsync(this, S, false);
        RefreshPrivacyStatus();
        RefreshStatus();
    }

    private void RevokeConsent()
    {
        var dlg = new ConfirmDialog("撤回全部同意",
            "撤回后软件将立即停止摄像头侦测（人脸相关功能不可用），其他功能（如手动防窥）仍可使用；下次启动会重新征求你的同意。\n\n确定要撤回吗？",
            "撤回同意", "取消");
        dlg.Closed += (_, _) =>
        {
            if (!dlg.Confirmed) return;
            ConsentService.Revoke(S);
            RefreshPrivacyStatus();
            RefreshStatus();
        };
        dlg.ShowDialog(this);
    }

    private void OpenDataDir()
    {
        try
        {
            var dir = Platform.AppDataDir;
            Directory.CreateDirectory(dir);
            OpenUrl(dir);
        }
        catch { }
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    public static void ShowPrivacy()
    {
        var w = Instance;
        if (w == null) return;
        w.Show();
        _ = ConsentService.RunAsync(w, w.S, false);
    }

    private void RefreshPrivacyStatus()
    {
        if (_privacyStatus == null) return;
        var policy = S.ConsentPrivacyPolicy ? "已同意" : "未同意";
        var face = S.ConsentFaceProcessing ? "已授权" : "未授权";
        var time = string.IsNullOrEmpty(S.ConsentTime) ? "" : $" ｜ 同意时间：{S.ConsentTime}";
        _privacyStatus.Text = $"《隐私政策》：{policy} ｜ 人脸处理：{face}{time}";
    }

    private bool NeedsOpenMainGate() => S.PasswordEnabled && S.ProtectOpenMain;

    private int SessionTtlMinutes => Math.Max(0, S.SecuritySessionMinutes);

    private bool IsSecurityUnlockedNow() =>
        (_securityUnlocked || SecurityService.SessionFace) && SecurityService.IsSessionActive(SessionTtlMinutes);

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _hidden = true;
        _securityUnlocked = false;
        SecurityService.ResetSession();
        base.OnClosing(e);
    }

    private void OnReopen()
    {
        if (!_hidden) return;
        _hidden = false;
        if (NeedsOpenMainGate() && !SecurityService.IsSessionActive(SessionTtlMinutes))
        {
            _mainLocked = true;
            _securityUnlocked = false;
            ShowOpenMainLockPanel();
        }
        else if (_securityBody != null && S.PasswordEnabled && S.ProtectOpenSecurity)
        {
            _securityUnlocked = false;
            RenderSecurityContent();
        }
    }

    private void EnforceSessionExpiry()
    {
        if (!IsVisible) return;
        if (SecurityService.IsSessionActive(SessionTtlMinutes)) return;
        if (NeedsOpenMainGate() && !_mainLocked)
        {
            LockMainWindow();
        }
        else if (S.PasswordEnabled && S.ProtectOpenSecurity && _securityRenderedUnlocked && !IsSecurityUnlockedNow())
        {
            _securityUnlocked = false;
            RenderSecurityContent();
        }
    }

    private void LockMainWindow()
    {
        _mainLocked = true;
        _securityUnlocked = false;
        ShowOpenMainLockPanel();
    }

    private StackPanel CreateOpenMainLockPanel()
    {
        var panel = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(28),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = Palette.PageBg
        };
        panel.Children.Add(new TextBlock
        {
            Text = "窥屿盾已锁定",
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            Foreground = Palette.TextPrimary,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = "输入密码以打开主页面。",
            FontSize = 13,
            Foreground = Palette.TextSecondary,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(MakeButton("解锁", (_) => TryOpenMainUnlock()));
        return panel;
    }

    private void ShowOpenMainLockPanel()
    {
        if (_mainLayout != null) _mainLayout.IsVisible = false;
        if (_lockPanel != null) _lockPanel.IsVisible = true;
    }

    private void UnlockMainView()
    {
        _mainLocked = false;
        if (_lockPanel != null) _lockPanel.IsVisible = false;
        if (_mainLayout != null) _mainLayout.IsVisible = true;
    }

    private void HandlePasswordRecovery()
    {
        S.ClearPasswordProtection();
        SecurityService.ResetSession();
        _securityUnlocked = false;
        UnlockMainView();
        BuildLayout();
        RefreshStatus();
        var info = new InfoDialog("密码保护已关闭", "密码保护已关闭，请重新设置密码。", "去设置");
        info.Closed += (_, _) => OpenSetPassword("设置密码");
        info.ShowDialog(this);
    }

    private async void TryOpenMainUnlock()
    {
        var r = await PasswordWindow.ShowVerify(this, "OpenMain", "解锁主页面", "输入密码以打开窥屿盾主页面。", S.PasswordHash, S.SecurityQuestion, S.SecurityAnswerHash, _engine, S, _engine.QuickVerifyAvailable);
        if (r == PasswordWindow.Outcome.Recovery)
        {
            HandlePasswordRecovery();
            return;
        }
        if (r == PasswordWindow.Outcome.Ok)
        {
            try
            {
                UnlockMainView();
                RefreshStatus();
            }
            catch (Exception ex)
            {
                try { LoggerService.LogInfo("解锁后恢复主界面异常：" + ex); } catch { }
            }
        }
    }

    private void RenderSecurityContent()
    {
        if (_securityBody == null) return;
        if (_selectedNavId != "security") return;
        _securityBody.Children.Clear();
        _securityRenderedUnlocked = false;
        var s = S;

        if (!s.PasswordEnabled)
        {
            _securityBody.Children.Add(new TextBlock
            {
                Text = "尚未启用密码保护。启用后可为退出 / 卸载 / 打开主页面 / 打开安全设置增加认证验证。",
                FontSize = 12, Foreground = Palette.TextMuted, TextWrapping = TextWrapping.Wrap
            });
            _securityBody.Children.Add(MakeButton("编辑认证方式", (_) => OpenAuthMethodsDialog()));
            return;
        }

        bool needGate = s.ProtectOpenSecurity && !IsSecurityUnlockedNow();
        if (needGate)
        {
            _securityBody.Children.Add(new TextBlock
            {
                Text = "安全设置已锁定，验证密码后可管理。",
                FontSize = 12, Foreground = Palette.TextMuted, TextWrapping = TextWrapping.Wrap
            });
            _securityBody.Children.Add(MakeButton("验证密码", async (_) =>
            {
                var r = await PasswordWindow.ShowVerify(this, "OpenSecurity", "安全设置验证", "请输入密码以管理安全设置。", s.PasswordHash, s.SecurityQuestion, s.SecurityAnswerHash, _engine, s, _engine.QuickVerifyAvailable);
                if (r == PasswordWindow.Outcome.Recovery) { HandlePasswordRecovery(); return; }
                if (r == PasswordWindow.Outcome.Ok) { _securityUnlocked = true; RenderSecurityContent(); }
            }));
            return;
        }

        var scope = new System.Text.StringBuilder();
        if (s.ProtectExit) scope.Append("退出 ");
        if (s.ProtectUninstall) scope.Append("卸载 ");
        if (s.ProtectOpenMain) scope.Append("打开主页面 ");
        if (s.ProtectOpenSecurity) scope.Append("打开安全设置 ");
        _securityBody.Children.Add(new TextBlock
        {
            Text = "已启用密码保护（保护范围：" + (scope.Length > 0 ? scope.ToString().Trim() : "无") + "）",
            FontSize = 12, Foreground = Palette.TextSecondary, TextWrapping = TextWrapping.Wrap
        });

        var sessRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        sessRow.Children.Add(MakeLabel("验证后保持解锁"));
        var sessBox = MakeNumberBox(1, 1440, S.SecuritySessionMinutes, 1, 120);
        sessBox.ValueChanged += (_, _) => { S.SecuritySessionMinutes = (int)sessBox.Value; S.Save(); };
        sessRow.Children.Add(sessBox);
        sessRow.Children.Add(new TextBlock
        {
            Text = "分钟（超时或关闭窗口再打开需重新验证）",
            FontSize = 12, Foreground = Palette.TextMuted, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap
        });
        _securityBody.Children.Add(sessRow);

        _faceHint = new TextBlock
        {
            FontSize = 12, Foreground = Palette.TextMuted, TextWrapping = TextWrapping.Wrap,
            IsVisible = false, Margin = new Thickness(0, 6, 0, 0)
        };
        _securityBody.Children.Add(_faceHint);

        BuildAuthMethodsSection(_securityBody);
        BuildProcessGuardSection(_securityBody);

        _securityBody.Children.Add(MakeButton("关闭密码保护", (_) => DisablePasswordProtection()));
        if (!string.IsNullOrEmpty(s.SecurityQuestion))
            _securityBody.Children.Add(new TextBlock { Text = "已设置保密问题：" + s.SecurityQuestion, FontSize = 12, Foreground = Palette.TextMuted, TextWrapping = TextWrapping.Wrap });
        else
            _securityBody.Children.Add(new TextBlock { Text = "未设置保密问题（忘记密码时将无法自助重置，可先关闭密码保护再重新设置）。", FontSize = 12, Foreground = Palette.TextMuted, TextWrapping = TextWrapping.Wrap });

        _securityRenderedUnlocked = true;
    }

    private void OpenSetPassword(string title)
    {
        var w = new SetPasswordWindow(S, null, title);
        w.Closed += (_, _) => { RenderSecurityContent(); RefreshStatus(); };
        w.ShowDialog(this);
    }

    private void DisablePasswordProtection()
    {
        var cd = new ConfirmDialog("关闭密码保护", "关闭后所有密码保护（退出 / 卸载 / 打开主页面 / 打开安全设置）将立即失效，确定关闭吗？", "关闭保护", "取消");
        cd.Closed += (_, _) =>
        {
            if (!cd.Confirmed) return;
            S.ClearPasswordProtection();
            GuardianService.Stop();
            _engine.ClearAllFaceAuth();
            _securityUnlocked = false;
            SecurityService.ResetSession();
            RenderSecurityContent();
            RefreshStatus();
        };
        cd.ShowDialog(this);
    }

    private static readonly (string Id, string Label)[] AllOperations =
    {
        ("Exit", "退出"), ("Uninstall", "卸载"), ("OpenMain", "打开主页面"), ("OpenSecurity", "打开安全设置")
    };

    private static string AuthKindLabel(AuthMethodKind k) => k switch
    {
        AuthMethodKind.Password => "密码",
        AuthMethodKind.Face => "人脸识别",
        AuthMethodKind.QuickFace => "快捷验证",
        AuthMethodKind.System => "系统解锁",
        AuthMethodKind.Usb => "U盘",
        _ => "未知"
    };

    private void BuildAuthMethodsSection(StackPanel body)
    {
        body.Children.Add(new Border
        {
            Height = 1,
            Background = Palette.Border,
            Margin = new Thickness(0, 10, 0, 8)
        });
        body.Children.Add(new TextBlock
        {
            Text = "认证方式（解锁顺序）",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = Palette.TextPrimary,
            Margin = new Thickness(0, 0, 0, 4)
        });
        body.Children.Add(new TextBlock
        {
            Text = "每种操作可搭配多种方式，解锁弹窗默认使用列表最上方的方式。可重复添加同一方式，拖动「⠿」调整先后顺序；每项可单独勾选生效的操作。",
            FontSize = 12, Foreground = Palette.TextMuted, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var list = new StackPanel { Spacing = 6 };
        var methods = S.AuthMethods;
        if (methods.Count == 0)
        {
            list.Children.Add(new TextBlock
            {
                Text = "尚未添加认证方式。添加后即可除密码之外，用人脸 / 系统凭据 / U盘完成验证。",
                FontSize = 12, Foreground = Palette.TextSecondary, TextWrapping = TextWrapping.Wrap
            });
        }
        foreach (var m in methods) list.Children.Add(BuildAuthMethodCard(m));
        body.Children.Add(list);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        row.Children.Add(MakeButton("编辑认证方式", (_) => OpenAuthMethodsDialog()));
        body.Children.Add(row);

        var tfCard = new Border
        {
            Background = Palette.CardBg,
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 10, 0, 0)
        };
        var tfStack = new StackPanel { Spacing = 6 };
        tfStack.Children.Add(new TextBlock
        {
            Text = "双因子解锁（可选，增强安全）",
            FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Palette.TextPrimary
        });
        tfStack.Children.Add(new TextBlock
        {
            Text = "开启后，受保护的操作（退出 / 卸载 / 打开主页面 / 打开安全设置）将要求同时使用两种不同的验证方式（例如「密码 + 人脸」「密码 + U盘」）。仅当该操作已配置至少两种不同类别的认证方式（知识 / 生物特征 / 平台凭据 / 物理密钥）时才会生效；否则自动退化为单因子。",
            FontSize = 12, Foreground = Palette.TextMuted, TextWrapping = TextWrapping.Wrap
        });
        tfStack.Children.Add(MakeCheck("启用双因子解锁（需两种不同方式）", S.TwoFactorEnabled, v =>
        {
            S.TwoFactorEnabled = v;
            S.Save();
            if (_faceHint != null)
            {
                _faceHint.Text = v
                    ? "已开启双因子解锁。请确保对应操作配置了至少两种不同类别的认证方式，否则仍按单因子处理。"
                    : "已关闭双因子解锁。";
                _faceHint.IsVisible = true;
            }
        }));
        tfCard.Child = tfStack;
        body.Children.Add(tfCard);
    }

    private static Border BuildAuthMethodCard(AuthMethodEntry m)
    {
        var card = new Border
        {
            Background = Palette.CardBg,
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10)
        };
        var stack = new StackPanel { Spacing = 4 };

        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        top.Children.Add(new TextBlock { Text = "⠿", FontSize = 13, Foreground = Palette.TextMuted, VerticalAlignment = VerticalAlignment.Center });
        top.Children.Add(new TextBlock { Text = "用" + AuthKindLabel(m.Kind) + "继续", FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        if (m.Operations.Count == 0)
            top.Children.Add(new TextBlock { Text = "（未勾选生效的操作）", FontSize = 12, Foreground = Palette.TextMuted, VerticalAlignment = VerticalAlignment.Center });
        foreach (var op in AllOperations)
            if (m.Operations.Contains(op.Id))
                top.Children.Add(new Border
                {
                    Background = Palette.AccentBg,
                    Padding = new Thickness(6, 2),
                    CornerRadius = new CornerRadius(10),
                    Child = new TextBlock { Text = op.Label, FontSize = 11, Foreground = Palette.AccentFg }
                });
        stack.Children.Add(top);

        if (m.Kind == AuthMethodKind.Usb)
        {
            var u = m.GetUsbOptions();
            stack.Children.Add(new TextBlock
            {
                Text = (u.UseFileMode ? "文件模式（写入隐藏密钥文件）" : "序列号模式（读取卷序列号，不写文件）")
                       + (string.IsNullOrEmpty(u.DriveLabel) ? "，尚未登记U盘" : "，已登记：" + u.DriveLabel),
                FontSize = 12, Foreground = Palette.TextSecondary, TextWrapping = TextWrapping.Wrap
            });
        }

        card.Child = stack;
        return card;
    }

    private void OpenAuthMethodsDialog()
    {
        var dlg = new AuthMethodsDialog(S, _engine);
        dlg.Closed += (_, _) =>
        {
            S.Save();
            _engine.ReloadFaceAuthVerifiers();
            RenderSecurityContent();
            RefreshStatus();
        };
        dlg.ShowDialog(this);
    }

    private void BuildProcessGuardSection(StackPanel body)
    {
        var sep = new Border
        {
            Height = 1,
            Background = Palette.Border,
            Margin = new Thickness(0, 10, 0, 8)
        };
        body.Children.Add(sep);
        body.Children.Add(new TextBlock
        {
            Text = "进程保护（被杀自动重启，可选）",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = Palette.TextPrimary,
            Margin = new Thickness(0, 0, 0, 4)
        });
        body.Children.Add(new TextBlock
        {
            Text = "守护进程始终在后台运行。本开关仅控制「进程被强制结束时是否锁定屏幕」--开启后可防止他人通过任务管理器等强制结束软件来绕过安全验证。需先设置密码。",
            FontSize = 12, Foreground = Palette.TextMuted, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        });

        var cb = MakeCheck("进程被强制结束时锁定屏幕（防绕过安全）", S.ProcessGuardEnabled, v => DoToggleProcessGuard(v));
        body.Children.Add(cb);

        _guardHint = new TextBlock
        {
            FontSize = 12, Foreground = Palette.TextMuted, TextWrapping = TextWrapping.Wrap,
            IsVisible = false, Margin = new Thickness(0, 6, 0, 0)
        };
        body.Children.Add(_guardHint);
    }

    private void DoToggleProcessGuard(bool on)
    {
        // 注意：看门狗守护进程（崩溃恢复）始终运行，与「进程保护」开关无关；
        // 本开关只控制「进程被强制结束时是否锁屏」这一防绕过安全行为。关闭它不会卸载守护进程。
        if (!on)
        {
            S.ProcessGuardEnabled = false;
            S.Save();
            if (_guardHint != null) { _guardHint.Text = "已关闭进程保护：被强制结束不再锁屏，但软件崩溃仍会自动恢复。"; _guardHint.IsVisible = true; }
            RenderSecurityContent();
            return;
        }
        if (!S.PasswordEnabled)
        {
            S.ProcessGuardEnabled = false;
            S.Save();
            if (_guardHint != null) { _guardHint.Text = "需先设置密码后才能启用进程保护（锁屏防绕过）。"; _guardHint.IsVisible = true; }
            RenderSecurityContent();
            return;
        }
        S.ProcessGuardEnabled = true;
        S.Save();
        GuardianService.Sync();
        if (_guardHint != null) { _guardHint.Text = "✓ 已启用进程保护：进程被强制结束将自动重启并锁屏，防止绕过安全验证。"; _guardHint.IsVisible = true; }
        RenderSecurityContent();
    }

    private void BuildWhitelistSection()
    {
        var body = AddCard("白名单人脸（可信但不触发防窥）");
        body.Children.Add(new TextBlock
        {
            FontSize = 12,
            Foreground = Palette.TextMuted,
            TextWrapping = TextWrapping.Wrap,
            Text = "把信任的人（如家人）加入白名单并录入其人脸后，这些人注视屏幕时不会触发雾化 / 弹窗 / 告警；陌生人仍会触发防护。需先录入机主人脸并开启智能防窥。"
        });

        _wlEnabledCheck = MakeCheck("启用白名单（关闭后所有人脸都按陌生人处理）", S.WhitelistEnabled, v =>
        {
            S.WhitelistEnabled = v; S.Save();
            if (_wlAddBtn != null) _wlAddBtn.IsEnabled = v;
            if (_wlHost != null) _wlHost.IsEnabled = v;
        });
        body.Children.Add(_wlEnabledCheck);

        _wlHost = new StackPanel { Spacing = 4, Margin = new Thickness(0, 6, 0, 2), IsEnabled = S.WhitelistEnabled };
        body.Children.Add(_wlHost);
        RebuildWhitelistList();

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        _wlInput = new TextBox { Width = 220, Watermark = "白名单姓名（如 家人A）", Foreground = Palette.TextPrimary, Background = Palette.CardBg };
        _wlAddBtn = MakeButton("添加白名单", (_) =>
        {
            var t = (_wlInput!.Text ?? string.Empty).Trim();
            if (t.Length == 0) return;
            if (S.Whitelist.Any(x => string.Equals(x.Name, t, StringComparison.OrdinalIgnoreCase))) return;
            var id = _engine.AddWhitelist(t);
            S.Save();
            RebuildWhitelistList();
            _wlInput.Text = "";
        });
        _wlAddBtn.IsEnabled = S.WhitelistEnabled;
        row.Children.Add(_wlInput);
        row.Children.Add(_wlAddBtn);
        body.Children.Add(row);

        _wlHint = new TextBlock
        {
            FontSize = 12,
            Foreground = Palette.TextMuted,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            Margin = new Thickness(0, 4, 0, 0)
        };
        body.Children.Add(_wlHint);

        _wlPreview = new Image { Width = 320, Height = 240, Stretch = Stretch.Uniform, IsVisible = false };
        var wlPreviewBorder = new Border
        {
            Width = 320,
            Height = 240,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Palette.CardBg,
            CornerRadius = new CornerRadius(8),
            Child = _wlPreview,
            IsVisible = false
        };
        _wlPreviewBorder = wlPreviewBorder;
        body.Children.Add(wlPreviewBorder);
    }

    private Border? _wlPreviewBorder;

    private void UnsubscribeWhitelistPreview()
    {
        if (_wlPreviewHandler != null)
        {
            _engine.PreviewFrame -= _wlPreviewHandler;
            _wlPreviewHandler = null;
        }
        _engine.ReleasePreviewRef();
    }

    private void RebuildWhitelistList()
    {
        if (_wlHost == null) return;
        _wlHost.Children.Clear();
        if (S.Whitelist.Count == 0)
        {
            _wlHost.Children.Add(new TextBlock
            {
                Text = "（暂无白名单，添加姓名并录入人脸后即可生效）",
                FontSize = 12,
                Foreground = Palette.TextFaint,
                Margin = new Thickness(2, 2, 0, 2)
            });
            return;
        }
        foreach (var e in S.Whitelist)
            _wlHost.Children.Add(MakeWhitelistRow(e));
    }

    private StackPanel MakeWhitelistRow(WhitelistEntry entry)
    {
        var card = new StackPanel { Spacing = 3, Margin = new Thickness(0, 3, 0, 3) };

        var row1 = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var cb = new CheckBox { IsChecked = entry.Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        cb.IsCheckedChanged += (_, _) => { entry.Enabled = cb.IsChecked == true; S.Save(); };
        Grid.SetColumn(cb, 0);

        var nameBox = new TextBox { Text = entry.Name, VerticalAlignment = VerticalAlignment.Center, Watermark = "白名单姓名", Foreground = Palette.TextPrimary, Background = Palette.CardBg };
        nameBox.TextChanged += (_, _) => _engine.RenameWhitelist(entry.Id, nameBox.Text.Trim());
        Grid.SetColumn(nameBox, 1);

        var del = MakeButton("删除", (_) =>
        {
            var cd = new ConfirmDialog("删除白名单", $"确定删除「{entry.Name}」？其人脸数据将从本地删除。", "删除", "取消");
            cd.Closed += (_, _) =>
            {
                if (!cd.Confirmed) return;
                _engine.RemoveWhitelist(entry.Id);
                RebuildWhitelistList();
            };
            cd.ShowDialog(this);
        });
        Grid.SetColumn(del, 2);

        row1.Children.Add(cb);
        row1.Children.Add(nameBox);
        row1.Children.Add(del);

        var row2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 2, 0, 0) };
        row2.Children.Add(MakeButton("录入人脸", async (_) => await DoEnrollWhitelist(entry.Id)));
        row2.Children.Add(MakeButton("上传照片", async (_) => await DoEnrollWhitelistPhoto(entry.Id)));
        int n = _engine.WhitelistSampleCount(entry.Id);
        row2.Children.Add(new TextBlock
        {
            Text = n > 0 ? $"已录入 {n} 张样本" : "未录入人脸",
            FontSize = 12,
            Foreground = Palette.TextMuted,
            VerticalAlignment = VerticalAlignment.Center
        });

        card.Children.Add(row1);
        card.Children.Add(row2);
        return card;
    }

    private async Task DoEnrollWhitelist(string id)
    {
        if (_wlBusy) return;
        _wlBusy = true;
        if (_wlAddBtn != null) _wlAddBtn.IsEnabled = false;
        ShowWhitelistPreview();
        UpdateWhitelistHint("录入中… 请正对摄像头保持静止（约 3 秒）");
        bool ok = await _engine.EnrollWhitelistAsync(id, 12, (n) => UpdateWhitelistHint($"已采集 {n} 张人脸样本…"));
        HideWhitelistPreview();
        UpdateWhitelistHint(ok ? "✓ 白名单录入成功" : "✗ 录入失败：未采集到足够清晰的人脸，请重试");
        RebuildWhitelistList();
        if (_wlAddBtn != null) _wlAddBtn.IsEnabled = S.WhitelistEnabled;
        _wlBusy = false;
    }

    private void ShowWhitelistPreview()
    {
        UnsubscribeWhitelistPreview();
        if (_wlPreviewBorder != null) _wlPreviewBorder.IsVisible = true;
        if (_wlPreview != null) _wlPreview.IsVisible = true;
        _wlPreviewHandler = OnWhitelistPreviewFrame;
        _engine.AddPreviewRef();
        _engine.PreviewFrame += _wlPreviewHandler;
    }

    private void HideWhitelistPreview()
    {
        UnsubscribeWhitelistPreview();
        if (_wlPreviewBorder != null) _wlPreviewBorder.IsVisible = false;
        if (_wlPreview != null) _wlPreview.IsVisible = false;
    }

    private void OnWhitelistPreviewFrame(OpenCvSharp.Mat mat)
    {
        if (_wlPreview == null || mat == null || mat.Empty()) { mat?.Dispose(); return; }
        if (_wlPreviewBusy) { mat.Dispose(); return; }
        _wlPreviewBusy = true;
        _ = Task.Run(() =>
        {
            try
            {
                var bytes = mat.ToBytes(".png");
                mat.Dispose();
                if (bytes == null || bytes.Length == 0) { _wlPreviewBusy = false; return; }
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        if (_wlPreview == null) return;
                        using var ms = new MemoryStream(bytes);
                        var old = _wlPreview.Source as IDisposable;
                        _wlPreview.Source = new Bitmap(ms);
                        old?.Dispose();
                    }
                    catch { }
                    finally { _wlPreviewBusy = false; }
                });
            }
            catch
            {
                mat.Dispose();
                _wlPreviewBusy = false;
            }
        });
    }

    private async Task DoEnrollWhitelistPhoto(string id)
    {
        if (_wlBusy) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择包含该白名单人员正脸的照片",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("图片") { Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.bmp" } } }
        });
        if (files == null || files.Count == 0) return;
        var path = files[0].Path.LocalPath;
        _wlBusy = true;
        if (_wlAddBtn != null) _wlAddBtn.IsEnabled = false;
        UpdateWhitelistHint("照片录入中… 正在本地分析人脸特征");
        bool ok = await _engine.EnrollWhitelistFromPhotoAsync(id, path, (n) => UpdateWhitelistHint($"已生成 {n} 个人脸特征样本…"));
        UpdateWhitelistHint(ok ? "✓ 白名单照片录入成功" : "✗ 未从照片中检测到清晰正脸，请换一张重新上传");
        RebuildWhitelistList();
        if (_wlAddBtn != null) _wlAddBtn.IsEnabled = S.WhitelistEnabled;
        _wlBusy = false;
    }

    private void UpdateWhitelistHint(string? text = null)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_wlHint == null) return;
            if (string.IsNullOrEmpty(text)) { _wlHint.IsVisible = false; return; }
            _wlHint.IsVisible = true;
            _wlHint.Text = text;
        });
    }
}
