using System;
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

    public static string AppDataDir
    {
        get
        {
            string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(baseDir))
                baseDir = AppContext.BaseDirectory;
            return Path.Combine(baseDir, "PeekShield");
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
}
