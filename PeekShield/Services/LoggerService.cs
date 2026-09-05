using System;
using System.IO;
using OpenCvSharp;
using PeekShield.Models;

namespace PeekShield.Services;

public static class LoggerService
{
    public static string LogDir => Platform.LogsDir;

    public static void Ensure() => Directory.CreateDirectory(LogDir);

    public static void LogPeek(PeekShieldSettings s, int faces)
    {
        try
        {
            Ensure();
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t检测到偷窥\t人脸数={faces}"
                       + $"\t雾化={s.ActionBlur}\t弹窗={s.ActionPopup}\t声音={s.ActionSound}\t最小化={s.ActionMinimize}";
            File.AppendAllText(Path.Combine(LogDir, "peek.log"), line + "\n");
        }
        catch { }
    }

    public static void SaveSnapshot(Mat frame, PeekShieldSettings s)
    {
        if (!s.ScreenshotOnPeek) return;
        try
        {
            Ensure();
            var name = $"peek_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            Cv2.ImWrite(Path.Combine(LogDir, name), frame);
        }
        catch { }
    }

    public static void LogInfo(string message)
    {
        try
        {
            Ensure();
            File.AppendAllText(Path.Combine(LogDir, "engine.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{message}\n");
        }
        catch { }
    }

    public static int CleanupOldData(int days)
    {
        if (days <= 0) return 0;
        int n = 0;
        try
        {
            if (!Directory.Exists(LogDir)) return 0;
            var cutoff = DateTime.Now.AddDays(-days);
            foreach (var f in Directory.GetFiles(LogDir))
            {
                try
                {
                    if (File.GetLastWriteTime(f) < cutoff)
                    {
                        File.Delete(f);
                        n++;
                    }
                }
                catch { }
            }
            if (n > 0) LogInfo($"自动清理：已删除超过 {days} 天的日志与截图 {n} 个文件");
        }
        catch { }
        return n;
    }

    public static int DeleteLogsAndSnapshots()
    {
        int n = 0;
        try
        {
            if (!Directory.Exists(LogDir)) return 0;
            foreach (var f in Directory.GetFiles(LogDir))
            {
                try
                {
                    File.Delete(f);
                    n++;
                }
                catch { }
            }
        }
        catch { }
        return n;
    }

    public static void SaveDebugFrame(Mat frame, PeekShieldSettings s)
    {
        if (!s.ScreenshotOnPeek) return;
        try
        {
            Ensure();
            Cv2.ImWrite(Path.Combine(LogDir, "debug_frame.jpg"), frame);
        }
        catch { }
    }
}
