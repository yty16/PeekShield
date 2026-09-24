using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using PeekShield.Models;

namespace PeekShield.Services;

public static class GuardianService
{
    private const string AppAliveMutexName = "PeekShield_AppAlive_Mutex";
    private const string GuardMutexName = "PeekShield_Guard_Mutex";
    private const string FlagFileName = "guardian_shutdown";
    private const string AppAlivePidFile = "appalive.pid";
    private const string GuardPidFile = "guard.pid";
    private const string CrashMarkerFile = "crash_recover.mode";
    private const string WinTaskName = "PeekShieldGuard";
    private const string RelaunchArg = "--guardian-launch";
    public const string RecoverArg = "--crash-recover";
    private const string RelaunchSilentArg = "--guardian-relaunch";
    private static readonly string FlagFilePath = Path.Combine(Platform.AppDataDir, FlagFileName);
    private static readonly string AppAlivePidPath = Path.Combine(Platform.AppDataDir, AppAlivePidFile);
    private static readonly string GuardPidPath = Path.Combine(Platform.AppDataDir, GuardPidFile);
    private static readonly string CrashMarkerPath = Path.Combine(Platform.AppDataDir, CrashMarkerFile);

    private static string? _mainExe;
    private static Mutex? _appMutex;
    private static DateTime _lastPanicUtc = DateTime.MinValue;
    private static bool _daemonInstalled;
    private static readonly object _daemonLock = new();

    public static string FlagFile => FlagFilePath;

    public static void Init(string mainExe)
    {
        _mainExe = mainExe;
    }

    public static bool HoldAppAlive()
    {
        try
        {
            _appMutex = new Mutex(true, AppAliveMutexName, out var created);
            WriteAppPid();
            return created;
        }
        catch { return false; }
    }

    public static void ReleaseAppAlive()
    {
        try { _appMutex?.ReleaseMutex(); } catch { }
        try { _appMutex?.Dispose(); } catch { }
        _appMutex = null;
        try { if (File.Exists(AppAlivePidPath)) File.Delete(AppAlivePidPath); } catch { }
    }

    public static void SetGracefulShutdown()
    {
        try
        {
            Directory.CreateDirectory(Platform.AppDataDir);
            File.WriteAllText(FlagFilePath, "1");
        }
        catch (Exception ex)
        {
            try { LoggerService.LogInfo("进程保护：写入优雅退出标志失败：" + ex.Message); } catch { }
        }
    }

    public static void ClearGracefulShutdown()
    {
        try { if (File.Exists(FlagFilePath)) File.Delete(FlagFilePath); } catch { }
    }

    public static bool IsGracefulShutdownRequested()
    {
        try { return File.Exists(FlagFilePath); } catch { return false; }
    }

    public static void WriteCrashMarker(string mode)
    {
        try { Directory.CreateDirectory(Platform.AppDataDir); File.WriteAllText(CrashMarkerPath, mode); } catch { }
    }

    private static string ReadAndClearCrashMarker()
    {
        try
        {
            if (!File.Exists(CrashMarkerPath)) return "";
            string m = File.ReadAllText(CrashMarkerPath).Trim();
            File.Delete(CrashMarkerPath);
            return m;
        }
        catch { return ""; }
    }

    public static void ClearCrashMarker()
    {
        try { if (File.Exists(CrashMarkerPath)) File.Delete(CrashMarkerPath); } catch { }
    }

    private static void WriteAppPid()
    {
        try
        {
            Directory.CreateDirectory(Platform.AppDataDir);
            File.WriteAllText(AppAlivePidPath, Process.GetCurrentProcess().Id.ToString());
        }
        catch { }
    }

    private static void WriteGuardPid()
    {
        try
        {
            Directory.CreateDirectory(Platform.AppDataDir);
            File.WriteAllText(GuardPidPath, Process.GetCurrentProcess().Id.ToString());
        }
        catch { }
    }

    public static void Sync()
    {
        var s = PeekShieldEngine.Instance.Settings;
        if (s.PasswordEnabled && s.ProcessGuardEnabled && !IsGracefulShutdownRequested())
            InstallDaemon();
        else
            Stop();
    }

    public static void Stop()
    {
        SetGracefulShutdown();
        UninstallDaemon();
    }

