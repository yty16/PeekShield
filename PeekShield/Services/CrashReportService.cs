using System;
using System.IO;
using System.Linq;
using System.Text;

namespace PeekShield.Services;

public static class CrashReportService
{
    private const string ReportFile = "crash_report.txt";
    private static readonly string ReportPath = Path.Combine(Platform.AppDataDir, ReportFile);

    public static void Write(Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(Platform.AppDataDir);
            var sb = new StringBuilder();
            sb.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("系统：" + Platform.OsLabel);
            if (ex != null)
            {
                sb.AppendLine("大白话原因：" + Explain(ex));
                sb.AppendLine("技术类型：" + ex.GetType().FullName);
                sb.AppendLine("技术信息：" + (ex.Message ?? "（无）"));
                sb.AppendLine("堆栈：");
                sb.AppendLine(ex.StackTrace ?? "（无堆栈）");
                if (ex.InnerException != null)
                    sb.AppendLine("内部异常：" + ex.InnerException.GetType().FullName + "：" + (ex.InnerException.Message ?? "（无）"));
            }
            else
            {
                sb.AppendLine("大白话原因：软件在运行时意外停止（未能捕获具体错误）。");
            }
            sb.AppendLine();
            sb.AppendLine("------ 崩溃前后的运行日志（engine.log 末尾）------");
            sb.AppendLine(ReadLogTail());
            File.WriteAllText(ReportPath, sb.ToString());
        }
        catch { }
    }

    public static CrashReport? Read()
    {
        try
        {
            if (!File.Exists(ReportPath)) return null;
            string text = File.ReadAllText(ReportPath);
            string reason = "软件在运行时崩溃了。";
            foreach (var line in text.Split('\n'))
            {
                if (line.StartsWith("大白话原因："))
                {
                    reason = line.Substring("大白话原因：".Length).Trim();
                    break;
                }
            }
            int idx = text.IndexOf("------ 崩溃前后的运行日志");
            string log = idx >= 0 ? text.Substring(idx) : text;
            return new CrashReport { Reason = reason, Detail = text, LogTail = log };
        }
        catch { return null; }
    }

    public static void Clear()
    {
        try { if (File.Exists(ReportPath)) File.Delete(ReportPath); } catch { }
    }

    private static string ReadLogTail()
    {
        try
        {
            var log = Path.Combine(LoggerService.LogDir, "engine.log");
            if (!File.Exists(log)) return "（无日志文件）";
            var lines = File.ReadAllLines(log);
            var tail = lines.Skip(Math.Max(0, lines.Length - 80)).ToArray();
            return string.Join("\n", tail);
        }
        catch { return "（读取日志失败）"; }
    }

    public static string Explain(Exception ex)
    {
        string msg = (ex.Message ?? "").ToLower();
        string type = ex.GetType().Name;
        if (ex is AccessViolationException || type == "AccessViolationException")
            return "软件试图访问了不被允许的内存（通常是摄像头/人脸底层的驱动出错），这是底层异常，软件自身拦不住。";
        if (msg.Contains("opencv") || msg.Contains("videocapture") || msg.Contains("camera") || msg.Contains("摄像头") || msg.Contains("capture"))
            return "摄像头打开或读取画面时出错，可能是摄像头被别的程序占用、没插好或驱动异常。";
        if (msg.Contains("dlib") || msg.Contains("face") || msg.Contains("shape_predictor") || msg.Contains("landmark"))
            return "人脸识别或人脸对齐时出错，可能是人脸模型文件缺失或损坏。";
        if (ex is OutOfMemoryException)
            return "电脑内存不够用了，软件被迫停止。";
        if (ex is System.IO.FileNotFoundException || msg.Contains(".dll") || msg.Contains("could not load") || msg.Contains("找不到"))
            return "缺少某个必要的运行文件（比如某个 .dll 没找到），可能是安装不完整或被杀毒软件删掉。";
        if (ex is NullReferenceException)
            return "软件内部有个对象为空就拿来用了（这是程序的 bug），导致崩溃。";
        if (ex is TypeInitializationException || ex is System.Reflection.ReflectionTypeLoadException)
            return "程序在启动时初始化失败，可能是某个组件没加载成功。";
        if (ex is System.Threading.ThreadAbortException)
            return "有线程被强制中断，导致崩溃。";
        return "软件内部发生了一个未知错误，已自动记录日志，可把下方日志发给开发者排查。";
    }
}

public sealed class CrashReport
{
    public string Reason { get; set; } = "";
    public string Detail { get; set; } = "";
    public string LogTail { get; set; } = "";
}
