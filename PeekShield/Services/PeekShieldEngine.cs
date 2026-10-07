using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using Avalonia.Threading;
using PeekShield.Models;

namespace PeekShield.Services;

public enum EngineStatus
{
    Idle, Monitoring, Secure, Peek, Paused, Manual, NoCamera, NotEnrolled, ConsentRequired, Error
}

public class PeekShieldEngine
{
    public static readonly PeekShieldEngine Instance = new();

    private PeekShieldSettings _settings = new();
    private volatile FaceRecognizer? _recognizer;
    private FaceVerifier? _verifier;
    private readonly Dictionary<string, FaceVerifier> _faceAuthVerifiers = new();
    private WhitelistService? _whitelist;
    private volatile FaceEngine? _faceEngine;
    private Task<bool>? _faceEngineTask;
    private readonly object _faceLock = new();
    private readonly CameraService _camera = new();
    private readonly ForegroundWatcher _fg = new();
    private readonly OverlayService _overlay = new();
    private TrayService? _tray;
    private HotkeyService? _hotkey;
    private HotkeyService? _escHotkey;
    private CancellationTokenSource? _cts;
    private System.Threading.Timer? _autoBackupTimer;
    private Task? _loopTask;
    private readonly object _frameLock = new();
    private Mat? _latestFrame;

    private bool _peekActive;
    private int _faceCount;
    private bool _ownerPresent;
    private bool _loggedFrame;
    private int _consecutiveBlackFrames;
    private string _lastCameraErrorLog = "";
    private EngineStatus _status = EngineStatus.Idle;
    private string _cameraError = "";
    private int _previewRefCount;

    private DateTime _lastStatusLog = DateTime.MinValue;
    private int _lastLoggedFaceCount = -1;
    private bool _lastLoggedOwnerPresent;

    private readonly Queue<int> _faceCountHistory = new();
    private readonly Queue<bool> _ownerHistory = new();
    private readonly Queue<bool> _strangerHistory = new();
    private readonly Queue<int> _ownerCountHistory = new();
    private readonly Queue<int> _whitelistCountHistory = new();
    private readonly Queue<int> _strangerCountHistory = new();
    private int _stableOwnerCount;
    private int _stableWhitelistCount;
    private int _stableStrangerCount;
    private bool _multiFaceNoticeActive;
    private DateTime _lastMultiFaceNotice = DateTime.MinValue;
    private const int HistorySize = 5;

    private readonly List<StrangerRecord> _strangers = new();
    private const double StrangerMatchThreshold = 0.6;

    private class StrangerRecord
    {
        public float[] Embedding = Array.Empty<float>();
        public DateTime FirstSeen;
        public DateTime LastSeen;
        public int AlertCount;
        public DateTime LastAlertTime;
    }

    public event Action<EngineStatus>? StatusChanged;
    public event Action? SettingsChanged;
    public event Action? OpenSettingsRequested;
    public event Action? OpenSecurityRequested;
    public event Action? OpenPrivacyRequested;
    public event Action<Mat>? PreviewFrame;

    public PeekShieldSettings Settings => _settings;
    public EngineStatus Status => _status;
    public int FaceCount => _faceCount;
    public bool OwnerPresent => _ownerPresent;
    public int OwnerCount => _stableOwnerCount;
    public int WhitelistCount => _stableWhitelistCount;
    public int StrangerCount => _stableStrangerCount;
    public string FaceDetail
    {
        get
        {
            if (_faceCount <= 0) return "无人";
            return $"人脸 {_faceCount}（机主 {_stableOwnerCount} · 白名单 {_stableWhitelistCount} · 陌生人 {_stableStrangerCount}）";
        }
    }
    public bool IsPeekActive => _peekActive;
    public string CameraError => _cameraError;
    public bool IsEnrolled => _verifier?.IsEnrolled ?? false;
    public bool IsFaceUnlockEnrolled => IsEnrolled || _faceAuthVerifiers.Values.Any(v => v.IsEnrolled);
    public bool IsFaceAuthEnrolled(string id)
    {
        var entry = _settings.AuthMethods.FirstOrDefault(m => m.Id == id);
        if (entry?.Kind == AuthMethodKind.QuickFace) return IsEnrolled;
        return _faceAuthVerifiers.TryGetValue(id, out var v) && v.IsEnrolled;
    }
    public int FaceAuthSampleCount(string id)
    {
        var entry = _settings.AuthMethods.FirstOrDefault(m => m.Id == id);
        if (entry?.Kind == AuthMethodKind.QuickFace) return _verifier?.SampleCount ?? 0;
        return _faceAuthVerifiers.TryGetValue(id, out var v) ? v.SampleCount : 0;
    }

    public double LastMatchDistance => _verifier?.LastDistance ?? -1;
    public double LastMatchThreshold => _verifier?.LastThreshold ?? -1;

    private static string EnrollDir => Platform.EnrollDir;
    private static string FaceAuthDir(string id) => Path.Combine(EnrollDir, "faces", id);

    public void Initialize()
    {
        _settings = PeekShieldSettings.Load();
        SecurityService.Settings = _settings;
        _verifier = new FaceVerifier();
        _verifier.Load(EnrollDir);

        ReloadFaceAuthVerifiers();
        _settings.FaceUnlockEnabled = IsFaceUnlockEnrolled;

        _whitelist = new WhitelistService(_settings);
        _whitelist.Reload();

        _fg.Start();

        _overlay.Dismissed += OnOverlayDismissed;

        _tray = new TrayService();
        _tray.OnTogglePause += TogglePause;
        _tray.OnToggleManual += ToggleManual;
        _tray.OnOpenSettings += () => OpenSettingsRequested?.Invoke();
        _tray.OnPrivacy += () => OpenPrivacyRequested?.Invoke();
        _tray.OnSecurity += () => OpenSecurityRequested?.Invoke();
        _tray.OnHideTray += () =>
        {
            _settings.ShowTrayIcon = false;
            _settings.Save();
            _tray?.Hide();
        };
        _tray.OnExit += () => App.RequestExit();
        if (_settings.ShowTrayIcon) _tray.Start();

        SyncAutoStart();
        ApplyHotkey();
        ApplyEscHotkey();
        ApplyManualMode();
        SetupAutoBackup();

        LoggerService.CleanupOldData(_settings.AutoCleanupDays);

        LoggerService.LogInfo("引擎初始化完成（构建签名 " + BuildConstants.BuildSignature + " 系统 " + Platform.OsLabel + "）");
        PushStatus(StatusWhenIdle());

        if (_settings.EnableSmartPeek && !_settings.Paused && ConsentService.CanProcessFace(_settings))
            StartLoop();
    }

    public void ReloadFaceAuthVerifiers()
    {
        lock (_faceAuthVerifiers)
        {
            foreach (var v in _faceAuthVerifiers.Values) v.Dispose();
            _faceAuthVerifiers.Clear();
            foreach (var m in _settings.AuthMethods)
            {
                if (m.Kind != AuthMethodKind.Face) continue;
                var v = new FaceVerifier();
                v.Load(FaceAuthDir(m.Id));
                _faceAuthVerifiers[m.Id] = v;
            }
        }
    }