    private static void InstallDaemon()
    {
        lock (_daemonLock)
        {
            if (_daemonInstalled) return;
            _daemonInstalled = true;
        }
        try
        {
            if (string.IsNullOrEmpty(_mainExe)) return;
            if (OperatingSystem.IsWindows()) InstallDaemonWindows(_mainExe);
            else if (OperatingSystem.IsLinux()) InstallDaemonLinux(_mainExe);
            else if (OperatingSystem.IsMacOS()) InstallDaemonMac(_mainExe);
            else SpawnGuard(_mainExe);
        }
        catch (Exception ex)
        {
            try { LoggerService.LogInfo("进程保护：安装守护进程失败，改为脱离式启动：" + ex.Message); } catch { }
            if (!string.IsNullOrEmpty(_mainExe)) SpawnGuard(_mainExe);
        }
    }

    private static void UninstallDaemon()
    {
        lock (_daemonLock)
        {
            _daemonInstalled = false;
        }
        try
        {
            if (OperatingSystem.IsWindows()) UninstallDaemonWindows();
            else if (OperatingSystem.IsLinux()) UninstallDaemonLinux();
            else if (OperatingSystem.IsMacOS()) UninstallDaemonMac();
        }
        catch { }
    }

    private static bool IsGuardRunning()
    {
        try
        {
            if (Mutex.TryOpenExisting(GuardMutexName, out var m))
            {
                m.Dispose();
                return true;
            }
            return false;
        }
        catch { return false; }
    }

    private static void InstallDaemonWindows(string exe)
    {
        string xml = BuildTaskXml(exe);
        string tmp = Path.Combine(Path.GetTempPath(), "peekshield_guard_task.xml");
        File.WriteAllText(tmp, xml, System.Text.Encoding.Unicode);
        int r = RunConsole("schtasks.exe", "/Create /TN \"" + WinTaskName + "\" /XML \"" + tmp + "\" /F");
        try { File.Delete(tmp); } catch { }
        if (r != 0) { SpawnGuard(exe); return; }
        if (IsGuardRunning()) return;
        int run = RunConsole("schtasks.exe", "/Run /TN \"" + WinTaskName + "\"");
        if (run != 0) SpawnGuard(exe);
    }

    private static void UninstallDaemonWindows()
    {
        RunConsole("schtasks.exe", "/Delete /TN \"" + WinTaskName + "\" /F");
    }

    private static string BuildTaskXml(string exe)
    {
        return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
            "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/05/06/tasks\">\r\n" +
            "  <RegistrationInfo><Author>PeekShield</Author><Description>PeekShield 进程保护守护进程</Description></RegistrationInfo>\r\n" +
            "  <Triggers><LogonTrigger><Enabled>true</Enabled></LogonTrigger></Triggers>\r\n" +
            "  <Principals><Principal id=\"Author\"><LogonType>InteractiveToken</LogonType><RunLevel>Limited</RunLevel></Principal></Principals>\r\n" +
            "  <Settings>\r\n" +
            "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
            "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
            "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
            "    <AllowHardTerminate>true</AllowHardTerminate>\r\n" +
            "    <StartWhenAvailable>false</StartWhenAvailable>\r\n" +
            "    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\r\n" +
            "    <IdleSettings><Duration>PT10M</Duration><WaitTimeout>PT1H</WaitTimeout><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>\r\n" +
            "    <AllowStartOnDemand>true</AllowStartOnDemand>\r\n" +
            "    <Enabled>true</Enabled>\r\n" +
            "    <Hidden>false</Hidden>\r\n" +
            "    <RunOnlyIfIdle>false</RunOnlyIfIdle>\r\n" +
            "    <WakeToRun>false</WakeToRun>\r\n" +
            "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
            "    <Priority>7</Priority>\r\n" +
            "    <RestartOnFailure><Interval>PT1M</Interval><Count>3</Count></RestartOnFailure>\r\n" +
            "  </Settings>\r\n" +
            "  <Actions Context=\"Author\"><Exec>\r\n" +
            "    <Command>\"" + exe + "\"</Command>\r\n" +
            "    <Arguments>--guard</Arguments>\r\n" +
            "  </Exec></Actions>\r\n" +
            "</Task>";
    }

    private static void InstallDaemonLinux(string exe)
    {
        try
        {
            string unitDir = Path.Combine(GetHome(), ".config", "systemd", "user");
            Directory.CreateDirectory(unitDir);
            string unit = Path.Combine(unitDir, "peekshield-guard.service");
            string content = "[Unit]\nDescription=PeekShield process guard\n\n[Service]\nExecStart=" + exe + " --guard\nRestart=always\nRestartSec=1\n\n[Install]\nWantedBy=default.target\n";
            File.WriteAllText(unit, content);
            RunConsole("systemctl", "--user daemon-reload");
            RunConsole("systemctl", "--user enable peekshield-guard");
            if (IsGuardRunning()) return;
            int r = RunConsole("systemctl", "--user start peekshield-guard");
            if (r != 0) SpawnGuard(exe);
        }
        catch { SpawnGuard(exe); }
    }

