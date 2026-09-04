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
