using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using PeekShield.Services;

namespace PeekShield.Models;

public enum CrashBehavior
{
    SilentRestart = 0,
    PromptRestart = 1,
    ExitApp = 2
}

public class PeekShieldSettings
{
    private static readonly string _buildToken = "eXR5MTY=";
    internal static string BuildSignature => _buildToken;

    public const string DefaultPeekAlertText = "【窥屿盾】已检测到他人注视屏幕,请注意隐私";

    public const int DefaultPopupWidth = 480;
    public const int DefaultPopupHeight = 150;
    public const int DefaultPopupFontSize = 22;
    public const string DefaultPopupPosition = "center";
    public const int DefaultPopupX = 240;
    public const int DefaultPopupY = 240;

    public const int CurrentConsentVersion = 1;
    public const int DefaultAutoCleanupDays = 30;

    public bool ConsentPrivacyPolicy { get; set; } = false;
    public bool ConsentFaceProcessing { get; set; } = false;
    public int ConsentVersion { get; set; } = 0;
    public string ConsentTime { get; set; } = "";
    public int AutoCleanupDays { get; set; } = DefaultAutoCleanupDays;

    public bool EnableSmartPeek { get; set; } = true;

    public bool AutoStart { get; set; } = true;

    public bool AutoCheckUpdate { get; set; } = true;
    public bool AutoSilentUpdate { get; set; } = false;

    public int CameraIndex { get; set; } = 0;

    public string CameraName { get; set; } = "";

    public int Sensitivity { get; set; } = 1;

    public bool EnableTopBanner { get; set; } = true;
    public bool EnableFullscreenProtect { get; set; } = true;

    public bool ActionPopup { get; set; } = true;
    public bool ActionBlur { get; set; } = true;
    public bool ActionSound { get; set; } = true;
    public bool ActionMinimize { get; set; } = false;

    [JsonConverter(typeof(ProtectedEntryListConverter))]
    public List<ProtectedEntry> ProtectedProcesses { get; set; } = new()
    {
        new ProtectedEntry { Name = "WeChat.exe", Enabled = true },
        new ProtectedEntry { Name = "Weixin.exe", Enabled = true },
        new ProtectedEntry { Name = "qq.exe", Enabled = true },
        new ProtectedEntry { Name = "TIM.exe", Enabled = true },
        new ProtectedEntry { Name = "chrome.exe", Enabled = true },
        new ProtectedEntry { Name = "msedge.exe", Enabled = true },
        new ProtectedEntry { Name = "brave.exe", Enabled = true },
        new ProtectedEntry { Name = "firefox.exe", Enabled = true },
        new ProtectedEntry { Name = "AliWorkbench.exe", Enabled = true },
        new ProtectedEntry { Name = "DingTalk.exe", Enabled = true },
        new ProtectedEntry { Name = "WXWork.exe", Enabled = true }
    };

    [JsonConverter(typeof(ProtectedEntryListConverter))]
    public List<ProtectedEntry> ProtectedWindowTitles { get; set; } = new()
    {
        new ProtectedEntry { Name = "桌面", Enabled = true }
    };

    public bool WhitelistEnabled { get; set; } = false;

    public List<WhitelistEntry> Whitelist { get; set; } = new();

    public bool OnlyProtectForeground { get; set; } = true;

    public bool LowLightEnhance { get; set; } = false;

    public bool MirrorPosterFilter { get; set; } = true;

    public bool Paused { get; set; } = false;
    public bool ManualMode { get; set; } = false;

    public bool ShowTrayIcon { get; set; } = true;

    public ThemeMode ThemeMode { get; set; } = ThemeMode.System;

    public ThemeSkin Skin { get; set; } = ThemeSkin.Blue;

    public bool EnableHotkey { get; set; } = true;
    public string HotkeyModifiers { get; set; } = "Ctrl+Shift";
    public string HotkeyKey { get; set; } = "P";
    public bool ScreenshotOnPeek { get; set; } = false;

    public int StrangerAlertLimit { get; set; } = 2;
    public int StrangerAlertCooldownMinutes { get; set; } = 10;

