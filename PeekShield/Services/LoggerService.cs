using System;
using System.IO;

namespace PeekShield.Services;

public static class LoggerService
{
    public static string LogDir => Platform.LogsDir;

    public static void Ensure() => Directory.CreateDirectory(LogDir);

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
}
