using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using PeekShield.Models;

namespace PeekShield.Services;

public class PeekShieldEngine : IDisposable
{
    public static PeekShieldEngine Instance { get; } = new PeekShieldEngine();

    public PeekShieldSettings _settings = PeekShieldSettings.Load();
    public PeekShieldSettings Settings => _settings;

    private CameraService _cam = new();
    private FaceRecognizer _recognizer = new();
    private FaceVerifier _verifier = new();
    private FaceEngine _faceEngine = null!;
    private ForegroundWatcher _fg = new();
    private OverlayService _overlay = new();
    private TrayService? _tray;
    private HotkeyService? _hotkey;

    public event Action? OpenSettingsRequested;

    public bool IsTrayIcon => _tray != null;

    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private DateTime _lastPeekTime = DateTime.MinValue;
    private DateTime _lastSnapshotTime = DateTime.MinValue;

    public bool IsFaceReady => _recognizer.IsReady;
    public bool IsEnrolled => _verifier.IsEnrolled;
    public int EnrolledCount => _verifier.SampleCount;

    public CameraService Camera => _cam;
    public FaceEngine FaceEngine => _faceEngine;

    public void Initialize()
    {
        LoggerService.LogInfo("引擎启动（构建签名 " + PeekShieldSettings.BuildSignature + " 系统 " + Platform.OsLabel + "）");
        Directory.CreateDirectory(Platform.LogsDir);

        try
        {
            var sp = Path.Combine(Platform.ModelsDir, "shape_predictor_68_face_landmarks.dat");
            var rn = Path.Combine(Platform.ModelsDir, "dlib_face_recognition_resnet_model_v1.dat");
            _recognizer.Load(sp, rn);
        }
        catch (Exception ex)
        {
            LoggerService.LogInfo("dlib 模型加载异常：" + ex.Message);
        }

        _faceEngine = new FaceEngine(_recognizer, _verifier);

        try
        {
            _verifier.Load(Platform.EnrollDir);
        }
        catch (Exception ex)
        {
            LoggerService.LogInfo("已录入数据加载异常：" + ex.Message);
        }

        try { _fg.Start(); } catch (Exception ex) { LoggerService.LogInfo("前台监听启动失败：" + ex.Message); }

        if (!_verifier.IsEnrolled)
        {
            LoggerService.LogInfo("机主人脸尚未录入，检测循环不启动（先在主窗口录入一次）");
        }
        else if (_settings.EnableSmartPeek && !_settings.Paused)
        {
            StartLoop();
        }
        else
        {
            LoggerService.LogInfo("未启动检测循环（智能防窥=" + _settings.EnableSmartPeek + " 暂停=" + _settings.Paused + "）");
        }

        if (_settings.ShowTrayIcon && _tray == null)
        {
            try
            {
                _tray = new TrayService();
                _tray.OnTogglePause += () => { _settings.Paused = !_settings.Paused; _settings.Save(); };
                _tray.OnToggleManual += () => { _settings.ManualMode = !_settings.ManualMode; _settings.Save(); };
                _tray.OnOpenSettings += () => OpenSettingsRequested?.Invoke();
                _tray.OnExit += () => App.RequestExit();
                _tray.Start();
            }
            catch (Exception ex) { LoggerService.LogInfo("托盘启动失败：" + ex.Message); }
        }

        if (_settings.EnableHotkey && _hotkey == null)
        {
            try
            {
                _hotkey = new HotkeyService(_settings.HotkeyModifiers, _settings.HotkeyKey, () =>
                {
                    _settings.Paused = !_settings.Paused;
                    _settings.Save();
                });
            }
            catch (Exception ex) { LoggerService.LogInfo("全局热键启动失败：" + ex.Message); }
        }

        LoggerService.LogInfo("引擎初始化完成");
    }