    public int PopupWidth { get; set; } = DefaultPopupWidth;
    public int PopupHeight { get; set; } = DefaultPopupHeight;
    public int PopupFontSize { get; set; } = DefaultPopupFontSize;
    public string PopupPosition { get; set; } = DefaultPopupPosition;
    public int PopupX { get; set; } = DefaultPopupX;
    public int PopupY { get; set; } = DefaultPopupY;

    public string PeekAlertText { get; set; } = DefaultPeekAlertText;

    public bool RestoreOnSafe { get; set; } = false;

    public bool IsEnrolled { get; set; } = false;

    public bool PasswordEnabled { get; set; } = false;
    public string PasswordHash { get; set; } = "";
    public string SecurityQuestion { get; set; } = "";
    public string SecurityAnswerHash { get; set; } = "";
    public bool ProtectExit { get; set; } = true;
    public bool ProtectUninstall { get; set; } = true;
    public bool ProtectOpenMain { get; set; } = true;
    public bool ProtectOpenSecurity { get; set; } = true;
    public int SecuritySessionMinutes { get; set; } = 15;
    public int PasswordFailedAttempts { get; set; } = 0;
    public DateTime PasswordLockoutUntil { get; set; } = DateTime.MinValue;
    public int PasswordLockoutLevel { get; set; } = 0;

    public bool FaceUnlockEnabled { get; set; } = false;

    public bool SystemUnlockEnabled { get; set; } = false;

    public bool UsbUnlockEnabled { get; set; } = false;
    public string UsbUnlockTokenHash { get; set; } = "";
    public string UsbUnlockDriveLabel { get; set; } = "";

    public bool QuickVerifyEnabled { get; set; } = false;

    public bool ProcessGuardEnabled { get; set; } = false;

    public List<AuthMethodEntry> AuthMethods { get; set; } = new();

    public List<string> ExitAuthIds { get; set; } = new();
    public List<string> UninstallAuthIds { get; set; } = new();
    public List<string> OpenMainAuthIds { get; set; } = new();
    public List<string> OpenSecurityAuthIds { get; set; } = new();

    public CrashBehavior CrashBehaviorOnCrash { get; set; } = CrashBehavior.SilentRestart;

    public List<TrayMenuItemConfig> TrayMenuItems { get; set; } = new();

    public int SettingsVersion { get; set; } = 0;

    private static string SettingsPath => Path.Combine(Platform.AppDataDir, BuildConstants.SettingsFileName);
    public static string SettingsFilePath => SettingsPath;

