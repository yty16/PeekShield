using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using PeekShield.Models;
using PeekShield.Views;

namespace PeekShield.Services;

public static class ConsentService
{
    public static bool NeedsConsent(PeekShieldSettings s)
        => !s.ConsentPrivacyPolicy || s.ConsentVersion < PeekShieldSettings.CurrentConsentVersion;

    public static bool CanProcessFace(PeekShieldSettings s)
        => s.ConsentPrivacyPolicy
           && s.ConsentFaceProcessing
           && s.ConsentVersion >= PeekShieldSettings.CurrentConsentVersion;

    public static async Task<bool> RunAsync(Window owner, PeekShieldSettings s, bool required)
    {
        var dlg = new PrivacyConsentDialog(required);
        await dlg.ShowDialog(owner);
        if (!dlg.Accepted)
        {
            LoggerService.LogInfo("隐私告知窗口关闭，未确认同意（首次强制=" + required + "）");
            return !required;
        }
        s.ConsentPrivacyPolicy = true;
        s.ConsentFaceProcessing = dlg.FaceAgreed;
        s.ConsentVersion = PeekShieldSettings.CurrentConsentVersion;
        s.ConsentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        s.Save();
        LoggerService.LogInfo("隐私告知已确认：政策=已同意 人脸处理单独同意=" + (dlg.FaceAgreed ? "是" : "否"));
        PeekShieldEngine.Instance.ApplySettings();
        return true;
    }

    public static void Revoke(PeekShieldSettings s)
    {
        s.ConsentPrivacyPolicy = false;
        s.ConsentFaceProcessing = false;
        s.ConsentVersion = 0;
        s.ConsentTime = "";
        s.Save();
        LoggerService.LogInfo("用户已撤回同意，人脸侦测停用，下次启动将重新征求同意");
    }
}
