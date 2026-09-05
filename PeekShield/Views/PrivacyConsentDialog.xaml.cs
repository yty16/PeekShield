using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using PeekShield;
using PeekShield.Services;

namespace PeekShield.Views;

public sealed class PrivacyConsentDialog : Window
{
    public bool Accepted { get; private set; }
    public bool FaceAgreed { get; private set; }

    private readonly bool _required;
    private readonly CheckBox _cbPolicy = new();
    private readonly CheckBox _cbFace = new();
    private readonly Button _btnAccept = new();
    private readonly TextBlock _hint = new();
    private bool _decided;

    public PrivacyConsentDialog(bool required)
    {
        _required = required;

        Title = "隐私告知与同意 · PeekShield 窥屿盾";
        Width = 620;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = !required;
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
            Text = "隐私告知与同意",
            Foreground = Palette.TextPrimary,
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        };

        var subtitle = new TextBlock
        {
            Text = required
                ? "本软件首次启动需要你确认以下内容方可继续使用。全部人脸数据在本机处理，不上传网络。"
                : "你可随时重新查看并调整授权。全部人脸数据在本机处理，不上传网络。",
            Foreground = Palette.TextMuted,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };

        var body = new StackPanel { Margin = new Thickness(2, 0, 6, 0) };
        body.Children.Add(MakeSection("一、处理目的",
            "用于本地防窥侦测：判断是否有陌生人正对你的屏幕注视，在有人偷窥时遮挡屏幕并提醒你。"));
        body.Children.Add(MakeSection("二、处理方式",
            "· 摄像头实时采集画面 → 本地 dlib 模型提取人脸特征 → 与已录入的机主特征在本机比对。",
            "· 不保存原始照片，不上传网络；全部计算在本机完成，可完全离线使用。",
            "· 默认仅在受保护程序处于前台时检测（可在设置中关闭该限制）。"));
        body.Children.Add(MakeSection("三、保存期限",
            "· 人脸特征数据（128 维向量）：保存至你主动删除，或达到设置的自动清理保留天数后自动清除。",
            "· 实时画面帧：仅在内存中处理，不写入磁盘。",
            "· 例外：开启「自动保存截屏到本地日志」后，检测到偷窥时会保存截图与调试帧到本机日志目录，同样受保留期限约束。"));
        body.Children.Add(MakeSection("四、对个人权益的影响",
            "· 摄像头可能在后台运行，摄像头指示灯会亮起。",
            "· 算法存在误判可能，可能导致屏幕被临时遮挡、弹出提醒或受保护程序被最小化。",
            "· 你可通过托盘菜单或快捷键随时暂停防护。"));
        body.Children.Add(MakeSection("五、你的权利（查阅 / 删除 / 撤回同意）",
            "· 查阅：主界面「隐私与授权」卡片或托盘菜单「隐私与授权」中可「打开数据目录」，查看本机保存的全部数据。",
            "· 删除：数据目录中可手动删除人脸数据、日志与截图；也可在「隐私与授权」中撤回同意后重新录入或清除。",
            "· 撤回同意：在「隐私与授权」中可「撤回全部同意」，撤回后软件立即停止摄像头侦测，其他功能（如手动防窥）仍可使用；下次启动会重新征求你的同意。",
            "· 撤回后数据：已保存的人脸特征数据你可选择保留或删除；未设置自动清理时，人脸特征数据将持续保存在本地，直至你主动删除。"));
        body.Children.Add(MakeSection("六、说明",
            "本告知构成 PeekShield《隐私政策》的核心内容，完整文本见项目内 PRIVACY.md。"));

