using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PeekShield.Models;
using PeekShield.Services;

namespace PeekShield.Views;

public sealed class AuthMethodsDialog : Window
{
    private readonly PeekShieldSettings _settings;
    private readonly PeekShieldEngine? _engine;
    private readonly StackPanel _listPanel;
    private readonly ComboBox _addCombo;
    private readonly Dictionary<AuthMethodEntry, Border> _rows = new();
    private int _dragIndex = -1;
    private Point _dragPoint;
    private readonly List<AuthMethodEntry> _workingList;
    private bool _confirmed;

    private static readonly string[] OpNames = { "Exit", "Uninstall", "OpenMain", "OpenSecurity" };
    private static readonly string[] OpLabels = { "退出", "卸载", "打开主页面", "打开安全设置" };
    private readonly CheckBox[] _scopeChecks = new CheckBox[OpNames.Length];
    private readonly Border _scopeCard = new();

    public AuthMethodsDialog(PeekShieldSettings settings, PeekShieldEngine? engine)
    {
        _settings = settings;
        _engine = engine;
        _workingList = settings.AuthMethods.Select(m => new AuthMethodEntry
        {
            Id = m.Id,
            Kind = m.Kind,
            Name = m.Name,
            OptionsJson = m.OptionsJson,
            Operations = new List<string>(m.Operations)
        }).ToList();

        Title = "编辑认证项目";
        Width = 560;
        Height = 560;
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

        var root = new StackPanel { Spacing = 12, Margin = new Thickness(20) };

        root.Children.Add(new TextBlock
        {
            Text = "编辑进行此操作需要的认证方式。",
            FontSize = 13,
            Foreground = Palette.TextSecondary,
            TextWrapping = TextWrapping.Wrap
        });

        var addRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _addCombo = new ComboBox { MinWidth = 160, VerticalAlignment = VerticalAlignment.Center };
        _addCombo.Items.Add(new ComboBoxItem { Content = "密码", Tag = AuthMethodKind.Password });
        _addCombo.Items.Add(new ComboBoxItem { Content = "人脸识别", Tag = AuthMethodKind.Face });
        _addCombo.Items.Add(new ComboBoxItem { Content = "快捷验证", Tag = AuthMethodKind.QuickFace });
        if (OperatingSystem.IsWindows())
            _addCombo.Items.Add(new ComboBoxItem { Content = "系统解锁", Tag = AuthMethodKind.System });
        _addCombo.Items.Add(new ComboBoxItem { Content = "U盘", Tag = AuthMethodKind.Usb });
        _addCombo.SelectedIndex = 0;
        addRow.Children.Add(_addCombo);
        var addBtn = new Button { Content = "+ 添加认证方式", Padding = new Thickness(10, 5) };
        addBtn.Click += (_, _) => DoAdd();
        addRow.Children.Add(addBtn);
        root.Children.Add(addRow);

        BuildScopePanel();
        root.Children.Add(_scopeCard);

        _listPanel = new StackPanel { Spacing = 8 };
        root.Children.Add(_listPanel);

        var btnRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var ok = new Button { Content = "确定", MinWidth = 80, Padding = new Thickness(10, 5), Background = new SolidColorBrush(Color.Parse("#2563EB")), Foreground = new SolidColorBrush(Colors.White) };
        ok.Click += (_, _) => { _confirmed = true; ApplyAndClose(); };
        var cancel = new Button { Content = "取消", MinWidth = 80, Padding = new Thickness(10, 5) };
        cancel.Click += (_, _) => Close();
        btnRow.Children.Add(cancel);
        btnRow.Children.Add(ok);
        root.Children.Add(btnRow);

        Content = new ScrollViewer { Content = root };
        RefreshList();

        Closed += (_, _) =>
        {
            if (_confirmed) return;
            try
            {
                var facesDir = Path.Combine(Platform.EnrollDir, "faces");
                if (!Directory.Exists(facesDir)) return;
                foreach (var dir in Directory.GetDirectories(facesDir))
                {
                    var id = Path.GetFileName(dir);
                    if (!_settings.AuthMethods.Any(m => m.Id == id && m.Kind == AuthMethodKind.Face))
                        Directory.Delete(dir, true);
                }
            }
            catch { }
        };
    }

    public bool Confirmed => _confirmed;

