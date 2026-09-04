using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PeekShield.Models;
using PeekShield.Services;

namespace PeekShield;

public partial class MainWindow : Window
{
    public static MainWindow? Instance;
    public static void ShowSettings() => Instance?.Show();

    public MainWindow()
    {
        InitializeComponent();
        Instance = this;

        var eng = PeekShieldEngine.Instance;
        var s = eng.Settings;

        var camStatus = this.FindControl<TextBlock>("CamStatusText");
        if (camStatus != null)
        {
            var cams = CameraService.Enumerate();
            if (cams.Count == 0)
                camStatus.Text = "没找到摄像头（这台机器可能没摄像头或被禁用）";
            else
                camStatus.Text = "发现 " + cams.Count + " 个摄像头： " +
                                  string.Join("、 ", cams.ConvertAll(c => "#" + c.index + " " + c.name));
        }

        var enrollStatus = this.FindControl<TextBlock>("EnrollStatusText");
        if (enrollStatus != null)
        {
            enrollStatus.Text = eng.IsFaceReady
                ? "dlib 已就绪，" + (eng.IsEnrolled ? "已录入 " + eng.EnrolledCount + " 条" : "尚未录入人脸")
                : "dlib 模型未就绪（在 Models/ 放两个 .dat 文件后重启）";
        }

        var smartCheck = this.FindControl<CheckBox>("SmartPeekCheck");
        if (smartCheck != null) smartCheck.IsChecked = s.EnableSmartPeek;
        var blurCheck = this.FindControl<CheckBox>("ActionBlurCheck");
        if (blurCheck != null) blurCheck.IsChecked = s.ActionBlur;
        var popupCheck = this.FindControl<CheckBox>("ActionPopupCheck");
        if (popupCheck != null) popupCheck.IsChecked = s.ActionPopup;
        var soundCheck = this.FindControl<CheckBox>("ActionSoundCheck");
        if (soundCheck != null) soundCheck.IsChecked = s.ActionSound;
        var minCheck = this.FindControl<CheckBox>("ActionMinimizeCheck");
        if (minCheck != null) minCheck.IsChecked = s.ActionMinimize;
    }

    private void OnEnrollClicked(object? sender, RoutedEventArgs e)
    {
        var eng = PeekShieldEngine.Instance;
        var cams = CameraService.Enumerate();
        int idx = cams.Count > 0 ? cams[0].index : 0;
        bool ok = eng.EnrollFromCamera(idx);
        var status = this.FindControl<TextBlock>("EnrollStatusText");
        if (status != null)
            status.Text = ok
                ? "录入成功！共 " + eng.EnrolledCount + " 条。下次启动会自动加载。"
                : "录入失败（画面里没检测到人脸？换个角度或者换个亮一点的环境试试）";
    }
}