        var scroll = new ScrollViewer
        {
            Content = body,
            Height = 300,
            Background = Palette.CardBg,
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 6)
        };
        var scrollBorder = new Border
        {
            Background = Palette.CardBg,
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = scroll
        };

        _cbPolicy.Content = MakeCheckText("我已阅读并同意《隐私政策》");
        _cbPolicy.Foreground = Palette.TextPrimary;
        _cbPolicy.FontSize = 13;
        _cbPolicy.Margin = new Thickness(0, 2, 0, 2);
        _cbPolicy.IsCheckedChanged += (_, _) => RefreshButtons();

        _cbFace.Content = MakeCheckText("我同意使用摄像头处理我的人脸信息（单独同意，可拒绝）");
        _cbFace.Foreground = Palette.TextPrimary;
        _cbFace.FontSize = 13;
        _cbFace.Margin = new Thickness(0, 2, 0, 2);
        _cbFace.IsCheckedChanged += (_, _) => RefreshButtons();

        var consentBox = new Border
        {
            Background = Palette.CardBg,
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 6),
            Child = new StackPanel { Children = { _cbPolicy, _cbFace } }
        };

        _hint.Foreground = Palette.TextMuted;
        _hint.FontSize = 12;
        _hint.TextWrapping = TextWrapping.Wrap;
        _hint.LineHeight = 18;
        _hint.Margin = new Thickness(0, 0, 0, 10);

        var accent = new SolidColorBrush(ThemeService.IsDark ? Color.Parse("#3B82F6") : Color.Parse("#2563EB"));

        _btnAccept.Content = "同意并继续";
        _btnAccept.MinWidth = 130;
        _btnAccept.Padding = new Thickness(14, 7);
        _btnAccept.Background = accent;
        _btnAccept.Foreground = Brushes.White;
        _btnAccept.BorderThickness = new Thickness(0);
        _btnAccept.CornerRadius = new CornerRadius(4);
        _btnAccept.HorizontalContentAlignment = HorizontalAlignment.Center;
        _btnAccept.Click += (_, _) =>
        {
            if (_cbPolicy.IsChecked != true) return;
            Accepted = true;
            FaceAgreed = _cbFace.IsChecked == true;
            _decided = true;
            Close();
        };

        var btnDecline = new Button
        {
            Content = required ? "不同意，退出程序" : "关闭",
            MinWidth = _required ? 150 : 90,
            Padding = new Thickness(14, 7),
            Background = Palette.ButtonBg,
            Foreground = Palette.TextPrimary,
            BorderBrush = Palette.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            IsCancel = true
        };
        btnDecline.Click += (_, _) =>
        {
            _decided = true;
            Close();
        };

        var btnRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        btnRow.Children.Add(btnDecline);
        btnRow.Children.Add(_btnAccept);

        var root = new StackPanel
        {
            Margin = new Thickness(20, 18, 20, 16),
            Spacing = 0
        };
        root.Children.Add(title);
        root.Children.Add(subtitle);
        root.Children.Add(scrollBorder);
        root.Children.Add(consentBox);
        root.Children.Add(_hint);
        root.Children.Add(btnRow);

        var linkFg = new SolidColorBrush(ThemeService.IsDark ? Color.Parse("#60A5FA") : Color.Parse("#2563EB"));
        var mkLink = new Func<string, string, TextBlock>((text, url) =>
        {
            var tb = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = linkFg,
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                TextAlignment = TextAlignment.Center,
                Cursor = new Cursor(StandardCursorType.Hand),
                Margin = new Thickness(0, 2, 0, 0)
            };
            tb.PointerPressed += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                catch { }
            };
            return tb;
        });

        var contact = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 10, 0, 0)
        };
        contact.Children.Add(mkLink("项目仓库（含完整《隐私政策》PRIVACY.md）：" + BuildConstants.GitHubRepoUrl, BuildConstants.GitHubRepoUrl));
        contact.Children.Add(mkLink("遇到问题或建议，可在 GitHub 提交 Issue：" + BuildConstants.GitHubIssuesUrl, BuildConstants.GitHubIssuesUrl));
        root.Children.Add(contact);

        Content = root;

        Closing += (_, e) =>
        {
            if (_decided) return;
            e.Cancel = !_required;
            if (_required)
            {
                Accepted = false;
                FaceAgreed = false;
            }
        };

        RefreshButtons();
    }

    private void RefreshButtons()
    {
        bool policy = _cbPolicy.IsChecked == true;
        bool face = _cbFace.IsChecked == true;
        _btnAccept.IsEnabled = policy;
        _hint.Text = !policy
            ? "请先勾选第一项「我已阅读并同意《隐私政策》」后，方可点击「同意并继续」。"
            : (face
                ? "人脸侦测功能可用：摄像头将在受保护程序处于前台时启用，仅在本机比对，不保存原始照片、不上传网络。"
                : "未勾选第二项：人脸侦测功能不可用，摄像头不会被调用；手动防窥、托盘、主题与设置等不涉及人脸的功能仍可正常使用。");
    }

    private static TextBlock MakeCheckText(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        FontSize = 13,
        Margin = new Thickness(4, 0, 0, 0)
    };

    private static StackPanel MakeSection(string title, params string[] lines)
    {
        var p = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 0, 10) };
        p.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = Palette.TextPrimary,
            Margin = new Thickness(0, 0, 0, 3)
        });
        foreach (var l in lines)
        {
            p.Children.Add(new TextBlock
            {
                Text = l,
                FontSize = 12.5,
                Foreground = Palette.TextSecondary,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 19,
                Margin = new Thickness(2, 0, 0, 0)
            });
        }
        return p;
    }
}