    private static string KindLabel(AuthMethodKind k) => k switch
    {
        AuthMethodKind.Password => "密码",
        AuthMethodKind.Face => "人脸识别",
        AuthMethodKind.QuickFace => "快捷验证",
        AuthMethodKind.System => "系统解锁",
        AuthMethodKind.Usb => "U盘",
        _ => "未知"
    };

    private void DoAdd()
    {
        if (_addCombo.SelectedItem is not ComboBoxItem item || item.Tag is not AuthMethodKind kind) return;
        if (kind != AuthMethodKind.Password && !_workingList.Exists(m => m.Kind == AuthMethodKind.Password))
        {
            _ = new ConfirmDialog("需要先设置密码", "添加其他认证方式前，请先在列表中添加至少一个密码认证方式。\n\n管理员密码是使用其他人脸 / 系统 / U盘认证的前提。", "知道了") { ShowInTaskbar = false }.ShowDialog(this);
            return;
        }
        if (kind == AuthMethodKind.Password)
        {
            var w = new SetPasswordWindow(_settings, null, "添加密码");
            w.Closed += (_, _) =>
            {
                if (_settings.AuthMethods.Count > 0)
                {
                    // 重新从 settings 同步工作列表（新条目已写入 settings）
                    SyncFromSettings();
                    RefreshList();
                }
            };
            w.ShowDialog(this);
            return;
        }
        var entry = new AuthMethodEntry { Kind = kind };
        if (kind == AuthMethodKind.Usb)
        {
            entry.SetUsbOptions(new AuthUsbOptions());
        }
        if (entry.Operations.Count == 0) entry.Operations = new List<string>(OpNames);
        _workingList.Add(entry);
        RefreshList();
    }

    private void SyncFromSettings()
    {
        _workingList.Clear();
        foreach (var m in _settings.AuthMethods)
        {
            _workingList.Add(new AuthMethodEntry
            {
                Id = m.Id,
                Kind = m.Kind,
                Name = m.Name,
                OptionsJson = m.OptionsJson,
                Operations = new List<string>(m.Operations)
            });
        }
    }

    private void RefreshList()
    {
        _listPanel.Children.Clear();
        _rows.Clear();
        for (int i = 0; i < _workingList.Count; i++)
        {
            var row = BuildRow(_workingList[i], i);
            _listPanel.Children.Add(row);
            _rows[_workingList[i]] = row;
        }
    }

    private Button MakeActionButton(string text, bool enabled, Action click, IBrush? foreground = null)
    {
        var btn = new Button
        {
            Content = text,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 4),
            MinWidth = 28,
            FontSize = 13,
            Foreground = foreground ?? Palette.TextSecondary,
            IsEnabled = enabled,
            Opacity = enabled ? 1.0 : 0.35,
            Cursor = enabled ? new Cursor(StandardCursorType.Hand) : Cursor.Default,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        if (enabled)
        {
            btn.Click += (_, _) => click();
            btn.PointerEntered += (_, _) => btn.Background = new SolidColorBrush(Palette.Border.Color) { Opacity = 0.35 };
            btn.PointerExited += (_, _) => btn.Background = Brushes.Transparent;
        }
        return btn;
    }

    private Border BuildRow(AuthMethodEntry m, int index)
    {
        var card = new Border
        {
            Background = Palette.CardBg,
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 6)
        };

        var stack = new StackPanel { Spacing = 8 };
        card.Child = stack;

        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var handle = new TextBlock
        {
            Text = "⠿", FontSize = 14, VerticalAlignment = VerticalAlignment.Center,
            Foreground = Palette.TextMuted
        };
        handle.PointerPressed += (_, e) =>
        {
            _dragIndex = index;
            _dragPoint = e.GetPosition(_listPanel);
            e.Pointer.Capture(handle);
            e.Handled = true;
            if (_listPanel.Children[index] is Control c0) c0.Opacity = 0.45;
        };
        handle.PointerMoved += (_, e) =>
        {
            if (_dragIndex < 0) return;
            _dragPoint = e.GetPosition(_listPanel);
        };
        handle.PointerReleased += (_, e) =>
        {
            if (_dragIndex < 0) return;
            int from = _dragIndex;
            _dragIndex = -1;
            if (_listPanel.Children[Math.Min(from, _listPanel.Children.Count - 1)] is Control c1) c1.Opacity = 1;
            int target = 0;
            for (int i = 0; i < _listPanel.Children.Count; i++)
            {
                var bb = _listPanel.Children[i].Bounds;
                if (_dragPoint.Y > bb.Top + bb.Height / 2) target = i + 1;
            }
            if (target > from) target--;
            if (target != from && target >= 0 && target < _workingList.Count) Move(from, target);
        };
        var icon = new TextBlock { Text = KindIcon(m.Kind), FontSize = 16, VerticalAlignment = VerticalAlignment.Center };

