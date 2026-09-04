using System;
using System.Collections.Generic;
using OpenCvSharp;
#if WINDOWS
using DirectShowLib;
#endif

namespace PeekShield.Services;

public class CameraService : System.IDisposable
{
    // 单例句柄复用整个进程的生命周期，不要每次换 index 都 new 一个
    // （new + dispose 太频繁 native 那边会崩，教训）
    private readonly object _lock = new();
    private VideoCapture? _cap;
    public int Index { get; private set; } = -1;
    public string? LastError { get; private set; }

    // 旧版用的 EMGU CV，API 限制太大换掉了，DShow 在 Windows 下能拿到设备名
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
        // FIXME: 不要在这里调 VideoCapture.Set(FrameWidth/Height)
        // DSHOW 后端部分驱动会触发原生 AV（AV 0xc0000005），try/catch 都拦不住。先用设备默认分辨率。
        lock (_lock)
        {
            try
            {
                if (_cap == null)
                    _cap = new VideoCapture();
                try { _cap.Release(); } catch { }
                Index = index;
                if (!_cap.Open(index, Api))
                {
                    LastError = "无法打开摄像头（索引 " + index + "）";
                    return false;
                }
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
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

    public static List<(int index, string name)> Enumerate()
    {
        var list = new List<(int index, string name)>();
#if WINDOWS
        try
        {
            var devices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);
            VideoCapture? probe = null;
            try
            {
                probe = new VideoCapture();
                for (int i = 0; i < devices.Length; i++)
                {
                    bool opened = false;
                    try { probe.Release(); opened = probe.Open(i, Api); }
                    catch { opened = false; }
                    if (opened)
                    {
                        var friendly = devices[i]?.Name;
                        list.Add((i, string.IsNullOrWhiteSpace(friendly) ? $"摄像头 {i}" : friendly!.Trim()));
                    }
                }
            }
            finally
            {
                if (probe != null)
                {
                    try { probe.Release(); } catch { }
                    try { GC.SuppressFinalize(probe); } catch { }
                }
            }
            if (list.Count > 0) return list;
        }
        catch { }
#endif

        VideoCapture? probe2 = null;
        try
        {
            probe2 = new VideoCapture();
            for (int i = 0; i < 8; i++)
            {
                bool opened = false;
                try { probe2.Release(); opened = probe2.Open(i, Api); }
                catch { opened = false; }
                if (opened) list.Add((i, $"摄像头 {i}"));
            }
        }
        catch { }
        finally
        {
            if (probe2 != null)
            {
                try { probe2.Release(); } catch { }
                try { GC.SuppressFinalize(probe2); } catch { }
            }
        }
        return list;
    }
}
