using System;
using System.Threading.Tasks;
using Avalonia;
using PeekShield.Services;

namespace PeekShield;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try { LoggerService.LogInfo("致命未处理异常（进程即将退出）：" + (e.ExceptionObject?.ToString() ?? "未知")); } catch { }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            try { LoggerService.LogInfo("后台任务未处理异常：" + (e.Exception?.ToString() ?? "未知")); } catch { }
            e.SetObserved();
        };

        LoggerService.LogInfo("进程启动（PID " + Environment.ProcessId + "）");

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