    private static void UninstallDaemonLinux()
    {
        try { RunConsole("systemctl", "--user disable --now peekshield-guard"); } catch { }
        try { File.Delete(Path.Combine(GetHome(), ".config", "systemd", "user", "peekshield-guard.service")); } catch { }
    }

    private static void InstallDaemonMac(string exe)
    {
        try
        {
            string dir = Path.Combine(GetHome(), "Library", "LaunchAgents");
            Directory.CreateDirectory(dir);
            string plist = Path.Combine(dir, "com.peekshield.guard.plist");
            string content = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n<plist version=\"1.0\">\n<dict>\n  <key>Label</key><string>com.peekshield.guard</string>\n  <key>ProgramArguments</key>\n  <array>\n    <string>" + exe + "</string>\n    <string>--guard</string>\n  </array>\n  <key>KeepAlive</key><true/>\n  <key>RunAtLoad</key><true/>\n</dict>\n</plist>\n";
            File.WriteAllText(plist, content);
            if (!IsGuardRunning()) RunConsole("launchctl", "load " + plist);
        }
        catch { SpawnGuard(exe); }
    }

    private static void UninstallDaemonMac()
    {
        string plist = Path.Combine(GetHome(), "Library", "LaunchAgents", "com.peekshield.guard.plist");
        try { RunConsole("launchctl", "unload " + plist); } catch { }
        try { File.Delete(plist); } catch { }
    }

