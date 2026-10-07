using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PeekShield.Models;

namespace PeekShield.Services;

public enum DiagnosticStatus { Ok, Warn, Error }

public class DiagnosticItem
{
    public string Name { get; set; } = "";
    public DiagnosticStatus Status { get; set; } = DiagnosticStatus.Ok;
    public string Detail { get; set; } = "";
}

public static class DiagnosticService
{
    private static string FactorCategory(AuthMethodKind k) => k switch
    {
        AuthMethodKind.Password => "knowledge",
        AuthMethodKind.Face => "biometric",
        AuthMethodKind.QuickFace => "biometric",
        AuthMethodKind.System => "platform",
        AuthMethodKind.Usb => "possession",
        _ => "other"
    };

    public static List<DiagnosticItem> Run(PeekShieldSettings settings, PeekShieldEngine? engine)
    {
        var items = new List<DiagnosticItem>();
        items.Add(CheckRuntime());
        items.Add(CheckModels());
        items.Add(CheckCamera(engine));
        items.Add(CheckFaceData(settings, engine));
        items.Add(CheckAutostart(settings));
        items.Add(CheckSettingsConsistency(settings));
        items.Add(CheckDataDir());
        items.Add(CheckProfile());
        return items;
    }

    private static DiagnosticItem CheckRuntime()
    {
        var detail = $"{Platform.OsLabel} · .NET {Environment.Version.Major}.{Environment.Version.Minor}";
        return new DiagnosticItem { Name = "运行环境", Status = DiagnosticStatus.Ok, Detail = detail };
    }

    private static DiagnosticItem CheckModels()
    {
        var dir = Platform.ModelsDir;
        var sp = Path.Combine(dir, "shape_predictor_68_face_landmarks.dat");
        var net = Path.Combine(dir, "dlib_face_recognition_resnet_model_v1.dat");
        var missing = new List<string>();
        if (!File.Exists(sp)) missing.Add("shape_predictor_68_face_landmarks.dat");
        if (!File.Exists(net)) missing.Add("dlib_face_recognition_resnet_model_v1.dat");
        if (missing.Count == 0)
            return new DiagnosticItem { Name = "人脸模型文件", Status = DiagnosticStatus.Ok, Detail = $"模型完整（{dir}）" };
        return new DiagnosticItem { Name = "人脸模型文件", Status = DiagnosticStatus.Error, Detail = "缺少模型文件：" + string.Join("、", missing) + "，人脸检测/识别将无法工作。" };
    }

    private static DiagnosticItem CheckCamera(PeekShieldEngine? engine)
    {
        var usingStates = new[]
        {
            EngineStatus.Monitoring, EngineStatus.Secure, EngineStatus.Peek,
            EngineStatus.Manual, EngineStatus.Paused
        };
        if (engine != null)
        {
            var st = engine.Status;
            if (st == EngineStatus.NoCamera)
                return new DiagnosticItem { Name = "摄像头", Status = DiagnosticStatus.Error, Detail = "未检测到可用摄像头或摄像头被占用，防偷窥将无法运行。" };
            if (usingStates.Contains(st))
                return new DiagnosticItem { Name = "摄像头", Status = DiagnosticStatus.Ok, Detail = "摄像头工作正常（引擎正在使用）。" };
        }
        try
        {
            var cams = CameraService.Enumerate();
            if (cams.Count > 0)
                return new DiagnosticItem { Name = "摄像头", Status = DiagnosticStatus.Ok, Detail = $"检测到 {cams.Count} 个摄像头设备。" };
            return new DiagnosticItem { Name = "摄像头", Status = DiagnosticStatus.Warn, Detail = "未检测到摄像头设备；若尚未录入人脸可暂时忽略。" };
        }
        catch (Exception ex)
        {
            return new DiagnosticItem { Name = "摄像头", Status = DiagnosticStatus.Warn, Detail = "摄像头检测异常：" + ex.Message };
        }
    }

    private static DiagnosticItem CheckFaceData(PeekShieldSettings settings, PeekShieldEngine? engine)
    {
        var problems = new List<string>();
        if (settings.IsEnrolled)
        {
            var owner = Path.Combine(Platform.EnrollDir, "embeddings.bin");
            if (!File.Exists(owner))
                problems.Add("机主人脸数据文件缺失（enrollment/embeddings.bin）");
        }
        foreach (var m in settings.AuthMethods.Where(m => m.Kind == AuthMethodKind.Face))
        {
            var f = Path.Combine(Platform.EnrollDir, "faces", m.Id, "embeddings.bin");
            if (!File.Exists(f))
                problems.Add($"人脸认证条目「{m.Name}」的人脸数据缺失");
        }
        if (settings.WhitelistEnabled && settings.Whitelist.Count == 0)
            problems.Add("已开启白名单但未添加任何成员");
        if (problems.Count == 0)
            return new DiagnosticItem { Name = "人脸数据完整性", Status = DiagnosticStatus.Ok, Detail = "已录入的人脸数据文件均完整。" };
        return new DiagnosticItem { Name = "人脸数据完整性", Status = DiagnosticStatus.Warn, Detail = string.Join("；", problems) };
    }