    public static PeekShieldSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var s = JsonSerializer.Deserialize<PeekShieldSettings>(json);
                if (s != null)
                {
                    s.Migrate();
                    if (s.Sanitize()) s.Save();
                    return s;
                }
                BackupCorruptSettings();
            }
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PeekShield] Settings load error: {ex.Message}");
            BackupCorruptSettings();
        }
        return new PeekShieldSettings();
    }

    private static void BackupCorruptSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var bak = SettingsPath + ".bak";
                if (File.Exists(bak)) File.Delete(bak);
                File.Move(SettingsPath, bak);
            }
        }
        catch { }
    }

    private void Migrate()
    {
        bool changed = false;
        if (SettingsVersion < 1)
        {
            SettingsVersion = 1;
            changed = true;
        }
        if (SettingsVersion < 2)
        {
            SettingsVersion = 2;
            if (PasswordEnabled && string.IsNullOrEmpty(PasswordHash))
                PasswordEnabled = false;
            changed = true;
        }
        if (SettingsVersion < 3)
        {
            SettingsVersion = 3;
            PasswordFailedAttempts = 0;
            PasswordLockoutUntil = DateTime.MinValue;
            PasswordLockoutLevel = 0;
            changed = true;
        }
        if (SettingsVersion < 4)
        {
            SettingsVersion = 4;
            if (FaceUnlockEnabled && !PasswordEnabled) FaceUnlockEnabled = false;
            changed = true;
        }
        if (SettingsVersion < 5)
        {
            SettingsVersion = 5;
            if (QuickVerifyEnabled && (!PasswordEnabled || !IsEnrolled)) QuickVerifyEnabled = false;
            changed = true;
        }
        if (SettingsVersion < 6)
        {
            SettingsVersion = 6;
            if (ProcessGuardEnabled && !PasswordEnabled) ProcessGuardEnabled = false;
            changed = true;
        }
        if (SettingsVersion < 7)
        {
            SettingsVersion = 7;
            changed = true;
        }
        if (SettingsVersion < 8)
        {
            SettingsVersion = 8;
            if (SystemUnlockEnabled && !PasswordEnabled) SystemUnlockEnabled = false;
            changed = true;
        }
        if (SettingsVersion < 9)
        {
            SettingsVersion = 9;
            if (UsbUnlockEnabled && !PasswordEnabled) { UsbUnlockEnabled = false; UsbUnlockTokenHash = ""; UsbUnlockDriveLabel = ""; }
            changed = true;
        }
        if (SettingsVersion < 10)
        {
            SettingsVersion = 10;
            var defaultOps = new List<string> { "Exit", "Uninstall", "OpenMain", "OpenSecurity" };
            var enabledOps = new List<string>();
            if (ProtectExit) enabledOps.Add("Exit");
            if (ProtectUninstall) enabledOps.Add("Uninstall");
            if (ProtectOpenMain) enabledOps.Add("OpenMain");
            if (ProtectOpenSecurity) enabledOps.Add("OpenSecurity");
            if (enabledOps.Count == 0) enabledOps = new List<string>(defaultOps);

            var allIds = new List<string>();
            var passwordId = "";
            if (PasswordEnabled)
            {
                passwordId = Guid.NewGuid().ToString("N");
                var pwd = new AuthMethodEntry { Id = passwordId, Kind = AuthMethodKind.Password, Operations = new List<string>(enabledOps) };
                allIds.Add(passwordId);
                AuthMethods.Add(pwd);
            }
            if (FaceUnlockEnabled && PasswordEnabled)
            {
                var id = Guid.NewGuid().ToString("N");
                var face = new AuthMethodEntry { Id = id, Kind = AuthMethodKind.Face, Operations = new List<string>(enabledOps) };
                allIds.Add(id);
                AuthMethods.Add(face);
            }
            if (SystemUnlockEnabled && PasswordEnabled && OperatingSystem.IsWindows())
            {
                var id = Guid.NewGuid().ToString("N");
                var sys = new AuthMethodEntry { Id = id, Kind = AuthMethodKind.System, Operations = new List<string>(enabledOps) };
                allIds.Add(id);
                AuthMethods.Add(sys);
            }
            if (UsbUnlockEnabled && PasswordEnabled)
            {
                var id = Guid.NewGuid().ToString("N");
                var usb = new AuthMethodEntry { Id = id, Kind = AuthMethodKind.Usb, Operations = new List<string>(enabledOps) };
                var opts = new AuthUsbOptions { UseFileMode = true, TokenHash = UsbUnlockTokenHash, DriveLabel = UsbUnlockDriveLabel };
                usb.SetUsbOptions(opts);
                allIds.Add(id);
                AuthMethods.Add(usb);
            }
            if (ProtectExit) ExitAuthIds = new List<string>(allIds);
            if (ProtectUninstall) UninstallAuthIds = new List<string>(allIds);
            if (ProtectOpenMain) OpenMainAuthIds = new List<string>(allIds);
            if (ProtectOpenSecurity) OpenSecurityAuthIds = new List<string>(allIds);
            changed = true;
        }
        if (SettingsVersion < 11)
        {
            SettingsVersion = 11;
            var defaultOps = new List<string> { "Exit", "Uninstall", "OpenMain", "OpenSecurity" };
            // 修复 v10 误把 id 写入 Operations/AuthIds 的问题
            foreach (var m in AuthMethods)
            {
                if (m.Operations.Count > 0 && !m.Operations.All(defaultOps.Contains))
                    m.Operations = new List<string>(defaultOps);
            }
            if (ExitAuthIds.Count > 0 && !ExitAuthIds.All(x => AuthMethods.Exists(m => m.Id == x)))
            {
                var ids = AuthMethods.Where(m => m.Operations.Contains("Exit")).Select(m => m.Id).ToList();
                ExitAuthIds = ids.Count > 0 ? ids : new List<string>(AuthMethods.Select(m => m.Id));
            }
            if (UninstallAuthIds.Count > 0 && !UninstallAuthIds.All(x => AuthMethods.Exists(m => m.Id == x)))
            {
                var ids = AuthMethods.Where(m => m.Operations.Contains("Uninstall")).Select(m => m.Id).ToList();
                UninstallAuthIds = ids.Count > 0 ? ids : new List<string>(AuthMethods.Select(m => m.Id));
            }
            if (OpenMainAuthIds.Count > 0 && !OpenMainAuthIds.All(x => AuthMethods.Exists(m => m.Id == x)))
            {
                var ids = AuthMethods.Where(m => m.Operations.Contains("OpenMain")).Select(m => m.Id).ToList();
                OpenMainAuthIds = ids.Count > 0 ? ids : new List<string>(AuthMethods.Select(m => m.Id));
            }
            if (OpenSecurityAuthIds.Count > 0 && !OpenSecurityAuthIds.All(x => AuthMethods.Exists(m => m.Id == x)))
            {
                var ids = AuthMethods.Where(m => m.Operations.Contains("OpenSecurity")).Select(m => m.Id).ToList();
                OpenSecurityAuthIds = ids.Count > 0 ? ids : new List<string>(AuthMethods.Select(m => m.Id));
            }
            // 把全局密码迁移到第一个 Password 条目中
            if (PasswordEnabled)
            {
                var pwd = AuthMethods.Find(m => m.Kind == AuthMethodKind.Password);
                if (pwd == null)
                {
                    pwd = new AuthMethodEntry { Id = Guid.NewGuid().ToString("N"), Kind = AuthMethodKind.Password, Operations = new List<string>(defaultOps) };
                    AuthMethods.Insert(0, pwd);
                }
                var opts = pwd.GetPasswordOptions();
                opts.PasswordHash = PasswordHash;
                opts.SecurityQuestion = SecurityQuestion;
                opts.SecurityAnswerHash = SecurityAnswerHash;
                pwd.SetPasswordOptions(opts);
            }
            changed = true;
        }
        if (SettingsVersion < 12)
        {
            SettingsVersion = 12;
            var defaultOps = new List<string> { "Exit", "Uninstall", "OpenMain", "OpenSecurity" };
            // 把旧「快捷验证」全局开关转换为 QuickFace 认证条目
            if (QuickVerifyEnabled && PasswordEnabled && !AuthMethods.Exists(m => m.Kind == AuthMethodKind.QuickFace))
            {
                var ops = new List<string>();
                if (ProtectExit) ops.Add("Exit");
                if (ProtectUninstall) ops.Add("Uninstall");
                if (ProtectOpenMain) ops.Add("OpenMain");
                if (ProtectOpenSecurity) ops.Add("OpenSecurity");
                if (ops.Count == 0) ops = new List<string>(defaultOps);
                AuthMethods.Add(new AuthMethodEntry { Kind = AuthMethodKind.QuickFace, Operations = ops });
            }
            // 确保旧 FaceUnlock 状态也覆盖 QuickFace（之前被遗漏）
            FaceUnlockEnabled = AuthMethods.Exists(m => m.Kind == AuthMethodKind.Face || m.Kind == AuthMethodKind.QuickFace);
            changed = true;
        }
        if (SettingsVersion < 13)
        {
            SettingsVersion = 13;
            // 把旧全局 unlock 人脸数据迁移到第一个 Face/QuickFace 认证条目
            var oldUnlockFile = Path.Combine(Platform.EnrollDir, "unlock", "embeddings.bin");
            if (File.Exists(oldUnlockFile))
            {
                var target = AuthMethods.FirstOrDefault(m => m.Kind == AuthMethodKind.Face || m.Kind == AuthMethodKind.QuickFace);
                if (target != null)
                {
                    var dir = Path.Combine(Platform.EnrollDir, "faces", target.Id);
                    Directory.CreateDirectory(dir);
                    var dest = Path.Combine(dir, "embeddings.bin");
                    if (File.Exists(dest)) File.Delete(dest);
                    File.Move(oldUnlockFile, dest);
                }
                try
                {
                    var oldUnlockDir = Path.Combine(Platform.EnrollDir, "unlock");
                    if (Directory.Exists(oldUnlockDir)) Directory.Delete(oldUnlockDir);
                }
                catch { }
            }
            changed = true;
        }
        if (SettingsVersion < 14)
        {
            SettingsVersion = 14;
            if (TrayMenuItems == null || TrayMenuItems.Count == 0)
            {
                TrayMenuItems = new List<TrayMenuItemConfig>
                {
                    new() { Id = "open", Visible = true },
                    new() { Id = "privacy", Visible = true },
                    new() { Id = "security", Visible = true },
                    new() { Id = "pause", Visible = true },
                    new() { Id = "manual", Visible = true },
                    new() { Id = "hide", Visible = true },
                    new() { Id = "exit", Visible = true }
                };
            }
            changed = true;
        }
        if (SettingsVersion < 15)
        {
            SettingsVersion = 15;
            if (!Enum.IsDefined(typeof(ThemeSkin), Skin)) Skin = ThemeSkin.Blue;
            changed = true;
        }
        if (changed) Save();
    }

    public bool AnyAuthMethodForOperation(string operation) => operation switch
    {
        "Exit" => ExitAuthIds.Count > 0,
        "Uninstall" => UninstallAuthIds.Count > 0,
        "OpenMain" => OpenMainAuthIds.Count > 0,
        "OpenSecurity" => OpenSecurityAuthIds.Count > 0,
        _ => false
    };

    public List<AuthMethodEntry> GetAuthMethodsForOperation(string operation)
    {
        var result = new List<AuthMethodEntry>();
        foreach (var m in AuthMethods)
        {
            if (m.Operations.Contains(operation)) result.Add(m);
        }
        return result;
    }

    public void SyncLegacyAuthBooleans()
    {
        FaceUnlockEnabled = AuthMethods.Exists(m => m.Kind == AuthMethodKind.Face || m.Kind == AuthMethodKind.QuickFace);
        QuickVerifyEnabled = AuthMethods.Exists(m => m.Kind == AuthMethodKind.QuickFace);
        SystemUnlockEnabled = AuthMethods.Exists(m => m.Kind == AuthMethodKind.System);
        UsbUnlockEnabled = AuthMethods.Exists(m => m.Kind == AuthMethodKind.Usb);
        var usb = AuthMethods.Find(m => m.Kind == AuthMethodKind.Usb);
        if (usb != null)
        {
            var opts = usb.GetUsbOptions();
            UsbUnlockTokenHash = opts.UseFileMode ? opts.TokenHash : "";
            UsbUnlockDriveLabel = opts.DriveLabel;
        }
        else
        {
            UsbUnlockTokenHash = "";
            UsbUnlockDriveLabel = "";
        }
        PasswordEnabled = AuthMethods.Exists(m => m.Kind == AuthMethodKind.Password);
        var pwd = AuthMethods.Find(m => m.Kind == AuthMethodKind.Password);
        if (pwd != null)
        {
            var opts = pwd.GetPasswordOptions();
            PasswordHash = opts.PasswordHash;
            SecurityQuestion = opts.SecurityQuestion;
            SecurityAnswerHash = opts.SecurityAnswerHash;
        }
        else
        {
            PasswordHash = "";
            SecurityQuestion = "";
            SecurityAnswerHash = "";
        }
        ProtectExit = AuthMethods.Any(m => m.Operations.Contains("Exit"));
        ProtectUninstall = AuthMethods.Any(m => m.Operations.Contains("Uninstall"));
        ProtectOpenMain = AuthMethods.Any(m => m.Operations.Contains("OpenMain"));
        ProtectOpenSecurity = AuthMethods.Any(m => m.Operations.Contains("OpenSecurity"));
        // 兼容性：operation id 列表也同步为启用了对应操作的条目 id
        ExitAuthIds = AuthMethods.Where(m => m.Operations.Contains("Exit")).Select(m => m.Id).ToList();
        UninstallAuthIds = AuthMethods.Where(m => m.Operations.Contains("Uninstall")).Select(m => m.Id).ToList();
        OpenMainAuthIds = AuthMethods.Where(m => m.Operations.Contains("OpenMain")).Select(m => m.Id).ToList();
        OpenSecurityAuthIds = AuthMethods.Where(m => m.Operations.Contains("OpenSecurity")).Select(m => m.Id).ToList();
    }

    private bool Sanitize()
    {
        bool changed = false;
        changed |= Dedupe(ProtectedProcesses);
        changed |= Dedupe(ProtectedWindowTitles);
        return changed;
    }

    private static bool Dedupe(List<ProtectedEntry> list)
    {
        if (list == null) return false;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cleaned = new List<ProtectedEntry>();
        foreach (var e in list)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.Name)) { continue; }
            if (seen.Add(e.Name)) cleaned.Add(e);
        }
        if (cleaned.Count != list.Count)
        {
            list.Clear();
            list.AddRange(cleaned);
            return true;
        }
        return false;
    }

    public void Save()
    {
        try
        {
            SyncLegacyAuthBooleans();
            Directory.CreateDirectory(Platform.AppDataDir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PeekShield] Settings save error: {ex.Message}");
        }
    }

    public void ClearPasswordProtection()
    {
        PasswordEnabled = false;
        PasswordHash = "";
        SecurityQuestion = "";
        SecurityAnswerHash = "";
        ProtectExit = false;
        ProtectUninstall = false;
        ProtectOpenMain = false;
        ProtectOpenSecurity = false;
        PasswordFailedAttempts = 0;
        PasswordLockoutUntil = DateTime.MinValue;
        PasswordLockoutLevel = 0;
        FaceUnlockEnabled = false;
        SystemUnlockEnabled = false;
        UsbUnlockEnabled = false;
        UsbUnlockTokenHash = "";
        UsbUnlockDriveLabel = "";
        QuickVerifyEnabled = false;
        ProcessGuardEnabled = false;
        AuthMethods = new List<AuthMethodEntry>();
        ExitAuthIds = new List<string>();
        UninstallAuthIds = new List<string>();
        OpenMainAuthIds = new List<string>();
        OpenSecurityAuthIds = new List<string>();
        RemoveUnlockData();
        RemoveFaceAuthData();
        Save();
    }

    private static void RemoveUnlockData()
    {
        try
        {
            var dir = Path.Combine(Platform.EnrollDir, "unlock");
            if (Directory.Exists(dir))
            {
                foreach (var f in Directory.GetFiles(dir)) File.Delete(f);
                Directory.Delete(dir);
            }
        }
        catch { }
    }

    private static void RemoveFaceAuthData()
    {
        try
        {
            var dir = Path.Combine(Platform.EnrollDir, "faces");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch { }
    }
}

