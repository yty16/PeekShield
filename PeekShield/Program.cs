using System;
using System.Threading.Tasks;
using Avalonia;
using PeekShield.Services;

namespace PeekShield;

class Program
{
    public static bool IsSecondaryInstance;

    [STAThread]
    public static void Main(string[] args)
    {
        IsSecondaryInstance = !SingleInstanceService.TryAcquire();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try { LoggerService.LogInfo("致命未处理异常（进程即将退出）：" + (e.ExceptionObject?.ToString() ?? "未知")); } catch { }
        };
        AppDomain.CurrentDomain.ProcessExit += (_, e) =>
        {
            try { LoggerService.LogInfo("进程退出（代码 " + Environment.ExitCode + "）"); } catch { }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            try { LoggerService.LogInfo("后台任务未处理异常：" + (e.Exception?.ToString() ?? "未知")); } catch { }
            e.SetObserved();
        };

        if (IsSecondaryInstance)
            LoggerService.LogInfo("次实例启动（PID " + Environment.ProcessId + "）：检测到主实例运行，将弹出提示对话框");
        else
            LoggerService.LogInfo("进程启动（PID " + Environment.ProcessId + "）");

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
