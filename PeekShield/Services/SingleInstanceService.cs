using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace PeekShield.Services;

public static class SingleInstanceService
{
    private const string MutexName = "PeekShield_SingleInstance_Mutex";
    private const string PipeName = "PeekShield_SingleInstance_Pipe";
    private static Mutex? _mutex;

    public static bool TryAcquire()
    {
        try
        {
            _mutex = new Mutex(true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                // 已存在但不属于本实例：可能是真实次实例，也可能是上次主程序被原生崩溃(abandoned)遗留的废弃互斥量。
                // 释放本句柄后重试一次：真实实例的句柄仍在、再次打开仍 createdNew=false（安全退出）；
                // 若为废弃互斥量，释放后 OS 销毁对象、再次打开 createdNew=true（本实例接管，避免守护拉起时全体静默退出）。
                try { _mutex.Dispose(); } catch { }
                _mutex = new Mutex(true, MutexName, out createdNew);
                if (!createdNew)
                {
                    try { _mutex.Dispose(); } catch { }
                    _mutex = null;
                    return false;
                }
            }
            return true;
        }
        catch
        {
            try { _mutex?.Dispose(); } catch { }
            _mutex = null;
            return false;
        }
    }

    public static bool TrySendShowToExisting()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1500);
            using var w = new StreamWriter(client) { AutoFlush = true };
            w.Write("SHOW");
            LoggerService.LogInfo("已向主实例发送 SHOW 唤起指令");
            return true;
        }
        catch (Exception ex)
        {
            LoggerService.LogInfo("向主实例发送 SHOW 失败：" + ex.Message);
            return false;
        }
    }

    public static void StartServer(Action onShow)
    {
        var t = new Thread(() =>
        {
            while (true)
            {
                NamedPipeServerStream? server = null;
                try
                {
                    server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1);
                    server.WaitForConnection();
                    using var r = new StreamReader(server);
                    var msg = r.ReadToEnd();
                    if (msg.Contains("SHOW", StringComparison.OrdinalIgnoreCase)) onShow();
                }
                catch { }
                finally
                {
                    try { server?.Dispose(); } catch { }
                }
            }
        })
        { IsBackground = true };
        t.Start();
    }

    public static void Release()
    {
        try { _mutex?.ReleaseMutex(); } catch { }
        try { _mutex?.Dispose(); } catch { }
        _mutex = null;
    }
}