public class ProtectedEntry
{
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public class WhitelistEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public class ProtectedEntryListConverter : JsonConverter<List<ProtectedEntry>>
{
    public override List<ProtectedEntry> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var list = new List<ProtectedEntry>();
        if (reader.TokenType != JsonTokenType.StartArray) return list;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) break;
            if (reader.TokenType == JsonTokenType.String)
            {
                list.Add(new ProtectedEntry { Name = reader.GetString() ?? "", Enabled = true });
            }
            else if (reader.TokenType == JsonTokenType.StartObject)
            {
                string name = "";
                bool enabled = true;
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject) break;
                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        var prop = reader.GetString();
                        reader.Read();
                        if (prop == "Name") name = reader.GetString() ?? "";
                        else if (prop == "Enabled") enabled = reader.GetBoolean();
                    }
                }
                list.Add(new ProtectedEntry { Name = name, Enabled = enabled });
            }
        }
        return list;
    }

    public override void Write(Utf8JsonWriter writer, List<ProtectedEntry> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var e in value)
        {
            writer.WriteStartObject();
            writer.WriteString("Name", e.Name);
            writer.WriteBoolean("Enabled", e.Enabled);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
}

public class TrayMenuItemConfig
{
    public string Id { get; set; } = "";
    public bool Visible { get; set; } = true;
}
