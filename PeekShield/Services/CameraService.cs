using System;
using System.Collections.Generic;
using OpenCvSharp;
#if WINDOWS
using DirectShowLib;
#endif

namespace PeekShield.Services;

public class CameraService : System.IDisposable
{
    private readonly object _lock = new();
    private VideoCapture? _cap;
    public int Index { get; private set; } = -1;
    public string? LastError { get; private set; }

    private static VideoCaptureAPIs Api =>
        OperatingSystem.IsWindows() ? VideoCaptureAPIs.DSHOW : VideoCaptureAPIs.ANY;

    public bool IsOpen
    {
        get
        {
            lock (_lock) return _cap != null && _cap.IsOpened();
        }
    }

    public bool Open(int index)
    {
        lock (_lock)
        {
            try
            {
                // 每次都使用全新 VideoCapture，并彻底丢弃上一轮对象：
                // 避免复用失败/损坏的 capture 并对其调用 Release 触发 videoio 原生 AV（进程直接崩溃）
                if (_cap != null)
                {
                    try { if (_cap.IsOpened()) _cap.Release(); } catch { }
                    _cap = null;
                }
                _cap = new VideoCapture();
                Index = index;
                if (!_cap.Open(index, Api))
                {
                    LastError = "无法打开摄像头（索引 " + index + "）";
                    _cap = null;
                    return false;
                }
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _cap = null;
                return false;
            }
        }
    }

    public bool ReadFrame(Mat frame)
    {
        lock (_lock)
        {
            if (_cap == null || !_cap.IsOpened()) return false;
            try
            {
                return _cap.Read(frame);
            }
            catch { return false; }
        }
    }

    public void Close()
    {
        lock (_lock)
        {
            try { _cap?.Release(); } catch { }
        }
    }

    private void CloseInternal() => Close();

    public void Dispose()
    {
        lock (_lock)
        {
            if (_cap != null)
            {
                try { _cap.Release(); } catch { }
                try { GC.SuppressFinalize(_cap); } catch { }
                _cap = null;
            }
        }
    }

    private static List<(int index, string name)>? _cachedDevices;
    private static readonly object _enumLock = new();

    public static List<(int index, string name)> Enumerate()
    {
        lock (_enumLock)
        {
            if (_cachedDevices != null) return _cachedDevices;
            _cachedDevices = EnumerateCore();
            return _cachedDevices;
        }
    }

    public static void RefreshCameraList()
    {
        lock (_enumLock)
        {
            _cachedDevices = EnumerateCore();
        }
    }

    private static List<(int index, string name)> EnumerateCore()
    {
        var list = new List<(int index, string name)>();
#if WINDOWS
        try
        {
            // 仅读取设备名，绝不打开硬件：监控循环已持有 index 0 的摄像头（DSHOW 同进程唯一句柄），
            // 若此处再 Open 会阻塞 UI 线程（卡死）并触发 videoio 原生 AV（进程崩溃）。DsDevice 枚举不需打开设备。
            var devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
            for (int i = 0; i < devices.Length; i++)
            {
                var friendly = devices[i]?.Name;
                list.Add((i, string.IsNullOrWhiteSpace(friendly) ? $"摄像头 {i}" : friendly!.Trim()));
            }
            if (list.Count > 0)
            {
                return list;
            }
        }
        catch (Exception ex)
        {
        }
#endif

        if (list.Count == 0) list.Add((0, "默认摄像头 (0)"));
        return list;
    }
}
