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

namespace PeekShield;

public partial class App : Application
{
    private static bool _explicitExit;

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

            PeekShieldEngine.Instance.Initialize();
            ThemeService.Init(PeekShieldEngine.Instance.Settings.ThemeMode);
            ApplyTheme();

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
        base.OnFrameworkInitializationCompleted();
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
        var s = PeekShieldEngine.Instance.Settings;
        if (s.PasswordEnabled && s.ProtectExit)
        {
            var w = MainWindow.Instance;
            if (w != null)
            {
                var dlg = new PasswordWindow("验证以退出", "退出应用前需验证密码。", s.PasswordHash, s.SecurityQuestion, s.SecurityAnswerHash);
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

    private static void DoExit()
    {
        _explicitExit = true;
        try { LoggerService.LogInfo("应用开始正常退出"); } catch { }
        try { PeekShieldEngine.Instance.Dispose(); } catch { }
        SingleInstanceService.Release();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d) d.Shutdown();
    }

    private static void HandleUninstallVerify()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)
            d.ShutdownMode = ShutdownMode.OnLastWindowClose;

        var settings = PeekShieldSettings.Load();
        if (!settings.PasswordEnabled || !settings.ProtectUninstall)
        {
            Environment.Exit(0);
            return;
        }

        try { ThemeService.Init(settings.ThemeMode); } catch { }

        var w = new PasswordWindow("卸载验证",
            "为保障你的隐私，卸载本软件前需验证密码。若已设置保密问题，可通过回答保密问题来验证。",
            settings.PasswordHash, settings.SecurityQuestion, settings.SecurityAnswerHash);
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