    private Task<bool> EnsureFaceEngineAsync()
    {
        lock (_faceLock)
        {
            if (_faceEngine != null && _faceEngine.IsFaceReady) return Task.FromResult(true);
            if (_faceEngineTask != null) return _faceEngineTask;
            _faceEngineTask = Task.Run(LoadFaceEngineCore);
            return _faceEngineTask;
        }
    }

    private bool LoadFaceEngineCore()
    {
        try
        {
            var modelsDir = Platform.ModelsDir;
            var spPath = Path.Combine(modelsDir, "shape_predictor_68_face_landmarks.dat");
            var netPath = Path.Combine(modelsDir, "dlib_face_recognition_resnet_model_v1.dat");
            if (!File.Exists(spPath) || !File.Exists(netPath))
            {
                _cameraError = "人脸识别模型文件缺失，请重新部署程序";
                LoggerService.LogInfo("Dlib 模型文件缺失：sp=" + spPath + " net=" + netPath);
                PushStatus(EngineStatus.Error);
                return false;
            }
            if (_recognizer == null) _recognizer = new FaceRecognizer();
            _recognizer.Load(spPath, netPath);
            _faceEngine = new FaceEngine(_recognizer, _verifier!);
            if (!_faceEngine.IsFaceReady)
            {
                _cameraError = "人脸识别模型未能加载，详见 logs/engine.log";
                LoggerService.LogInfo("人脸识别引擎未就绪：recognizer.IsReady=" + _recognizer.IsReady);
                PushStatus(EngineStatus.Error);
                return false;
            }
            LoggerService.LogInfo("人脸识别模型已按需加载完成（构建签名 " + BuildConstants.BuildSignature + "）");
            return true;
        }
        catch (Exception ex)
        {
            _cameraError = "人脸识别模型加载异常，详见 logs/engine.log";
            LoggerService.LogInfo("人脸识别引擎加载异常：" + ex);
            PushStatus(EngineStatus.Error);
            return false;
        }
    }

    public void StartLoop()
    {
        if (_loopTask != null && !_loopTask.IsCompleted) return;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _loopTask = Task.Run(() => LoopAsync(ct));
        if (_verifier != null && _verifier.IsEnrolled) _ = EnsureFaceEngineAsync();
    }

    public void AddPreviewRef()
    {
        Interlocked.Increment(ref _previewRefCount);
        StartLoop();
    }

    public void ReleasePreviewRef()
    {
        if (Interlocked.Decrement(ref _previewRefCount) < 0) _previewRefCount = 0;
        // 不再从 UI 线程同步 StopLoop：LoopAsync 自己会在下一轮检测到 !ShouldPreview && !ShouldMonitor
        // 并安全关闭摄像头。彻底避免 SelectNav 切换页面时因 Wait(2000) 阻塞 UI 线程导致卡死。
        // 需要立即释放摄像头的场景（RestartCamera/Dispose）会显式调用 StopLoop。
    }

    private bool ShouldPreview() => _previewRefCount > 0;

