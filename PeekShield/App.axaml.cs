using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
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
                RequestExit();
            }
        }
        catch (System.Exception ex)
        {
            try { LoggerService.LogInfo("隐私告知窗口异常：" + ex.Message); } catch { }
        }
    }

    public static void RequestExit()
    {
        _explicitExit = true;
        try { LoggerService.LogInfo("应用开始正常退出"); } catch { }
        try { PeekShieldEngine.Instance.Dispose(); } catch { }
        SingleInstanceService.Release();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d) d.Shutdown();
    }
}