    private static string GetHome() =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static void SpawnGuard(string exe)
    {
        if (IsGuardRunning()) return;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var psi = new ProcessStartInfo("cmd.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                psi.ArgumentList.Add("/c");
                psi.ArgumentList.Add("start");
                psi.ArgumentList.Add("");
                psi.ArgumentList.Add(exe);
                psi.ArgumentList.Add("--guard");
                Process.Start(psi);
            }
            else
            {
                var psi = new ProcessStartInfo("setsid")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add(exe);
                psi.ArgumentList.Add("--guard");
                Process.Start(psi);
            }
        }
        catch (Exception ex)
        {
            try { LoggerService.LogInfo("进程保护：脱离式启动守卫失败：" + ex.Message); } catch { }
        }
    }

    public static void RunGuard()
    {
        if (Mutex.TryOpenExisting(GuardMutexName, out var existing))
        {
            existing.Dispose();
            return;
        }

        using var m = new Mutex(true, GuardMutexName, out var created);
        if (!created) return;

        WriteGuardPid();
        try { LoggerService.LogInfo("进程保护：守护进程已启动"); } catch { }

        int missingCount = 0;
        bool wasAlive = AppAlive();
        DateTime burstStart = DateTime.MinValue;
        int burstCrashes = 0;
        bool suppressed = false;
        while (true)
        {
            if (IsGracefulShutdownRequested()) break;
            bool alive = AppAlive();
            if (!alive)
            {
                if (wasAlive)
                {
                    missingCount = 1;
                    try { LoggerService.LogInfo("进程保护：检测到主程序不再存活（第 1 次确认），继续观察"); } catch { }
                }
                else
                {
                    missingCount++;
                }
                if (missingCount >= 2)
                    OnAppCrashed(ref suppressed, ref burstStart, ref burstCrashes);
            }
            else
            {
                missingCount = 0;
                // 任意一次验证成功（主程序恢复正常存活）即清理突发抑制累计与抑制态，
                // 避免一次成功拉起后残留计数误伤后续崩溃处理（计数只在「反复死亡」时才有意义）
                if (burstCrashes != 0 || suppressed)
                {
                    burstCrashes = 0;
                    suppressed = false;
                    burstStart = DateTime.UtcNow;
                    try { LoggerService.LogInfo("进程保护：主程序已正常存活，突发抑制计数与抑制态已清理"); } catch { }
                }
            }
            wasAlive = alive;
            try { Thread.Sleep(1000); } catch { break; }
        }
        try { LoggerService.LogInfo("进程保护：守护进程退出（已置位优雅退出标志）"); } catch { }
    }

    private static void OnAppCrashed(ref bool suppressed, ref DateTime burstStart, ref int burstCrashes)
    {
        var now = DateTime.UtcNow;
        if ((now - burstStart).TotalSeconds > 60) { burstStart = now; burstCrashes = 0; }

        // 先读取崩溃标记：明确标记的软件自身崩溃（提示崩溃/静默重启/自动退出）始终按用户设置处理，
        // 不受突发抑制影响——用户已明确选择行为，且提示崩溃由用户交互打破，不会死锁屏。
        string marker = ReadAndClearCrashMarker();
        if (marker == "exit")
        {
            try { LoggerService.LogInfo("进程保护：主程序按「自动退出应用」设置优雅退出，守护进程不再自动重启（继续待命）"); } catch { }
            return;
        }
        if (marker == "prompt")
        {
            try { LoggerService.LogInfo("进程保护：主程序按「提示崩溃」设置退出，已拉起恢复提示等待用户确认（不锁屏）"); } catch { }
            LaunchAppForRecovery();
            return;
        }
        if (marker == "silent")
        {
            try { LoggerService.LogInfo("进程保护：主程序按「静默重启应用」设置退出，已拉起新实例（不锁屏，与强制结束区分）"); } catch { }
            LaunchApp(RelaunchSilentArg);
            return;
        }

        // 无标记死亡（外部强制结束 / 原生 AV）：最危险，会锁屏 + 反复拉起，受突发抑制保护以防死循环
        burstCrashes++;
        try { LoggerService.LogInfo("进程保护：检测到主程序无标记终止（疑似被强制结束），准备锁屏并自动重启（累计=" + burstCrashes + "）"); } catch { }
        if (burstCrashes >= 4)
        {
            if (!suppressed)
            {
                suppressed = true;
                try { LoggerService.LogInfo("进程保护：检测到主程序在短期内被反复强制结束（疑似被杀软/任务管理器干扰），已暂停自动拉起与锁屏以避免死循环；请手动启动应用。"); } catch { }
            }
            return;
        }

        TriggerPanic();
        LaunchApp();
    }

    private static bool AppAlive()
    {
        return IsProcessAliveByPid(AppAlivePidPath);
    }

    private static bool IsMainAlive()
    {
        return IsProcessAliveByPid(AppAlivePidPath);
    }

    private static bool IsProcessAliveByPid(string pidPath)
    {
        try
        {
            if (!File.Exists(pidPath)) return false;
            string txt = File.ReadAllText(pidPath).Trim();
            if (!int.TryParse(txt, out int pid) || pid <= 0) return false;
            var p = Process.GetProcessById(pid);
            try
            {
                return p.ProcessName.IndexOf("PeekShield", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return true; }
            finally { p.Dispose(); }
        }
        catch { return false; }
    }

    public static bool IsGuardAlive()
    {
        return IsProcessAliveByPid(GuardPidPath);
    }

    public static void EnsureGuard()
    {
        try
        {
            if (IsGracefulShutdownRequested()) return;
            if (IsGuardAlive()) return;
            var s = PeekShieldEngine.Instance.Settings;
            if (!(s.PasswordEnabled && s.ProcessGuardEnabled)) return;
            LoggerService.LogInfo("进程保护：主程序检测到守护进程已退出，重新拉起守护");
        }
        catch { }
        _daemonInstalled = false;
        InstallDaemon();
    }

    private static void LaunchApp(string arg = RelaunchArg)
    {
        try
        {
            if (string.IsNullOrEmpty(_mainExe)) return;
            if (IsMainAlive()) return;
            Process.Start(new ProcessStartInfo(_mainExe)
            {
                UseShellExecute = true,
                Arguments = arg
            });
        }
        catch (Exception ex)
        {
            try { LoggerService.LogInfo("进程保护：启动主程序失败：" + ex.Message); } catch { }
        }
    }

    private static void LaunchAppForRecovery()
    {
        try
        {
            if (string.IsNullOrEmpty(_mainExe)) return;
            if (IsMainAlive()) return;
            Process.Start(new ProcessStartInfo(_mainExe)
            {
                UseShellExecute = true,
                Arguments = RecoverArg
            });
        }
        catch (Exception ex)
        {
            try { LoggerService.LogInfo("进程保护：启动恢复主程序失败：" + ex.Message); } catch { }
        }
    }

    public static void TriggerPanic()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastPanicUtc).TotalSeconds < 5) return;
        _lastPanicUtc = now;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                LockWorkStation();
            }
            else if (OperatingSystem.IsLinux())
            {
                if (RunConsole("loginctl", "lock-session") != 0)
                    RunConsole("xdg-screensaver", "lock");
            }
            else if (OperatingSystem.IsMacOS())
            {
                RunConsole("/System/Library/CoreServices/Menu Extras/User.menu/Contents/Resources/CGSession", "-suspend");
            }
        }
        catch { }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();

    private static int RunConsole(string exe, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var p = Process.Start(psi);
            if (p == null) return -1;
            if (!p.WaitForExit(15000)) return -1;
            return p.ExitCode;
        }
        catch { return -1; }
    }
}
