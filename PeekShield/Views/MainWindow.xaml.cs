using Avalonia.Controls;
using PeekShield.Services;

namespace PeekShield;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var status = this.FindControl<TextBlock>("CamStatusText");
        if (status != null)
        {
            var cams = CameraService.Enumerate();
            if (cams.Count == 0)
                status.Text = "没找到摄像头（连个插着的都没有？这台机器可能没摄像头或被禁用）";
            else
                status.Text = "发现 " + cams.Count + " 个摄像头： " +
                              string.Join("、 ", cams.ConvertAll(c => "#" + c.index + " " + c.name));
        }
    }
}
