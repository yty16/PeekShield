using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using PeekShield.Models;
using PeekShield.Services;
using PeekShield.Views;
using System.Diagnostics;
using System.Threading.Tasks;

namespace PeekShield;

public partial class App : Application
{
    private static bool _explicitExit;
    private static bool _exiting;
    private static System.Threading.Timer? _guardWatchdog;
    private static int _guardMissingCount;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (Program.IsUninstallVerify)
        {
            HandleUninstallVerify();
            base.OnFrameworkInitializationCompleted();
            return;
        }

        if (Program.IsSecondaryInstance)
        {
            if (this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
            {
                d.ShutdownMode = ShutdownMode.OnLastWindowClose;
                var dlg = new AlreadyRunningDialog();
                dlg.Closed += (_, _) =>
                {
                    try { LoggerService.LogInfo("次实例对话框关闭（用户选择=" + dlg.Result + "）"); } catch { }
                    if (dlg.Result == AlreadyRunningDialog.Choice.BringToFront)
                        SingleInstanceService.TrySendShowToExisting();
                    else if (dlg.Result == AlreadyRunningDialog.Choice.KillAndRelaunch)
                        KillOtherInstancesAndRelaunch();
                    d.Shutdown();
                };
                dlg.Show();
            }
            base.OnFrameworkInitializationCompleted();
            return;
        }

        if (this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                try { LoggerService.LogInfo("UI 线程未处理异常（已拦截防闪退）：" + (e.Exception?.ToString() ?? "未知")); } catch { }
                e.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                try
                {
                    var ex = e.ExceptionObject as Exception;
                    LoggerService.LogInfo("进程内未处理异常（致命崩溃）：" + (ex?.ToString() ?? "未知"));
                }
                catch { }
                HandleCrash(e.ExceptionObject as Exception);
            };

            if (Environment.GetCommandLineArgs().Contains("--crash-recover"))
            {
                var crashSettings = PeekShieldSettings.Load();
                ThemeService.Init(crashSettings.ThemeMode);
                ApplyTheme();
                if (crashSettings.CrashBehaviorOnCrash == CrashBehavior.PromptRestart)
                {
                    ShowCrashRecoverPromptFirst(desktop);
                    base.OnFrameworkInitializationCompleted();
                    return;
                }
                // 如果用户已把设置改为非提示，则 crash-recover 实例直接退出
                ShutdownCrashPrompt();
                base.OnFrameworkInitializationCompleted();
                return;
            }

            PeekShieldEngine.Instance.Initialize();
            ThemeService.Init(PeekShieldEngine.Instance.Settings.ThemeMode);
            ApplyTheme();

            ContinueStartup(desktop);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static void ShowCrashRecoverPromptFirst(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            try { GuardianService.WriteCrashMarker("exit"); } catch { }
            var dlg = new PeekShield.Views.CrashRecoverDialog();
            try { desktop.MainWindow = dlg; } catch { }
            dlg.Closed += (_, _) =>
            {
                if (dlg.RecoverChoice == true)
                {
                    try { LoggerService.LogInfo("崩溃处理：用户在恢复提示中选择重启应用，启动新实例后本提示进程退出"); } catch { }
                    RelaunchMainApp();
                    ShutdownCrashPrompt();
                }
                else
                {
                    try { LoggerService.LogInfo("崩溃处理：用户在恢复提示中选择关闭应用，本提示进程退出"); } catch { }
                    ShutdownCrashPrompt();
                }
            };
            dlg.Show();
        }
        catch (Exception ex)
        {
            try { LoggerService.LogInfo("崩溃恢复提示显示异常：" + ex.Message); } catch { }
            RelaunchMainApp();
            ShutdownCrashPrompt();
        }
    }