    public bool EnrollFromCamera(int cameraIndex)
    {
        if (!_cam.IsOpen && !_cam.Open(cameraIndex)) return false;
        using var frame = new OpenCvSharp.Mat();
        if (!_cam.ReadFrame(frame)) return false;

        var faces = _faceEngine.Detect(frame, 1, false, false);
        if (faces.Count == 0) return false;
        var owner = faces.FirstOrDefault(f => f.HasEyes) ?? faces[0];
        if (!_verifier.AddSample(owner.Embedding)) return false;
        _verifier.Save(Platform.EnrollDir);
        LoggerService.LogInfo("新增一条人脸特征（总 " + _verifier.SampleCount + " 条）");
        return true;
    }

    private void StartLoop()
    {
        if (_loopTask != null) return;
        _cts = new CancellationTokenSource();
        _loopTask = Task.Run(() => RunLoop(_cts.Token));
    }

    private void StopLoop()
    {
        try { _cts?.Cancel(); } catch { }
        _cts = null;
        _loopTask = null;
    }

    private async Task RunLoop(CancellationToken ct)
    {
        if (!_cam.IsOpen && !_cam.Open(_settings.CameraIndex))
        {
            LoggerService.LogInfo("摄像头打开失败，循环退出：" + _cam.LastError);
            return;
        }
        LoggerService.LogInfo("检测循环开始（摄像头索引=" + _cam.Index + "）");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var frame = new OpenCvSharp.Mat();
                if (!_cam.ReadFrame(frame))
                {
                    await Task.Delay(500, ct);
                    continue;
                }
                _fg.RefreshForeground();

                var faces = _faceEngine.Detect(frame, _settings.Sensitivity, _settings.LowLightEnhance, _settings.MirrorPosterFilter);

                int strangerCount = faces.Count(f => !f.IsOwner && f.LookingAtScreen);
                if (strangerCount > 0 && _verifier.IsEnrolled)
                {
                    LoggerService.LogPeek(_settings, strangerCount);
                    if ((DateTime.Now - _lastSnapshotTime).TotalSeconds > 5)
                    {
                        _lastSnapshotTime = DateTime.Now;
                        LoggerService.SaveSnapshot(frame, _settings);
                    }
                    if ((DateTime.Now - _lastPeekTime).TotalSeconds > 5)
                    {
                        _lastPeekTime = DateTime.Now;
                        _overlay.HideAll();
                        if (_settings.ActionPopup) _overlay.ShowPopup(_settings.PeekAlertText, _settings);
                    }
                    if (_settings.ActionMinimize && IsProtectedForeground())
                        WindowGuard.MinimizeProcesses(_settings.ProtectedProcesses);
                }
                else if (strangerCount == 0)
                {
                    Dispatcher.UIThread.Post(() => _overlay.HideAll());
                }

                int fps = faces.Count > 0 ? 8 : 3;
                await Task.Delay(1000 / fps, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                LoggerService.LogInfo("检测循环异常：" + ex.Message);
                await Task.Delay(1000, ct);
            }
        }

        LoggerService.LogInfo("检测循环结束");
    }

    private bool IsProtectedForeground()
    {
        if (!_fg.IsSupported) return false;
        var proc = (_fg.ForegroundProcessName ?? "").ToLowerInvariant();
        if (string.IsNullOrEmpty(proc)) return false;
        var title = _fg.ForegroundWindowTitle ?? "";
        foreach (var p in _settings.ProtectedProcesses)
        {
            if (!p.Enabled) continue;
            var n = p.Name?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(n)) continue;
            var proc2 = proc.EndsWith(".exe") ? proc[..^4] : proc;
            var n2 = n.EndsWith(".exe") ? n[..^4] : n;
            if (proc2 == n2) return true;
        }
        foreach (var t in _settings.ProtectedWindowTitles)
        {
            if (!t.Enabled) continue;
            var n = t.Name ?? "";
            if (!string.IsNullOrWhiteSpace(n) && title.Contains(n, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public void Dispose()
    {
        try { StopLoop(); } catch { }
        try { _tray?.Stop(); } catch { }
        try { _hotkey?.Dispose(); } catch { }
        try { _cam.Dispose(); } catch { }
        try { _recognizer?.Dispose(); } catch { }
    }
}
