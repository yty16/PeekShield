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
        _mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            try { _mutex.Dispose(); } catch { }
            _mutex = null;
        }
        return createdNew;
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