    private static void RelaunchMainApp()
    {
        try
        {
            var path = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(path)) return;
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            try { LoggerService.LogInfo("崩溃处理：启动新主实例失败：" + ex.Message); } catch { }
        }
    }

    private static void ShutdownCrashPrompt()
    {
        try
        {
            _explicitExit = true;
            _exiting = true;
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
                d.Shutdown();
        }
        catch { }
    }

    private static void ContinueStartup(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (!GuardianService.HoldAppAlive())
        {
            LoggerService.LogInfo("检测到已有主实例持有存活锁，本实例将作为次实例运行");
        }
        GuardianService.Sync();
        GuardianService.ClearCrashMarker();
        if (!Environment.GetCommandLineArgs().Contains("--crash-recover"))
            CrashReportService.Clear();

        var s = PeekShieldEngine.Instance.Settings;
        if (s.PasswordEnabled && s.ProcessGuardEnabled)
        {
            if (Environment.GetCommandLineArgs().Contains("--guardian-launch"))
            {
                try { LoggerService.LogInfo("进程保护：因被杀被守护进程拉起，启动即锁屏"); } catch { }
                GuardianService.TriggerPanic();
            }
            StartGuardWatchdog();
        }

        var main = new MainWindow();
        desktop.MainWindow = main;
        MainWindow.Instance = main;

        PeekShieldEngine.Instance.OpenSettingsRequested += MainWindow.ShowSettings;
        PeekShieldEngine.Instance.OpenSecurityRequested += MainWindow.ShowSecurity;
        PeekShieldEngine.Instance.OpenPrivacyRequested += MainWindow.ShowPrivacy;

        main.Closing += (_, e) =>
        {
            if (!_explicitExit)
            {
                e.Cancel = true;
                main.Hide();
            }
        };
        main.Show();

        if (ConsentService.NeedsConsent(PeekShieldEngine.Instance.Settings))
            _ = RunConsentGateAsync(main);
        else
            _ = UpdateService.CheckAndNotifyAsync(main);

        SingleInstanceService.StartServer(() => Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var w = MainWindow.Instance;
                if (w == null) return;
                w.Show();
                if (w.WindowState == Avalonia.Controls.WindowState.Minimized)
                    w.WindowState = Avalonia.Controls.WindowState.Normal;
                w.Activate();
            }
            catch { }
        }));

        ThemeService.Changed += () => Dispatcher.UIThread.Post(ApplyTheme);
    }

    private static void ApplyTheme()
    {
        if (Application.Current != null)
            Application.Current.RequestedThemeVariant = ThemeService.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    private static void KillOtherInstancesAndRelaunch()
    {
        try
        {
            int self = Process.GetCurrentProcess().Id;
            string? path = Process.GetCurrentProcess().MainModule?.FileName;
            foreach (var p in Process.GetProcessesByName("PeekShield"))
            {
                if (p.Id != self)
                {
                    try { p.Kill(); } catch { }
                }
            }
            if (!string.IsNullOrEmpty(path))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (System.Exception ex)
        {
            try { LoggerService.LogInfo("结束旧实例并重启失败：" + ex.Message); } catch { }
        }
    }

    private static async Task RunConsentGateAsync(Window owner)
    {
        try
        {
            bool cont = await ConsentService.RunAsync(owner, PeekShieldEngine.Instance.Settings, true);
            if (!cont)
            {
                LoggerService.LogInfo("首次隐私告知未获同意，程序退出");
                ForceExit();
            }
            else
            {
                _ = UpdateService.CheckAndNotifyAsync(owner);
            }
        }
        catch (System.Exception ex)
        {
            try { LoggerService.LogInfo("隐私告知窗口异常：" + ex.Message); } catch { }
        }
    }

    public static void RequestExit()
    {
        if (_exiting) return;
        var s = PeekShieldEngine.Instance.Settings;
        if (s.PasswordEnabled && s.ProtectExit)
        {
            var w = MainWindow.Instance;
            if (w != null && w.IsVisible)
            {
                var dlg = new PasswordWindow("验证以退出", "退出应用前需验证密码。", s.PasswordHash, s.SecurityQuestion, s.SecurityAnswerHash, PeekShieldEngine.Instance, s.PasswordEnabled && s.FaceUnlockEnabled, PeekShieldEngine.Instance.QuickVerifyAvailable);
                dlg.Closed += (_, _) =>
                {
                    if (dlg.Result == PeekShield.Views.PasswordWindow.Outcome.Ok ||
                        dlg.Result == PeekShield.Views.PasswordWindow.Outcome.Recovery)
                        DoExit();
                };
                dlg.ShowDialog(w);
                return;
            }
        }
        DoExit();
    }

    public static void ForceExit() => DoExit();

    private static void HandleCrash(Exception? ex)
    {
        try
        {
            CrashReportService.Write(ex);
            var s = PeekShieldEngine.Instance.Settings;
            CrashBehavior behavior = s.CrashBehaviorOnCrash;

            if (behavior == CrashBehavior.ExitApp)
            {
                try { LoggerService.LogInfo("崩溃处理：按「自动退出应用」设置，写标记并退出（不重启，与进程保护看门狗互不影响）"); } catch { }
                GuardianService.WriteCrashMarker("exit");
                CrashExit();
                return;
            }
            if (behavior == CrashBehavior.PromptRestart)
            {
                try { LoggerService.LogInfo("崩溃处理：按「提示崩溃」设置，写标记并自行拉起恢复提示进程（守护进程作为兜底）"); } catch { }
                GuardianService.WriteCrashMarker("prompt");
                try { SingleInstanceService.Release(); } catch { }
                SelfRelaunch(GuardianService.RecoverArg);
                CrashExit();
                return;
            }
            try { LoggerService.LogInfo("崩溃处理：按「静默重启应用」设置，写标记并自行重启（不锁屏，守护进程作为兜底）"); } catch { }
            GuardianService.WriteCrashMarker("silent");
            try { SingleInstanceService.Release(); } catch { }
            SelfRelaunch(null);
            CrashExit();
        }
        catch
        {
            try { Environment.Exit(1); } catch { }
        }
    }

    private static void SelfRelaunch(string? extraArg)
    {
        try
        {
            var path = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(path)) return;
            var psi = new ProcessStartInfo(path) { UseShellExecute = true };
            if (!string.IsNullOrEmpty(extraArg)) psi.ArgumentList.Add(extraArg);
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            try { LoggerService.LogInfo("崩溃处理：自重启失败：" + ex.Message); } catch { }
        }
    }

    private static void CrashExit()
    {
        if (_exiting) return;
        _exiting = true;
        _explicitExit = true;
        try { LoggerService.LogInfo("应用因崩溃进入崩溃恢复流程（不卸载守护进程）"); } catch { }
        StopGuardWatchdog();
        try { GuardianService.ReleaseAppAlive(); } catch { }
        try { PeekShieldEngine.Instance.Dispose(); } catch { }
        try { SingleInstanceService.Release(); } catch { }
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d) d.Shutdown();
    }

    private static void DoExit()
    {
        if (_exiting) return;
        _exiting = true;
        _explicitExit = true;
        try { LoggerService.LogInfo("应用开始正常退出"); } catch { }
        StopGuardWatchdog();
        GuardianService.Stop();
        GuardianService.ReleaseAppAlive();
        try { PeekShieldEngine.Instance.Dispose(); } catch { }
        SingleInstanceService.Release();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d) d.Shutdown();
    }

    private static void StartGuardWatchdog()
    {
        try
        {
            _guardMissingCount = 0;
            _guardWatchdog = new System.Threading.Timer(_ => GuardWatchdogTick(), null, 3000, 2000);
        }
        catch { }
    }

    private static void StopGuardWatchdog()
    {
        try { _guardWatchdog?.Dispose(); } catch { }
        _guardWatchdog = null;
    }

    private static void GuardWatchdogTick()
    {
        try
        {
            if (GuardianService.IsGracefulShutdownRequested()) { StopGuardWatchdog(); return; }
            if (GuardianService.IsGuardAlive()) { _guardMissingCount = 0; return; }
            _guardMissingCount++;
            if (_guardMissingCount >= 5)
            {
                _guardMissingCount = 0;
                try { LoggerService.LogInfo("进程保护：主程序检测到守护进程已退出，重新拉起守护（不锁屏，主程序仍在运行）"); } catch { }
                GuardianService.EnsureGuard();
            }
        }
        catch { }
    }

    private static void HandleUninstallVerify()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
            d.ShutdownMode = ShutdownMode.OnLastWindowClose;

        var settings = PeekShieldSettings.Load();
        SecurityService.Settings = settings;
        if (!settings.PasswordEnabled || !settings.ProtectUninstall)
        {
            Environment.Exit(0);
            return;
        }

        try { ThemeService.Init(settings.ThemeMode); } catch { }

        var w = new PasswordWindow("卸载验证",
            "为保障你的隐私，卸载本软件前需验证密码。若已设置保密问题，可通过回答保密问题来验证。",
            settings.PasswordHash, settings.SecurityQuestion, settings.SecurityAnswerHash, PeekShieldEngine.Instance, settings.PasswordEnabled && settings.FaceUnlockEnabled, PeekShieldEngine.Instance.QuickVerifyAvailable);
        w.Closed += (_, _) =>
        {
            int code = (w.Result == PeekShield.Views.PasswordWindow.Outcome.Ok ||
                        w.Result == PeekShield.Views.PasswordWindow.Outcome.Recovery) ? 0 : 2;
            Environment.Exit(code);
        };
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d2)
            d2.MainWindow = w;
        w.Show();
    }
}
