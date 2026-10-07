using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace PeekShield;

internal static class Platform
{
    public static bool IsWindows => OperatingSystem.IsWindows();
    public static bool IsMacOS => OperatingSystem.IsMacOS();
    public static bool IsLinux => OperatingSystem.IsLinux();

    public static string AppBaseDir =>
        Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location)
        ?? AppContext.BaseDirectory;

    // 批次3：多配置文件。空字符串表示默认配置（数据目录与旧版一致）。
    public static string ProfileName { get; set; } = "";

    public static string ProfilesRoot
    {
        get
        {
            string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(baseDir))
                baseDir = AppContext.BaseDirectory;
            return Path.Combine(baseDir, "PeekShield");
        }
    }

    public static string AppDataDir
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ProfileName))
                return ProfilesRoot;
            return Path.Combine(ProfilesRoot, "profiles", SanitizeProfileName(ProfileName));
        }
    }

    public static string SettingsDir => AppDataDir;
    public static string EnrollDir => Path.Combine(AppDataDir, BuildConstants.EnrollDirName);
    public static string LogsDir => Path.Combine(AppDataDir, BuildConstants.LogsDirName);

    public static string ModelsDir
    {
        get
        {
            var nextToExe = Path.Combine(AppBaseDir, BuildConstants.ModelsDirName);
            if (Directory.Exists(nextToExe) &&
                File.Exists(Path.Combine(nextToExe, "shape_predictor_68_face_landmarks.dat")))
                return nextToExe;
            var inData = Path.Combine(AppDataDir, BuildConstants.ModelsDirName);
            if (Directory.Exists(inData) &&
                File.Exists(Path.Combine(inData, "shape_predictor_68_face_landmarks.dat")))
                return inData;
            return nextToExe;
        }
    }

    public static string OsLabel =>
        IsWindows ? "Windows" : (IsMacOS ? "macOS" : (IsLinux ? "Linux" : RuntimeInformation.OSDescription));

    public static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            if (IsWindows)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
            else if (IsMacOS)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("open", path) { UseShellExecute = true });
            else
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("xdg-open", path) { UseShellExecute = true });
        }
        catch { }
    }

    public static void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch { }
    }

    // 批次3：配置文件管理
    public static string CurrentProfileMarkerPath => Path.Combine(ProfilesRoot, "current_profile.txt");

    public static string SanitizeProfileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder();
        foreach (var c in (name ?? "").Trim())
            sb.Append(invalid.Contains(c) ? '_' : c);
        var s = sb.ToString().Trim('_');
        return string.IsNullOrEmpty(s) ? "default" : s;
    }

    public static void SetCurrentProfile(string name)
    {
        try { File.WriteAllText(CurrentProfileMarkerPath, name ?? ""); }
        catch { }
    }

    public static string GetCurrentProfile()
    {
        try { return File.Exists(CurrentProfileMarkerPath) ? (File.ReadAllText(CurrentProfileMarkerPath) ?? "").Trim() : ""; }
        catch { return ""; }
    }

    public static List<string> ListProfiles()
    {
        var list = new List<string>();
        try
        {
            var dir = Path.Combine(ProfilesRoot, "profiles");
            if (Directory.Exists(dir))
            {
                foreach (var d in Directory.GetDirectories(dir))
                    list.Add(Path.GetFileName(d));
            }
        }
        catch { }
        return list;
    }
}