    private static DiagnosticItem CheckAutostart(PeekShieldSettings settings)
    {
        if (!settings.AutoStart)
            return new DiagnosticItem { Name = "开机自启", Status = DiagnosticStatus.Ok, Detail = "未开启开机自启，跳过。" };
        bool enabled = AutoStartService.IsEnabled();
        if (enabled)
            return new DiagnosticItem { Name = "开机自启", Status = DiagnosticStatus.Ok, Detail = "已在系统中注册开机自启。" };
        return new DiagnosticItem { Name = "开机自启", Status = DiagnosticStatus.Warn, Detail = "已开启开机自启但系统未注册，重启后可能不会自动启动；请尝试关闭再重新打开该开关。" };
    }

    private static DiagnosticItem CheckSettingsConsistency(PeekShieldSettings settings)
    {
        var errors = new List<string>();
        var warns = new List<string>();
        var ops = new (string id, string label)[]
        {
            ("Exit", "退出"), ("Uninstall", "卸载"), ("OpenMain", "打开主页面"), ("OpenSecurity", "打开安全设置")
        };
        foreach (var op in ops)
        {
            bool protect = op.id switch
            {
                "Exit" => settings.ProtectExit,
                "Uninstall" => settings.ProtectUninstall,
                "OpenMain" => settings.ProtectOpenMain,
                "OpenSecurity" => settings.ProtectOpenSecurity,
                _ => false
            };
            if (!protect) continue;
            var methods = settings.GetAuthMethodsForOperation(op.id);
            if (methods.Count == 0)
            {
                errors.Add($"受保护操作「{op.label}」未配置任何认证方式，可能被锁死无法验证");
                continue;
            }
            if (settings.TwoFactorEnabled)
            {
                int cats = methods.Select(m => FactorCategory(m.Kind)).Distinct().Count();
                if (cats < 2)
                    warns.Add($"双因子已开启，但「{op.label}」仅 {cats} 种类别认证方式，将退化为单因子");
            }
        }
        var pwd = settings.AuthMethods.FirstOrDefault(m => m.Kind == AuthMethodKind.Password);
        if (pwd != null)
        {
            var opts = pwd.GetPasswordOptions();
            if (string.IsNullOrEmpty(opts.PasswordHash))
                errors.Add("密码认证条目缺少密码哈希，验证将失败");
        }
        bool hasFaceMethod = settings.AuthMethods.Any(m => m.Kind == AuthMethodKind.Face || m.Kind == AuthMethodKind.QuickFace);
        if (hasFaceMethod && !settings.IsEnrolled)
            warns.Add("已配置人脸解锁但机主尚未录入人脸，刷脸将无法使用");
        if (errors.Count > 0)
            return new DiagnosticItem { Name = "设置有效性", Status = DiagnosticStatus.Error, Detail = string.Join("；", errors) };
        if (warns.Count > 0)
            return new DiagnosticItem { Name = "设置有效性", Status = DiagnosticStatus.Warn, Detail = string.Join("；", warns) };
        return new DiagnosticItem { Name = "设置有效性", Status = DiagnosticStatus.Ok, Detail = "认证方式配置一致，无冲突。" };
    }

    private static DiagnosticItem CheckDataDir()
    {
        try
        {
            Directory.CreateDirectory(Platform.AppDataDir);
            var tmp = Path.Combine(Platform.AppDataDir, ".diag_writable_test");
            File.WriteAllText(tmp, "1");
            File.Delete(tmp);
            return new DiagnosticItem { Name = "数据目录", Status = DiagnosticStatus.Ok, Detail = $"数据目录可写（{Platform.AppDataDir}）" };
        }
        catch (Exception ex)
        {
            return new DiagnosticItem { Name = "数据目录", Status = DiagnosticStatus.Error, Detail = "数据目录不可写：" + ex.Message };
        }
    }

    private static DiagnosticItem CheckProfile()
    {
        var name = string.IsNullOrWhiteSpace(Platform.ProfileName) ? "默认配置" : Platform.ProfileName;
        int count = Platform.ListProfiles().Count;
        return new DiagnosticItem { Name = "当前配置文件", Status = DiagnosticStatus.Ok, Detail = $"当前：{name}（共 {count + 1} 套配置）" };
    }
}