        var isAdmin = m.Kind == AuthMethodKind.Password && _workingList.Find(x => x.Kind == AuthMethodKind.Password) == m;
        var canDelete = !(m.Kind == AuthMethodKind.Password && isAdmin);

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(handle);
        left.Children.Add(icon);
        if (m.Kind == AuthMethodKind.Password)
        {
            var nameBox = new TextBox
            {
                Text = string.IsNullOrEmpty(m.Name) ? (isAdmin ? "管理员密码" : "用户密码") : m.Name,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                MinWidth = 120,
                MaxWidth = 220,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(6, 4)
            };
            nameBox.LostFocus += (_, _) => m.Name = nameBox.Text?.Trim() ?? "";
            left.Children.Add(nameBox);
            if (isAdmin)
            {
                left.Children.Add(new Border
                {
                    Background = Palette.AccentBg,
                    Padding = new Thickness(6, 2),
                    CornerRadius = new CornerRadius(10),
                    Child = new TextBlock { Text = "管理员", FontSize = 11, Foreground = Palette.AccentFg }
                });
            }
        }
        else if (m.Kind == AuthMethodKind.Face || m.Kind == AuthMethodKind.QuickFace)
        {
            var nameBox = new TextBox
            {
                Text = string.IsNullOrEmpty(m.Name) ? KindLabel(m.Kind) : m.Name,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                MinWidth = 120,
                MaxWidth = 220,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(6, 4)
            };
            nameBox.LostFocus += (_, _) => m.Name = nameBox.Text?.Trim() ?? "";
            left.Children.Add(nameBox);
            left.Children.Add(new TextBlock
            {
                Text = $"（{KindLabel(m.Kind)}）",
                FontSize = 12,
                Foreground = Palette.TextMuted,
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        else
        {
            var title = new TextBlock { Text = $"用{KindLabel(m.Kind)}继续", FontSize = 14, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(title);
        }
        Grid.SetColumn(left, 0);
        top.Children.Add(left);

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var up = MakeActionButton("↑", index > 0, () => Move(index, -1));
        var down = MakeActionButton("↓", index < _workingList.Count - 1, () => Move(index, 1));
        var remove = MakeActionButton("×", canDelete, () =>
        {
            if (m.Kind == AuthMethodKind.Password)
            {
                var pwdCount = _workingList.Count(x => x.Kind == AuthMethodKind.Password);
                if (pwdCount <= 1)
                {
                    _ = new ConfirmDialog("无法删除", "管理员密码是最基础的认证方式，必须至少保留一个密码条目。", "知道了") { ShowInTaskbar = false }.ShowDialog(this);
                    return;
                }
            }
            if (m.Kind == AuthMethodKind.Face || m.Kind == AuthMethodKind.QuickFace)
                _engine?.ClearFaceAuth(m.Id);
            _workingList.RemoveAt(index);
            RefreshList();
        }, canDelete ? Palette.TextSecondary : Palette.TextMuted);
        if (!canDelete)
        {
            ToolTip.SetTip(remove, "管理员密码是使用其他人脸 / 系统 / U盘认证的前提，不可删除");
        }

        right.Children.Add(up);
        right.Children.Add(down);
        right.Children.Add(remove);
        Grid.SetColumn(right, 1);
        top.Children.Add(right);
        stack.Children.Add(top);

        if (m.Kind == AuthMethodKind.Password)
        {
            var pwdRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var editPwd = new Button { Content = "修改密码", Padding = new Thickness(8, 4) };
            editPwd.Click += (_, _) =>
            {
                var w = new SetPasswordWindow(_settings, m, "修改密码");
                w.Closed += (_, _) =>
                {
                    // 修改后的密码和范围已经写入 settings 和 entry；刷新列表即可
                    RefreshList();
                };
                w.ShowDialog(this);
            };
            pwdRow.Children.Add(editPwd);
            stack.Children.Add(pwdRow);
        }

        if (m.Kind == AuthMethodKind.Usb)
        {
            var usb = m.GetUsbOptions();
            var usbRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            var toggle = new ToggleSwitch { IsChecked = usb.UseFileMode, OffContent = "序列号模式", OnContent = "文件模式" };
            toggle.IsCheckedChanged += (_, _) =>
            {
                var opts = m.GetUsbOptions();
                opts.UseFileMode = toggle.IsChecked == true;
                m.SetUsbOptions(opts);
            };
            usbRow.Children.Add(toggle);
            var status = new TextBlock
            {
                Text = string.IsNullOrEmpty(usb.DriveLabel) ? "尚未登记U盘" : $"已登记：{usb.DriveLabel}",
                FontSize = 12,
                Foreground = Palette.TextSecondary,
                VerticalAlignment = VerticalAlignment.Center
            };
            usbRow.Children.Add(status);
            stack.Children.Add(usbRow);
        }

        if (isAdmin)
        {
            stack.Children.Add(new TextBlock
            {
                Text = "适用于所有保护操作（最高权限）",
                FontSize = 12,
                Foreground = Palette.AccentFg,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            });
        }
        else
        {
            var opsRow = new StackPanel { Spacing = 6 };
            opsRow.Children.Add(new TextBlock { Text = "适用于：", FontSize = 12, Foreground = Palette.TextMuted });
            var checks = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            for (int i = 0; i < OpNames.Length; i++)
            {
                var op = OpNames[i];
                var label = OpLabels[i];
                var cb = new CheckBox { Content = label, IsChecked = m.Operations.Contains(op) };
                cb.IsCheckedChanged += (_, _) =>
                {
                    if (cb.IsChecked == true)
                    { if (!m.Operations.Contains(op)) m.Operations.Add(op); }
                    else
                    { m.Operations.Remove(op); }
                };
                checks.Children.Add(cb);
            }
            opsRow.Children.Add(checks);
            stack.Children.Add(opsRow);
        }

        if (m.Kind == AuthMethodKind.Face || m.Kind == AuthMethodKind.QuickFace)
        {
            var faceRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
            if (m.Kind == AuthMethodKind.QuickFace)
            {
                var ownerCount = _engine?.FaceAuthSampleCount(m.Id) ?? 0;
                var status = new TextBlock
                {
                    Text = ownerCount > 0 ? $"已录入机主人脸（{ownerCount} 张样本）" : "尚未录入机主人脸，请在主设置页录入",
                    FontSize = 12,
                    Foreground = ownerCount > 0 ? Palette.TextSecondary : Palette.TextMuted,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var hint = new TextBlock
                {
                    Text = "使用机主人脸数据，无需单独录入",
                    FontSize = 11,
                    Foreground = Palette.TextMuted,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0)
                };
                faceRow.Children.Add(status);
                faceRow.Children.Add(hint);
            }
            else
            {
                var sampleCount = _engine?.FaceAuthSampleCount(m.Id) ?? 0;
                var status = new TextBlock
                {
                    Text = sampleCount > 0 ? $"已录入 {sampleCount} 张样本" : "未录入人脸",
                    FontSize = 12,
                    Foreground = Palette.TextSecondary,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var enrollBtn = new Button { Content = "录入人脸", Padding = new Thickness(8, 4) };
                enrollBtn.Click += async (_, _) =>
                {
                    if (_engine == null) return;
                    status.Text = "录入中…";
                    bool ok = await _engine.EnrollFaceAuthAsync(m.Id, 12, n =>
                        Dispatcher.UIThread.Post(() => { if (n > 0) status.Text = $"已采集 {n} 张样本…"; }));
                    var count = _engine.FaceAuthSampleCount(m.Id);
                    status.Text = count > 0 ? $"已录入 {count} 张样本" : "未录入人脸";
                    if (!ok)
                        _ = new ConfirmDialog("录入失败", "未采集到足够清晰的人脸，请重试。", "知道了") { ShowInTaskbar = false }.ShowDialog(this);
                };
                var photoBtn = new Button { Content = "上传照片录入", Padding = new Thickness(8, 4) };
                photoBtn.Click += async (_, _) =>
                {
                    if (_engine == null) return;
                    var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                    {
                        Title = "选择一张包含正脸的人脸照片",
                        AllowMultiple = false,
                        FileTypeFilter = new[] { new FilePickerFileType("图片") { Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.bmp" } } }
                    });
                    if (files == null || files.Count == 0) return;
                    status.Text = "照片录入中…";
                    bool ok = await _engine.EnrollFaceAuthFromPhotoAsync(m.Id, files[0].Path.LocalPath);
                    var count = _engine.FaceAuthSampleCount(m.Id);
                    status.Text = count > 0 ? $"已录入 {count} 张样本" : "未录入人脸";
                    if (!ok)
                        _ = new ConfirmDialog("录入失败", "未从照片中检测到清晰正脸，请换一张重新上传。", "知道了") { ShowInTaskbar = false }.ShowDialog(this);
                };
                var clearBtn = new Button { Content = "清空人脸数据", Padding = new Thickness(8, 4) };
                clearBtn.Click += (_, _) =>
                {
                    _engine?.ClearFaceAuth(m.Id);
                    status.Text = "未录入人脸";
                };
                faceRow.Children.Add(status);
                faceRow.Children.Add(enrollBtn);
                faceRow.Children.Add(photoBtn);
                faceRow.Children.Add(clearBtn);
            }
            stack.Children.Add(faceRow);
        }

        if (m.Kind == AuthMethodKind.Usb)
        {
            var regRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
            var regBtn = new Button { Content = "登记U盘", Padding = new Thickness(8, 4) };
            regBtn.Click += async (_, _) =>
            {
                await PickAndRegister(this, m);
                RefreshList();
            };
            regRow.Children.Add(regBtn);
            stack.Children.Add(regRow);
        }

        return card;
    }

    private static string KindIcon(AuthMethodKind k) => k switch
    {
        AuthMethodKind.Password => "🔒",
        AuthMethodKind.Face => "👤",
        AuthMethodKind.QuickFace => "⚡",
        AuthMethodKind.System => "🖥️",
        AuthMethodKind.Usb => "💾",
        _ => "•"
    };

    private void Move(int index, int delta)
    {
        int target = index + delta;
        if (target < 0 || target >= _workingList.Count) return;
        var m = _workingList[index];
        _workingList.RemoveAt(index);
        _workingList.Insert(target, m);
        RefreshList();
    }

    public static async System.Threading.Tasks.Task<bool> PickAndRegister(Window owner, AuthMethodEntry m)
    {
        var opts = m.GetUsbOptions();
        var vols = UsbUnlockService.ListCandidateVolumes();
        var combo = new ComboBox { MinWidth = 300 };
        if (vols.Count == 0)
            combo.Items.Add(new ComboBoxItem { Content = "（未检测到可移动磁盘）" });
        else
            foreach (var v in vols)
                combo.Items.Add(new ComboBoxItem { Content = UsbUnlockService.LabelOf(v) + "  (" + v.RootPath + ")", Tag = v });
        combo.SelectedIndex = 0;

        var w = new Window
        {
            Title = "设置验证存储器",
            Width = 420,
            Height = 200,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = Palette.PageBg,
            Content = new StackPanel { Spacing = 10, Margin = new Thickness(16), Children = { new TextBlock { Text = "选择要登记为解锁钥匙的U盘：", Foreground = Palette.TextSecondary }, combo, new Button { Content = "登记", Padding = new Thickness(10, 5), HorizontalAlignment = HorizontalAlignment.Right } } }
        };
        var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
        w.Closed += (_, _) => tcs.TrySetResult(true);

        var btn = ((w.Content as StackPanel)!.Children[2] as Button)!;
        btn.Click += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem item && item.Tag is UsbVolume vol)
            {
                if (opts.UseFileMode)
                {
                    var token = UsbUnlockService.GenerateToken();
                    if (!UsbUnlockService.WriteToken(vol.RootPath, token)) return;
                    opts.TokenHash = UsbUnlockService.HashToken(token);
                }
                else
                {
                    var serial = UsbUnlockService.GetVolumeSerial(vol.RootPath);
                    if (serial == null) return;
                    opts.SerialHash = UsbUnlockService.HashToken(serial);
                }
                opts.DriveLabel = UsbUnlockService.LabelOf(vol);
                m.SetUsbOptions(opts);
            }
            w.Close();
        };
        await w.ShowDialog(owner);
        return await tcs.Task;
    }

    private void ApplyAndClose()
    {
        if (!_workingList.Exists(m => m.Kind == AuthMethodKind.Password))
        {
            _ = new ConfirmDialog("无法保存", "必须保留至少一个密码认证方式。", "知道了") { ShowInTaskbar = false }.ShowDialog(this);
            return;
        }
        foreach (var op in OpNames)
        {
            bool anyNonPwd = _workingList.Any(m => m.Kind != AuthMethodKind.Password && m.Operations.Contains(op));
            bool anyPwd = _workingList.Any(m => m.Kind == AuthMethodKind.Password && m.Operations.Contains(op));
            if (anyNonPwd && !anyPwd)
            {
                _ = new ConfirmDialog("无法保存", $"勾选了「{OpLabels[Array.IndexOf(OpNames, op)]}」的人脸 / 系统 / U盘认证方式，必须至少有一个密码方式也勾选同一操作。\n\n管理员密码是使用其他认证方式的前提。", "知道了") { ShowInTaskbar = false }.ShowDialog(this);
                return;
            }
        }
        // 确保管理员密码始终拥有全部已勾选的保护范围
        SyncAdminPasswordOperations();
        // 非管理员条目的范围不能超出全局保护范围
        var enabledOps = new HashSet<string>(OpNames.Where((_, i) => _scopeChecks[i]?.IsChecked == true));
        foreach (var m in _workingList)
        {
            if (m.Kind == AuthMethodKind.Password && _workingList.Find(x => x.Kind == AuthMethodKind.Password) == m) continue;
            m.Operations = m.Operations.Where(enabledOps.Contains).ToList();
            if (m.Operations.Count == 0) m.Operations = new List<string>(enabledOps);
        }

        _settings.AuthMethods = _workingList.Select(m => new AuthMethodEntry
        {
            Id = m.Id,
            Kind = m.Kind,
            Name = m.Name,
            OptionsJson = m.OptionsJson,
            Operations = new List<string>(m.Operations)
        }).ToList();
        _settings.SyncLegacyAuthBooleans();
        _settings.Save();
        Close();
    }

    private void BuildScopePanel()
    {
        var stack = new StackPanel { Spacing = 8 };
        _scopeCard.Background = Palette.CardBg;
        _scopeCard.BorderBrush = Palette.Border;
        _scopeCard.BorderThickness = new Thickness(1);
        _scopeCard.CornerRadius = new CornerRadius(6);
        _scopeCard.Padding = new Thickness(12);
        _scopeCard.Margin = new Thickness(0, 0, 0, 4);

        stack.Children.Add(new TextBlock
        {
            Text = "需要保护的操作",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = Palette.TextPrimary
        });
        stack.Children.Add(new TextBlock
        {
            Text = "勾选后对应操作将要求验证；未勾选的操作可直接执行。管理员密码自动拥有全部权限。",
            FontSize = 12,
            Foreground = Palette.TextSecondary,
            TextWrapping = TextWrapping.Wrap
        });

        var checks = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 4, 0, 0) };
        var initial = new bool[] { _settings.ProtectExit, _settings.ProtectUninstall, _settings.ProtectOpenMain, _settings.ProtectOpenSecurity };
        for (int i = 0; i < OpNames.Length; i++)
        {
            var cb = new CheckBox { Content = OpLabels[i], IsChecked = initial[i] };
            var idx = i;
            cb.IsCheckedChanged += (_, _) =>
            {
                SyncScopeBool(idx, cb.IsChecked == true);
                SyncAdminPasswordOperations();
                RefreshList();
            };
            _scopeChecks[i] = cb;
            checks.Children.Add(cb);
        }
        stack.Children.Add(checks);
        _scopeCard.Child = stack;
    }

    private void SyncScopeBool(int index, bool enabled)
    {
        switch (OpNames[index])
        {
            case "Exit": _settings.ProtectExit = enabled; break;
            case "Uninstall": _settings.ProtectUninstall = enabled; break;
            case "OpenMain": _settings.ProtectOpenMain = enabled; break;
            case "OpenSecurity": _settings.ProtectOpenSecurity = enabled; break;
        }
    }

    private void SyncAdminPasswordOperations()
    {
        var admin = _workingList.Find(m => m.Kind == AuthMethodKind.Password);
        if (admin == null) return;
        var ops = new List<string>();
        for (int i = 0; i < OpNames.Length; i++)
            if (_scopeChecks[i]?.IsChecked == true) ops.Add(OpNames[i]);
        admin.Operations = ops;
    }
}
