using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using PeekShield.Services;

namespace PeekShield;

class Program
{
    public static bool IsSecondaryInstance;
    public static bool IsUninstallVerify;
    public static bool IsInstallVerify;

    [STAThread]
    public static void Main(string[] args)
    {
        GuardianService.Init(Process.GetCurrentProcess().MainModule?.FileName ?? "");

        if (args.Contains("--guard"))
        {
            GuardianService.RunGuard();
            return;
        }

        IsUninstallVerify = args.Contains("--uninstall-verify");
        IsInstallVerify = args.Contains("--install-verify");
        IsSecondaryInstance = (IsUninstallVerify || IsInstallVerify) ? false : !SingleInstanceService.TryAcquire();

        // 批次3：解析 --profile 参数，决定使用哪套配置文件（目录隔离）
        string? profileArg = null;
        foreach (var a in args)
        {
            if (a.StartsWith("--profile=", StringComparison.OrdinalIgnoreCase))
                profileArg = a.Substring("--profile=".Length).Trim();
        }
        Platform.ProfileName = string.IsNullOrWhiteSpace(profileArg)
            ? Platform.GetCurrentProfile()
            : Platform.SanitizeProfileName(profileArg);

        bool guardianLaunch = args.Contains("--guardian-launch");
        if (IsSecondaryInstance && guardianLaunch)
        {
            try { LoggerService.LogInfo("守护进程拉起的新实例检测到主实例仍存活，静默退出"); } catch { }
            return;
        }

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