    public void StopLoop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _loopTask?.Wait(2000); } catch { }
        _loopTask = null;
        _cts?.Dispose();
        _cts = null;
        if (_camera.IsOpen) _camera.Close();
        lock (_frameLock)
        {
            _latestFrame?.Dispose();
            _latestFrame = null;
        }
    }

    // 从主循环共享的最新帧取一份独立克隆（调用方负责 Dispose）。摄像头未就绪时返回 null。
    private Mat? GrabFrame()
    {
        lock (_frameLock)
        {
            return _latestFrame == null || _latestFrame.Empty() ? null : _latestFrame.Clone();
        }
    }

    // 录入/验证等需要摄像头采集的协程统一入口：由主循环持有摄像头（避免 Close+Open 抖动），
    // 协程只从共享帧取图；返回时按是否需要监控决定是否停循环。
    private void EndCameraSession()
    {
        if (_previewRefCount == 0) return;
        ReleasePreviewRef();
    }

    public void RestartCamera()
    {
        StopLoop();
        _loggedFrame = false;
        if (ShouldMonitor() || ShouldPreview()) StartLoop();
    }

    public void SyncAutoStart()
    {
        try
        {
            if (AutoStartService.IsSupported)
                AutoStartService.SetEnabled(_settings.AutoStart);
        }
        catch { }
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        using var frame = new Mat();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_faceEngine == null || !_faceEngine.IsFaceReady) { await SafeDelay(1000, ct); continue; }
                bool shouldMonitor = ShouldMonitor();
                bool shouldPreview = ShouldPreview();
                if (!shouldMonitor && !shouldPreview)
                {
                    if (_camera.IsOpen) _camera.Close();
                    if (_peekActive) EndPeek();
                    PushStatus(StatusWhenIdle());
                    await SafeDelay(700, ct);
                    continue;
                }

                if (!_camera.IsOpen)
                {
                    if (!_camera.Open(_settings.CameraIndex))
                    {
                        _cameraError = _camera.LastError ?? "摄像头打开失败";
                        if (_cameraError != _lastCameraErrorLog)
                        {
                            LoggerService.LogInfo("摄像头打开失败：" + _cameraError + "（每 3 秒重试，不影响其他功能）");
                            _lastCameraErrorLog = _cameraError;
                        }
                        PushStatus(EngineStatus.NoCamera);
                        await SafeDelay(3000, ct);
                        continue;
                    }
                    _cameraError = "";
                    _lastCameraErrorLog = "";
                }

                if (!_camera.ReadFrame(frame) || frame.Empty())
                {
                    await SafeDelay(120, ct);
                    continue;
                }

                // 黑帧/垃圾帧保护：摄像头刚打开或被其它程序抢占时常返回全黑帧（亮度均值≈0）。
                // 注意：绝不在此关闭/重开摄像头——刚打开的摄像头前几帧全黑属正常自恢复，
                // 强行 Close+Open 会在部分不稳定驱动上触发 videoio 原生 AV（进程瞬间崩溃、无托管堆栈）。
                // 这里只跳过本帧、不喂给 dlib，连续 30 帧全黑才判定设备异常并进入 NoCamera 退避。
                double frameMean = Cv2.Mean(frame).Val0;
                if (frameMean < 3.0)
                {
                    _consecutiveBlackFrames++;
                    if (_consecutiveBlackFrames >= 30)
                    {
                        _cameraError = "摄像头持续返回空画面（可能被其它程序占用或驱动异常）";
                        PushStatus(EngineStatus.NoCamera);
                        _camera.Close();
                        _loggedFrame = false;
                        await SafeDelay(3000, ct);
                        _consecutiveBlackFrames = 0;
                        continue;
                    }
                    await SafeDelay(200, ct);
                    continue;
                }
                _consecutiveBlackFrames = 0;
                // 主循环是摄像头的唯一读取者。每帧把帧存入共享 _latestFrame（引擎持有、录入/验证协程从中取帧），
                // 并克隆一份给预览。这样录入/验证不再 Close+Open 摄像头，避免部分驱动触发 videoio 原生 AV。
                // 注意：_latestFrame 与预览帧必须是各自独立的克隆——预览 handler 会 dispose 自己的帧。
                var shared = frame.Clone();
                lock (_frameLock)
                {
                    _latestFrame?.Dispose();
                    _latestFrame = shared;
                }
                PreviewFrame?.Invoke(frame.Clone());

                if (!_loggedFrame)
                {
                    LoggerService.LogInfo($"摄像头就绪，帧尺寸={frame.Width}x{frame.Height}，人脸模型就绪={_faceEngine?.IsFaceReady}");
                    _loggedFrame = true;
                }

                if (shouldMonitor)
                {
                    List<FaceInfo> faces = _faceEngine!.Detect(frame, _settings.Sensitivity, _settings.LowLightEnhance, _settings.MirrorPosterFilter, _settings.EnableGazeDetection);
                    if (_settings.WhitelistEnabled && _whitelist != null)
                    {
                        double wth = FaceEngine.OwnerMatchThreshold(_settings.Sensitivity);
                        foreach (var f in faces)
                        {
                            if (f.IsOwner || f.Embedding.Length != FaceVerifier.Dim) continue;
                            var nm = _whitelist.Match(f.Embedding, wth);
                            if (nm != null) { f.IsWhitelisted = true; f.WhitelistName = nm; }
                        }
                    }
                    _fg.RefreshForeground();
                    Evaluate(faces, frame);

                    int fps = faces.Count > 0 ? 12 : 2;
                    await SafeDelay(1000 / fps, ct);
                }
                else
                {
                    await SafeDelay(33, ct);
                }
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested) break;
                LoggerService.LogInfo("监控循环异常（已自动继续）：" + ex.Message);
                try { await SafeDelay(1000, ct); } catch { }
            }
        }
    }

    private static async Task SafeDelay(int ms, CancellationToken ct)
    {
        try { await Task.Delay(ms, ct); } catch { }
    }

    private bool ShouldMonitor()
    {
        if (!ConsentService.CanProcessFace(_settings)) return false;
        if (!_settings.EnableSmartPeek) return false;
        if (_settings.Paused) return false;
        if (_settings.ManualMode) return false;
        if (_fg.IsLocked) return false;
        if (_verifier == null || !_verifier.IsEnrolled) return false;
        if (_fg.IsSupported && _settings.OnlyProtectForeground && _settings.ProtectedProcesses.Count > 0 && !IsProtectedForeground())
            return false;
        return true;
    }

    private EngineStatus StatusWhenIdle()
    {
        if (_settings.Paused) return EngineStatus.Paused;
        if (_settings.ManualMode) return EngineStatus.Manual;
        if (!ConsentService.CanProcessFace(_settings)) return EngineStatus.ConsentRequired;
        if (_verifier == null || !_verifier.IsEnrolled) return EngineStatus.NotEnrolled;
        return EngineStatus.Monitoring;
    }

    private bool IsProtectedForeground()
    {
        var name = _fg.ForegroundProcessName;
        if (!string.IsNullOrEmpty(name))
        {
            var target = NormalizeProcessName(name);
            if (_settings.ProtectedProcesses.Any(p => p.Enabled && string.Equals(NormalizeProcessName(p.Name), target, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        var title = _fg.ForegroundWindowTitle;
        if (!string.IsNullOrWhiteSpace(title))
        {
            return _settings.ProtectedWindowTitles.Any(k =>
                k.Enabled && !string.IsNullOrWhiteSpace(k.Name) &&
                title.Contains(k.Name, StringComparison.OrdinalIgnoreCase));
        }
        return false;
    }

    private bool HasAlertableStranger(List<FaceInfo> strangers)
    {
        CleanupStrangers();
        int limit = _settings.StrangerAlertLimit;
        int coolMin = _settings.StrangerAlertCooldownMinutes;
        bool any = false;
        foreach (var face in strangers)
        {
            var rec = FindOrCreateStranger(face.Embedding);
            if (rec.AlertCount >= limit)
            {
                if ((DateTime.Now - rec.LastAlertTime).TotalMinutes >= coolMin)
                    rec.AlertCount = 0;
                else
                    continue;
            }
            any = true;
        }
        return any;
    }

    private void RegisterStrangerAlert(List<FaceInfo> strangers)
    {
        CleanupStrangers();
        foreach (var face in strangers)
        {
            var rec = FindOrCreateStranger(face.Embedding);
            rec.AlertCount++;
            rec.LastAlertTime = DateTime.Now;
            rec.LastSeen = DateTime.Now;
        }
    }

    private StrangerRecord FindOrCreateStranger(float[] embedding)
    {
        double best = double.MaxValue;
        StrangerRecord? match = null;
        foreach (var r in _strangers)
        {
            double d = EmbDist(r.Embedding, embedding);
            if (d < best) { best = d; match = r; }
        }
        if (match != null && best < StrangerMatchThreshold)
        {
            match.LastSeen = DateTime.Now;
            return match;
        }
        var rec = new StrangerRecord
        {
            Embedding = (float[])embedding.Clone(),
            FirstSeen = DateTime.Now,
            LastSeen = DateTime.Now,
            AlertCount = 0,
            LastAlertTime = DateTime.Now
        };
        _strangers.Add(rec);
        return rec;
    }

    private void CleanupStrangers()
    {
        int coolMin = _settings.StrangerAlertCooldownMinutes;
        var cutoff = DateTime.Now.AddMinutes(-Math.Max(coolMin, 30));
        _strangers.RemoveAll(r => r.LastSeen < cutoff);
    }

    public void ClearStrangerRecords()
    {
        _strangers.Clear();
        LoggerService.LogInfo("陌生人提醒记录已手动清空");
    }

    public void PreviewPopup()
    {
        _overlay.HideAll();
        if (_peekActive) return;
        _overlay.ShowPopup(PeekAlertText(), _settings);
        Task.Run(async () =>
        {
            await Task.Delay(2200);
            _overlay.HideAll();
        });
    }

    private string PeekAlertText()
    {
        var t = _settings.PeekAlertText;
        return string.IsNullOrWhiteSpace(t) ? PeekShieldSettings.DefaultPeekAlertText : t.Trim();
    }

    private static string NormalizeProcessName(string name)
    {
        if (name.Length > 4 && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return name[..^4];
        return name;
    }

    private void Evaluate(List<FaceInfo> faces, Mat frame)
    {
        int count = faces.Count;
        bool owner = faces.Any(f => f.IsOwner);
        bool strangerLooking = faces.Any(f => !f.IsOwner && !f.IsWhitelisted && f.LookingAtScreen);

        int oc = faces.Count(f => f.IsOwner);
        int wc = faces.Count(f => f.IsWhitelisted && !f.IsOwner);
        int sc = faces.Count(f => !f.IsOwner && !f.IsWhitelisted);
        PushHistory(count, owner, strangerLooking, oc, wc, sc);
        int stableCount = StableFaceCount();
        bool stableOwner = StableOwner();
        bool stableStranger = StableStranger();
        _stableOwnerCount = StableMax(_ownerCountHistory);
        _stableWhitelistCount = StableMax(_whitelistCountHistory);
        _stableStrangerCount = StableMax(_strangerCountHistory);

        _faceCount = stableCount;
        _ownerPresent = stableOwner;

        var strangerFaces = faces.Where(f => !f.IsOwner && !f.IsWhitelisted && f.LookingAtScreen && f.Embedding.Length == FaceVerifier.Dim).ToList();
        bool hasAlertableStranger = stableCount >= 1 && stableStranger && HasAlertableStranger(strangerFaces);

        if (hasAlertableStranger && !_peekActive) StartPeek(frame, strangerFaces);
        else if (!hasAlertableStranger && _peekActive) EndPeek();

        if (!_peekActive && _settings.EnableMultiFaceAlert)
        {
            bool multi = stableCount >= 2 && (_stableWhitelistCount > 0 || _stableStrangerCount > 0);
            if (multi)
            {
                if (!_multiFaceNoticeActive || (DateTime.Now - _lastMultiFaceNotice).TotalSeconds >= 8)
                {
                    ShowMultiFaceNotice();
                    _multiFaceNoticeActive = true;
                    _lastMultiFaceNotice = DateTime.Now;
                }
            }
            else
            {
                _multiFaceNoticeActive = false;
            }
        }

        if (!hasAlertableStranger)
        {
            var st = stableCount == 0 ? EngineStatus.Monitoring
                : (stableOwner && stableCount == 1 ? EngineStatus.Secure : EngineStatus.Monitoring);
            PushStatus(st);
        }

        double dist = _verifier?.LastDistance ?? -1;
        if (dist > 1000) dist = -1;
        double thr = _verifier?.LastThreshold ?? -1;
        bool changed = stableCount != _lastLoggedFaceCount || stableOwner != _lastLoggedOwnerPresent;
        bool periodic = (DateTime.Now - _lastStatusLog).TotalSeconds > 1.5;
        if (periodic || changed)
        {
            _lastStatusLog = DateTime.Now;
            _lastLoggedFaceCount = stableCount;
            _lastLoggedOwnerPresent = stableOwner;
        }

    }

    private void PushHistory(int count, bool owner, bool stranger, int ownerCount, int whitelistCount, int strangerCount)
    {
        _faceCountHistory.Enqueue(count);
        _ownerHistory.Enqueue(owner);
        _strangerHistory.Enqueue(stranger);
        _ownerCountHistory.Enqueue(ownerCount);
        _whitelistCountHistory.Enqueue(whitelistCount);
        _strangerCountHistory.Enqueue(strangerCount);
        while (_faceCountHistory.Count > HistorySize) _faceCountHistory.Dequeue();
        while (_ownerHistory.Count > HistorySize) _ownerHistory.Dequeue();
        while (_strangerHistory.Count > HistorySize) _strangerHistory.Dequeue();
        while (_ownerCountHistory.Count > HistorySize) _ownerCountHistory.Dequeue();
        while (_whitelistCountHistory.Count > HistorySize) _whitelistCountHistory.Dequeue();
        while (_strangerCountHistory.Count > HistorySize) _strangerCountHistory.Dequeue();
    }

    private int StableFaceCount() => _faceCountHistory.Count == 0 ? 0 : _faceCountHistory.Max();
    private bool StableOwner() => _ownerHistory.Count(h => h) >= 3;
    private bool StableStranger() => _strangerHistory.Count(h => h) >= 3;
    private static int StableMax(Queue<int> q) => q.Count == 0 ? 0 : q.Max();

    private void StartPeek(Mat frame, List<FaceInfo> strangers)
    {
        _peekActive = true;
        _multiFaceNoticeActive = false;
        RegisterStrangerAlert(strangers);
        CaptureStrangers(frame, strangers);

        if (_settings.EnableTopBanner || _settings.ActionPopup)
            _overlay.ShowPopup(PeekAlertText(), _settings);

        if (_settings.EnableFullscreenProtect && (!_fg.IsSupported || IsProtectedForeground()))
            _overlay.ShowProtect();

        if (_settings.ActionSound) AudioAlert.Play();
        if (_settings.ActionMinimize) WindowGuard.MinimizeProcesses(_settings.ProtectedProcesses);

        LoggerService.LogPeek(_settings, _faceCount);
        LoggerService.SaveSnapshot(frame, _settings);
        PushStatus(EngineStatus.Peek);
    }

    private void EndPeek()
    {
        _peekActive = false;
        _multiFaceNoticeActive = false;
        _overlay.HideAll();
        if (_settings.RestoreOnSafe) WindowGuard.RestoreProcesses(_settings.ProtectedProcesses);
        PushStatus(_ownerPresent ? EngineStatus.Secure : EngineStatus.Monitoring);
    }

    private string SilentCaptureDir =>
        string.IsNullOrWhiteSpace(_settings.SilentCaptureDir)
            ? Path.Combine(Platform.LogsDir, "evidence")
            : _settings.SilentCaptureDir;

    // 批次2：检测到偷窥时静默裁剪陌生人面部并存盘（仅陌生人，不保存机主/白名单）
    private void CaptureStrangers(Mat frame, List<FaceInfo> strangers)
    {
        if (!_settings.SilentCaptureStrangers) return;
        if (frame == null || frame.Empty() || strangers == null || strangers.Count == 0) return;
        try
        {
            var dir = SilentCaptureDir;
            Directory.CreateDirectory(dir);
            int idx = 0;
            foreach (var f in strangers)
            {
                if (f == null) continue;
                var r = f.Rect;
                if (r.Width <= 0 || r.Height <= 0) continue;
                int pad = (int)(Math.Min(r.Width, r.Height) * 0.3);
                int x = Math.Max(0, r.X - pad);
                int y = Math.Max(0, r.Y - pad);
                int w = Math.Min(frame.Width - x, r.Width + pad * 2);
                int h = Math.Min(frame.Height - y, r.Height + pad * 2);
                if (w <= 0 || h <= 0) continue;
                using var crop = new Mat(frame, new OpenCvSharp.Rect(x, y, w, h));
                var suffix = idx == 0 ? "" : $"_{idx}";
                var name = $"stranger_{DateTime.Now:yyyyMMdd_HHmmss_fff}{suffix}.png";
                Cv2.ImWrite(Path.Combine(dir, name), crop);
                idx++;
            }
            if (idx > 0) LoggerService.LogInfo($"静默取证：保存陌生人裁剪图 {idx} 张到 {dir}");
        }
        catch (Exception ex)
        {
            LoggerService.LogInfo("静默取证异常：" + ex.Message);
        }
    }

    // 批次2：自动定时备份 .kyd 到指定目录
    private void SetupAutoBackup()
    {
        try
        {
            _autoBackupTimer?.Dispose();
            _autoBackupTimer = null;
            if (!_settings.AutoBackupEnabled) return;
            int hours = Math.Clamp(_settings.AutoBackupIntervalHours, 1, 8760);
            long ms = (long)hours * 3600 * 1000;
            _autoBackupTimer = new System.Threading.Timer(_ => DoAutoBackup(), null, ms, ms);
            LoggerService.LogInfo($"自动备份已启用：间隔 {hours} 小时，目录={(string.IsNullOrWhiteSpace(_settings.AutoBackupDir) ? Platform.AppDataDir : _settings.AutoBackupDir)}");
        }
        catch (Exception ex)
        {
            LoggerService.LogInfo("自动备份定时器初始化异常：" + ex.Message);
        }
    }

    private void DoAutoBackup()
    {
        try
        {
            if (!_settings.AutoBackupEnabled) return;
            var dir = string.IsNullOrWhiteSpace(_settings.AutoBackupDir)
                ? Platform.AppDataDir
                : _settings.AutoBackupDir;
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "auto_backup.kyd");
            var req = new BackupExportRequest
            {
                IncludeSettings = true,
                IncludeOwnerFace = true,
                IncludeWhitelistFaces = _settings.WhitelistEnabled,
                IncludeAuthFaces = _settings.AuthMethods.Any(m => m.Kind == AuthMethodKind.Face),
                FilePassword = string.IsNullOrEmpty(_settings.AutoBackupPassword) ? null : _settings.AutoBackupPassword
            };
            var res = ConfigBackupService.Export(path, req);
            if (res.Success)
                LoggerService.LogInfo($"自动备份完成：{path}（加密={res.Encrypted}）");
            else
                LoggerService.LogInfo("自动备份失败：" + res.Error);
        }
        catch (Exception ex)
        {
            LoggerService.LogInfo("自动备份异常：" + ex.Message);
        }
    }

    public void RunAutoBackupNow() => DoAutoBackup();

    public string ResolveSilentCaptureDir() => SilentCaptureDir;

    private void ShowMultiFaceNotice()
    {
        var text = $"⚠ 检测到多人同屏（共 {_faceCount} 人：机主 {_stableOwnerCount} · 白名单 {_stableWhitelistCount} · 陌生人 {_stableStrangerCount}）";
        _overlay.ShowTransientPopup(text, _settings, 2600);
        LoggerService.LogInfo("多人同屏提醒：" + text);
    }

    private void OnOverlayDismissed()
    {
        if (_peekActive) EndPeek();
    }

    public void ApplySettings()
    {
        if (_tray != null)
        {
            if (_settings.ShowTrayIcon) _tray.Start();
            else _tray.Hide();
            _tray.SetPauseLabel(_settings.Paused);
            _tray.SetManualLabel(_settings.ManualMode);
        }
        ApplyHotkey();
        ApplyEscHotkey();
        ApplyManualMode();
        SyncAutoStart();
        SetupAutoBackup();

        if (_settings.EnableSmartPeek && !_settings.Paused && ConsentService.CanProcessFace(_settings))
            StartLoop();
        else
            StopLoop();
    }

    private void ApplyManualMode()
    {
        if (_settings.Paused)
        {
            _overlay.HideAll();
            return;
        }
        if (_settings.ManualMode && _settings.EnableSmartPeek)
        {
            if (_peekActive) EndPeek();
            _overlay.ShowFog("手动防窥模式已开启（侧面视角已变暗模糊）");
            PushStatus(EngineStatus.Manual);
            return;
        }
        _overlay.HideAll();
    }

    private void ApplyHotkey()
    {
        _hotkey?.Dispose();
        _hotkey = null;
        if (!_settings.EnableHotkey) return;
        _hotkey = new HotkeyService(_settings.HotkeyModifiers, _settings.HotkeyKey, OnHotkeyPressed);
        LoggerService.LogInfo($"快捷键已注册：{_settings.HotkeyModifiers}+{_settings.HotkeyKey}，用于一键暂停/恢复防护");
    }

    private void ApplyEscHotkey()
    {
        _escHotkey?.Dispose();
        _escHotkey = new HotkeyService("", "Escape", OnEscPressed);
    }

    private void OnEscPressed()
    {
        if (!_settings.ManualMode) return;
        ExitManual();
    }

    public void ExitManual()
    {
        if (!_settings.ManualMode) return;
        _settings.ManualMode = false;
        _settings.Save();
        ApplySettings();
        SettingsChanged?.Invoke();
        _tray?.ShowBalloon("窥屿盾", "已退出手动防窥");
    }

    private void OnHotkeyPressed()
    {
        if (!_settings.EnableSmartPeek)
        {
            LoggerService.LogInfo("快捷键触发：智能防窥总开关未开启，无动作");
            _tray?.ShowBalloon("窥屿盾", "智能防窥总开关未开启，快捷键无效");
            return;
        }
        if (_settings.HotkeyAction == 1)
        {
            ToggleManual();
            var on = _settings.ManualMode ? "已开启" : "已关闭";
            LoggerService.LogInfo($"快捷键切换手动防窥：{on}");
            _tray?.ShowBalloon("窥屿盾", $"手动防窥{on}");
        }
        else
        {
            TogglePause();
            var state = _settings.Paused ? "已暂停" : "已恢复";
            LoggerService.LogInfo($"快捷键切换防护状态：{state}");
            _tray?.ShowBalloon("窥屿盾", $"智能防窥{state}");
        }
    }

    public void TogglePause()
    {
        _settings.Paused = !_settings.Paused;
        _settings.Save();
        ApplySettings();
        SettingsChanged?.Invoke();
    }

    public void ToggleManual()
    {
        _settings.ManualMode = !_settings.ManualMode;
        _settings.Save();
        ApplySettings();
        SettingsChanged?.Invoke();
    }

    public void ToggleSmartPeek()
    {
        _settings.EnableSmartPeek = !_settings.EnableSmartPeek;
        _settings.Save();
        ApplySettings();
        SettingsChanged?.Invoke();
    }

    public void SetEnabled(bool enabled)
    {
        _settings.EnableSmartPeek = enabled;
        _settings.Save();
        ApplySettings();
        SettingsChanged?.Invoke();
    }

    public async Task<bool> EnrollAsync(int samples = 10, Action<int>? progress = null)
    {
        if (!ConsentService.CanProcessFace(_settings)) return DenyEnroll();
        AddPreviewRef();
        if (!await EnsureFaceEngineAsync())
        {
            _cameraError = "人脸识别模型加载失败，无法录入人脸";
            LoggerService.LogInfo("录入前模型按需加载失败");
            EndCameraSession();
            return false;
        }
        bool wasEnrolled = _verifier!.IsEnrolled;
        _verifier.Clear();
        bool ok = false;
        var seen = new List<float[]>();
        try
        {
            int collected = 0;
            int attempts = 0;
            for (int i = 0; i < samples + 30 && collected < samples; i++)
            {
                Mat? snap = GrabFrame();
                if (snap == null || snap.Empty()) { snap?.Dispose(); await SafeDelay(150, default); continue; }
                var faces = _recognizer!.Detect(snap);
                snap.Dispose();
                if (faces.Count == 0) { await SafeDelay(200, default); continue; }
                attempts++;
                var emb = faces[0].Embedding;
                bool dup = seen.Any(s => EmbDist(emb, s) < 0.25);
                if (!dup)
                {
                    seen.Add(emb);
                    if (_verifier.AddSample(emb)) { collected++; progress?.Invoke(collected); }
                }
                await SafeDelay(300, default);
            }
            ok = collected >= 3;
            if (ok)
            {
                _verifier.Save(EnrollDir);
                _settings.IsEnrolled = true;
                LoggerService.LogInfo($"摄像头录入成功：尝试={attempts} 接受={collected} 离散度={_verifier.SelfGap:F3} 灵敏度={_settings.Sensitivity}");
            }
            else
            {
                RestorePriorEnrollment(wasEnrolled);
                LoggerService.LogInfo($"摄像头录入失败：尝试={attempts} 接受={collected}（需至少 3 张合格样本{(wasEnrolled ? "，已恢复此前录入" : "")}）");
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogInfo("录入过程异常：" + ex);
            RestorePriorEnrollment(wasEnrolled);
            ok = false;
        }
        finally
        {
            ResetHistory();
            _settings.Save();
            ReleasePreviewRef();
            PushStatus(_settings.IsEnrolled ? EngineStatus.Monitoring : EngineStatus.NotEnrolled);
        }
        return ok;
    }

    private bool DenyEnroll()
    {
        _cameraError = "未取得人脸处理的单独同意，无法录入人脸";
        LoggerService.LogInfo("录入被拒绝：未取得人脸处理单独同意");
        PushStatus(EngineStatus.ConsentRequired);
        return false;
    }

    private static double EmbDist(float[] a, float[] b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++) { double d = a[i] - b[i]; s += d * d; }
        return Math.Sqrt(s);
    }

    private void RestorePriorEnrollment(bool wasEnrolled)
    {
        if (wasEnrolled)
        {
            try { _verifier!.Load(EnrollDir); } catch { }
            _settings.IsEnrolled = _verifier!.IsEnrolled;
        }
        else
        {
            _verifier!.Clear();
            _settings.IsEnrolled = false;
        }
    }

    public async Task<bool> EnrollFromPhotoAsync(string imagePath, Action<int>? progress = null)
    {
        if (!ConsentService.CanProcessFace(_settings)) return DenyEnroll();
        if (!await EnsureFaceEngineAsync())
        {
            _cameraError = "人脸识别模型加载失败，无法录入人脸";
            LoggerService.LogInfo("照片录入前模型按需加载失败");
            return false;
        }
        bool wasEnrolled = _verifier!.IsEnrolled;
        _verifier.Clear();
        bool ok = false;
        try
        {
            if (!File.Exists(imagePath)) { RestorePriorEnrollment(wasEnrolled); return false; }
            using var img = Cv2.ImRead(imagePath, ImreadModes.Color);
            if (img.Empty()) { RestorePriorEnrollment(wasEnrolled); return false; }

            int collected = 0;
            var faces = _recognizer!.Detect(img);
            if (faces.Count == 0)
            {
                RestorePriorEnrollment(wasEnrolled);
                LoggerService.LogInfo("照片录入失败：未在照片中检测到人脸，请换一张正脸清晰照片");
                return false;
            }
            if (_verifier.AddSample(faces[0].Embedding)) collected++;
            using var flip = new Mat(); Cv2.Flip(img, flip, FlipMode.Y);
            var f2 = _recognizer.Detect(flip);
            if (f2.Count > 0 && _verifier.AddSample(f2[0].Embedding)) collected++;

            ok = collected >= 1;
            if (ok)
            {
                _verifier.Save(EnrollDir);
                _settings.IsEnrolled = true;
                LoggerService.LogInfo($"照片录入成功：接受={collected} 离散度={_verifier.SelfGap:F3} 灵敏度={_settings.Sensitivity}");
            }
            else
            {
                RestorePriorEnrollment(wasEnrolled);
                LoggerService.LogInfo($"照片录入失败：接受={collected}（请换一张正脸清晰照片{(wasEnrolled ? "，已恢复此前录入" : "")}）");
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogInfo("录入过程异常：" + ex);
            RestorePriorEnrollment(wasEnrolled);
            ok = false;
        }
        finally
        {
            ResetHistory();
            _settings.Save();
            PushStatus(_settings.IsEnrolled ? EngineStatus.Monitoring : EngineStatus.NotEnrolled);
        }
        return ok;
    }

    public void ClearEnrollment()
    {
        _verifier?.Clear();
        try
        {
            if (Directory.Exists(EnrollDir))
                foreach (var f in Directory.GetFiles(EnrollDir)) File.Delete(f);
        }
        catch { }
        _settings.IsEnrolled = false;
        _settings.Save();
        ResetHistory();
        PushStatus(_peekActive ? EngineStatus.Peek : EngineStatus.NotEnrolled);
    }

    public async Task<bool> EnrollFaceAuthAsync(string id, int samples = 10, Action<int>? progress = null)
    {
        var entry = _settings.AuthMethods.FirstOrDefault(m => m.Id == id);
        if (entry?.Kind == AuthMethodKind.QuickFace) return IsEnrolled;
        if (!ConsentService.CanProcessFace(_settings)) return DenyEnroll();
        AddPreviewRef();
        if (!await EnsureFaceEngineAsync())
        {
            _cameraError = "人脸识别模型加载失败，无法录入人脸";
            LoggerService.LogInfo("人脸认证录入前模型按需加载失败");
            EndCameraSession();
            return false;
        }
        FaceVerifier verifier;
        lock (_faceAuthVerifiers)
        {
            if (!_faceAuthVerifiers.TryGetValue(id, out verifier))
            {
                verifier = new FaceVerifier();
                _faceAuthVerifiers[id] = verifier;
            }
        }
        bool wasEnrolled = verifier.IsEnrolled;
        verifier.Clear();
        bool ok = false;
        var seen = new List<float[]>();
        try
        {
            int collected = 0;
            int attempts = 0;
            for (int i = 0; i < samples + 30 && collected < samples; i++)
            {
                Mat? snap = GrabFrame();
                if (snap == null || snap.Empty()) { snap?.Dispose(); await SafeDelay(150, default); continue; }
                var faces = _recognizer!.Detect(snap);
                snap.Dispose();
                if (faces.Count == 0) { await SafeDelay(200, default); continue; }
                attempts++;
                var emb = faces[0].Embedding;
                bool dup = seen.Any(s => EmbDist(emb, s) < 0.25);
                if (!dup)
                {
                    seen.Add(emb);
                    if (verifier.AddSample(emb)) { collected++; progress?.Invoke(collected); }
                }
                await SafeDelay(300, default);
            }
            ok = collected >= 3;
            if (ok)
            {
                verifier.Save(FaceAuthDir(id));
                _settings.FaceUnlockEnabled = IsFaceUnlockEnrolled;
                LoggerService.LogInfo($"人脸认证录入成功：条目={id} 尝试={attempts} 接受={collected} 离散度={verifier.SelfGap:F3}");
            }
            else
            {
                RestoreFaceAuth(id, wasEnrolled);
                LoggerService.LogInfo($"人脸认证录入失败：条目={id} 尝试={attempts} 接受={collected}（需至少 3 张合格样本{(wasEnrolled ? "，已恢复此前录入" : "")}）");
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogInfo("人脸认证录入过程异常：" + ex);
            RestoreFaceAuth(id, wasEnrolled);
            ok = false;
        }
        finally
        {
            _settings.Save();
            ReleasePreviewRef();
        }
        return ok;
    }

    private void RestoreFaceAuth(string id, bool wasEnrolled)
    {
        if (_faceAuthVerifiers.TryGetValue(id, out var verifier))
        {
            if (wasEnrolled)
            {
                try { verifier.Load(FaceAuthDir(id)); } catch { }
            }
            else
            {
                verifier.Clear();
                try
                {
                    var dir = FaceAuthDir(id);
                    if (Directory.Exists(dir)) Directory.Delete(dir, true);
                }
                catch { }
            }
        }
        _settings.FaceUnlockEnabled = IsFaceUnlockEnrolled;
    }

    public async Task<bool> EnrollFaceAuthFromPhotoAsync(string id, string imagePath, Action<int>? progress = null)
    {
        var entry = _settings.AuthMethods.FirstOrDefault(m => m.Id == id);
        if (entry?.Kind == AuthMethodKind.QuickFace) return IsEnrolled;
        if (!ConsentService.CanProcessFace(_settings)) return DenyEnroll();
        if (!await EnsureFaceEngineAsync())
        {
            _cameraError = "人脸识别模型加载失败，无法录入人脸";
            LoggerService.LogInfo("人脸认证照片录入前模型按需加载失败");
            return false;
        }
        FaceVerifier verifier;
        lock (_faceAuthVerifiers)
        {
            if (!_faceAuthVerifiers.TryGetValue(id, out verifier))
            {
                verifier = new FaceVerifier();
                _faceAuthVerifiers[id] = verifier;
            }
        }
        bool wasEnrolled = verifier.IsEnrolled;
        verifier.Clear();
        bool ok = false;
        try
        {
            if (!File.Exists(imagePath)) { RestoreFaceAuth(id, wasEnrolled); return false; }
            using var img = Cv2.ImRead(imagePath, ImreadModes.Color);
            if (img.Empty()) { RestoreFaceAuth(id, wasEnrolled); return false; }

            int collected = 0;
            var faces = _recognizer!.Detect(img);
            if (faces.Count == 0)
            {
                RestoreFaceAuth(id, wasEnrolled);
                LoggerService.LogInfo("人脸认证照片录入失败：未在照片中检测到人脸，请换一张正脸清晰照片");
                return false;
            }
            if (verifier.AddSample(faces[0].Embedding)) collected++;
            using var flip = new Mat(); Cv2.Flip(img, flip, FlipMode.Y);
            var f2 = _recognizer.Detect(flip);
            if (f2.Count > 0 && verifier.AddSample(f2[0].Embedding)) collected++;

            ok = collected >= 1;
            if (ok)
            {
                verifier.Save(FaceAuthDir(id));
                _settings.FaceUnlockEnabled = IsFaceUnlockEnrolled;
                LoggerService.LogInfo($"人脸认证照片录入成功：条目={id} 接受={collected} 离散度={verifier.SelfGap:F3}");
            }
            else
            {
                RestoreFaceAuth(id, wasEnrolled);
                LoggerService.LogInfo($"人脸认证照片录入失败：条目={id} 接受={collected}（请换一张正脸清晰照片{(wasEnrolled ? "，已恢复此前录入" : "")})");
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogInfo("人脸认证照片录入过程异常：" + ex);
            RestoreFaceAuth(id, wasEnrolled);
            ok = false;
        }
        finally
        {
            _settings.Save();
        }
        return ok;
    }

    public async Task<bool> VerifyFaceAuthAsync(string id, Action<string>? status = null, Action<OpenCvSharp.Mat>? onFrame = null)
    {
        var entry = _settings.AuthMethods.FirstOrDefault(m => m.Id == id);
        FaceVerifier? verifier = entry?.Kind == AuthMethodKind.QuickFace ? _verifier : null;
        if (verifier == null) _faceAuthVerifiers.TryGetValue(id, out verifier);
        if (verifier == null || !verifier.IsEnrolled) return false;
        if (!ConsentService.CanProcessFace(_settings)) return false;
        AddPreviewRef();
        if (!await EnsureFaceEngineAsync())
        {
            status?.Invoke("人脸识别模型加载失败，请使用密码");
            LoggerService.LogInfo("人脸认证验证前模型按需加载失败");
            EndCameraSession();
            return false;
        }
        bool ok = false;
        try
        {
            double th = FaceEngine.OwnerMatchThreshold(_settings.Sensitivity);
            int attempts = 0;
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(8) && !ok)
            {
                Mat? snap = GrabFrame();
                if (snap == null || snap.Empty()) { snap?.Dispose(); await SafeDelay(150, default); continue; }
                if (onFrame != null) { try { onFrame(snap.Clone()); } catch { } }
                var faces = _recognizer!.Detect(snap);
                snap.Dispose();
                if (faces.Count == 0) { await SafeDelay(200, default); continue; }
                attempts++;
                var (isOwner, dist) = verifier.Verify(faces[0].Embedding, th);
                status?.Invoke("人脸比对中…");
                if (isOwner) { ok = true; break; }
                await SafeDelay(200, default);
            }
            LoggerService.LogInfo($"人脸认证验证：条目={id} 尝试={attempts} 结果={(ok ? "通过" : "未匹配")} 阈值={th:F3} 耗时={sw.Elapsed.TotalSeconds:F1}s");
        }
        catch (Exception ex)
        {
            LoggerService.LogInfo("人脸认证验证异常：" + ex);
            ok = false;
        }
        finally
        {
            ReleasePreviewRef();
        }
        return ok;
    }

    public bool QuickVerifyAvailable =>
        _settings.PasswordEnabled &&
        _settings.AuthMethods.Any(m => m.Kind == AuthMethodKind.QuickFace) &&
        IsEnrolled &&
        ConsentService.CanProcessFace(_settings);

    public async Task<bool> TryAutoVerifyAsync(Action<string>? status = null)
    {
        if (!_settings.PasswordEnabled || !_settings.QuickVerifyEnabled) return false;
        if (!ConsentService.CanProcessFace(_settings)) return false;
        if (_verifier == null || !_verifier.IsEnrolled) return false;
        AddPreviewRef();
        if (!await EnsureFaceEngineAsync())
        {
            status?.Invoke("人脸识别模型加载失败，请使用密码");
            LoggerService.LogInfo("快捷验证前模型按需加载失败");
            EndCameraSession();
            return false;
        }
        bool ok = false;
        try
        {
            double th = FaceEngine.OwnerMatchThreshold(_settings.Sensitivity);
            int attempts = 0;
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(8) && !ok)
            {
                Mat? snap = GrabFrame();
                if (snap == null || snap.Empty()) { snap?.Dispose(); await SafeDelay(150, default); continue; }
                var faces = _recognizer!.Detect(snap);
                snap.Dispose();
                if (faces.Count == 0) { await SafeDelay(200, default); continue; }
                attempts++;
                var (isOwner, dist) = _verifier.Verify(faces[0].Embedding, th);
                status?.Invoke("正在识别机主…（无需操作）");
                if (isOwner) { ok = true; break; }
                await SafeDelay(200, default);
            }
            LoggerService.LogInfo($"快捷验证：尝试={attempts} 结果={(ok ? "通过" : "未匹配")} 阈值={th:F3} 耗时={sw.Elapsed.TotalSeconds:F1}s");
        }
        catch (Exception ex)
        {
            LoggerService.LogInfo("快捷验证异常：" + ex);
            ok = false;
        }
        finally
        {
            ReleasePreviewRef();
        }
        return ok;
    }

    public void ClearFaceAuth(string id)
    {
        var entry = _settings.AuthMethods.FirstOrDefault(m => m.Id == id);
        if (entry?.Kind == AuthMethodKind.QuickFace) return;
        if (_faceAuthVerifiers.TryGetValue(id, out var verifier)) verifier.Clear();
        try
        {
            var dir = FaceAuthDir(id);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch { }
        _settings.FaceUnlockEnabled = IsFaceUnlockEnrolled;
        _settings.Save();
    }

    public void ClearAllFaceAuth()
    {
        lock (_faceAuthVerifiers)
        {
            foreach (var v in _faceAuthVerifiers.Values) v.Dispose();
            _faceAuthVerifiers.Clear();
        }
        try
        {
            var dir = Path.Combine(EnrollDir, "faces");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch { }
        _settings.FaceUnlockEnabled = false;
        _settings.Save();
    }

    public string AddWhitelist(string name)
    {
        var e = new WhitelistEntry { Id = Guid.NewGuid().ToString(), Name = name, Enabled = true };
        _settings.Whitelist.Add(e);
        _settings.Save();
        _whitelist?.AddItem(e);
        return e.Id;
    }

    public void RemoveWhitelist(string id)
    {
        _whitelist?.RemoveItem(id);
    }

    public void RenameWhitelist(string id, string name)
    {
        _whitelist?.Rename(id, name);
    }

    public int WhitelistSampleCount(string id)
    {
        return _whitelist?.GetItem(id)?.Verifier.SampleCount ?? 0;
    }

    public async Task<bool> EnrollWhitelistAsync(string id, int samples = 12, Action<int>? progress = null)
    {
        if (!ConsentService.CanProcessFace(_settings)) return DenyEnroll();
        if (!_settings.WhitelistEnabled) return false;
        var item = _whitelist?.GetItem(id);
        if (item == null) return false;
        AddPreviewRef();
        if (!await EnsureFaceEngineAsync())
        {
            _cameraError = "人脸识别模型加载失败，无法录入白名单人脸";
            LoggerService.LogInfo("白名单录入前模型按需加载失败");
            EndCameraSession();
            return false;
        }
        item.Verifier.Clear();
        bool ok = false;
        var seen = new List<float[]>();
        try
        {
            int collected = 0;
            int attempts = 0;
            for (int i = 0; i < samples + 30 && collected < samples; i++)
            {
                Mat? snap = GrabFrame();
                if (snap == null || snap.Empty()) { snap?.Dispose(); await SafeDelay(150, default); continue; }
                var faces = _recognizer!.Detect(snap);
                snap.Dispose();
                if (faces.Count == 0) { await SafeDelay(200, default); continue; }
                attempts++;
                var emb = faces[0].Embedding;
                bool dup = seen.Any(s => EmbDist(emb, s) < 0.25);
                if (!dup)
                {
                    seen.Add(emb);
                    if (item.Verifier.AddSample(emb)) { collected++; progress?.Invoke(collected); }
                }
                await SafeDelay(300, default);
            }
            ok = collected >= 3;
            if (ok)
            {
                _whitelist!.Persist(id);
                LoggerService.LogInfo($"白名单录入成功：名称={item.Entry.Name} 接受={collected} 离散度={item.Verifier.SelfGap:F3}");
            }
            else
            {
                item.Verifier.Clear();
                _whitelist!.DeleteData(id);
                LoggerService.LogInfo($"白名单录入失败：名称={item.Entry.Name} 接受={collected}（需至少 3 张合格样本）");
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogInfo("白名单录入过程异常：" + ex);
            item.Verifier.Clear();
            _whitelist?.DeleteData(id);
            ok = false;
        }
        finally
        {
            _settings.Save();
            ReleasePreviewRef();
        }
        return ok;
    }

    public async Task<bool> EnrollWhitelistFromPhotoAsync(string id, string imagePath, Action<int>? progress = null)
    {
        if (!ConsentService.CanProcessFace(_settings)) return DenyEnroll();
        if (!_settings.WhitelistEnabled) return false;
        var item = _whitelist?.GetItem(id);
        if (item == null) return false;
        if (!await EnsureFaceEngineAsync())
        {
            _cameraError = "人脸识别模型加载失败，无法录入白名单人脸";
            LoggerService.LogInfo("白名单照片录入前模型按需加载失败");
            return false;
        }
        item.Verifier.Clear();
        bool ok = false;
        try
        {
            if (!File.Exists(imagePath)) { return false; }
            using var img = Cv2.ImRead(imagePath, ImreadModes.Color);
            if (img.Empty()) { return false; }
            int collected = 0;
            var faces = _recognizer!.Detect(img);
            if (faces.Count == 0)
            {
                LoggerService.LogInfo("白名单照片录入失败：未在照片中检测到人脸，请换一张正脸清晰照片");
                return false;
            }
            if (item.Verifier.AddSample(faces[0].Embedding)) collected++;
            using var flip = new Mat(); Cv2.Flip(img, flip, FlipMode.Y);
            var f2 = _recognizer.Detect(flip);
            if (f2.Count > 0 && item.Verifier.AddSample(f2[0].Embedding)) collected++;
            ok = collected >= 1;
            if (ok)
            {
                _whitelist!.Persist(id);
                LoggerService.LogInfo($"白名单照片录入成功：名称={item.Entry.Name} 接受={collected} 离散度={item.Verifier.SelfGap:F3}");
            }
            else
            {
                item.Verifier.Clear();
                _whitelist!.DeleteData(id);
                LoggerService.LogInfo($"白名单照片录入失败：名称={item.Entry.Name} 接受={collected}（请换一张正脸清晰照片）");
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogInfo("白名单照片录入过程异常：" + ex);
            item.Verifier.Clear();
            _whitelist?.DeleteData(id);
            ok = false;
        }
        finally
        {
            _settings.Save();
        }
        return ok;
    }

    private void ResetHistory()
    {
        _faceCountHistory.Clear();
        _ownerHistory.Clear();
        _strangerHistory.Clear();
        _ownerCountHistory.Clear();
        _whitelistCountHistory.Clear();
        _strangerCountHistory.Clear();
        _stableOwnerCount = 0;
        _stableWhitelistCount = 0;
        _stableStrangerCount = 0;
        _multiFaceNoticeActive = false;
    }

    private void PushStatus(EngineStatus s)
    {
        _status = s;
        Dispatcher.UIThread.Post(() =>
        {
            _tray?.SetTooltip("窥屿盾 · " + StatusText(s) + " · " + FaceDetail);
            _tray?.SetStatusColor(s);
            StatusChanged?.Invoke(s);
        });
    }

    public static string StatusText(EngineStatus s) => s switch
    {
        EngineStatus.Monitoring => "监控中",
        EngineStatus.Secure => "安全（仅机主）",
        EngineStatus.Peek => "⚠ 检测到偷窥",
        EngineStatus.Paused => "已暂停",
        EngineStatus.Manual => "手动防窥",
        EngineStatus.NoCamera => "摄像头不可用",
        EngineStatus.NotEnrolled => "未录入人脸",
        EngineStatus.ConsentRequired => "待同意人脸处理",
        EngineStatus.Error => "错误",
        _ => "就绪"
    };

    public void Dispose()
    {
        StopLoop();
        try { _autoBackupTimer?.Dispose(); } catch { }
        _autoBackupTimer = null;
        _fg.Stop();
        _overlay.Dismissed -= OnOverlayDismissed;
        _overlay.HideAll();
        _hotkey?.Dispose();
        _escHotkey?.Dispose();
        _tray?.Stop();
        _recognizer?.Dispose();
        _faceEngine?.Dispose();
        _verifier?.Dispose();
        lock (_faceAuthVerifiers) { foreach (var v in _faceAuthVerifiers.Values) v.Dispose(); }
    }
}
