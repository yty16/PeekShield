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
    private FaceVerifier? _unlockVerifier;
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
    private Task? _loopTask;

    private bool _peekActive;
    private int _faceCount;
    private bool _ownerPresent;
    private bool _loggedFrame;
    private int _consecutiveBlackFrames;
    private string _lastCameraErrorLog = "";
    private EngineStatus _status = EngineStatus.Idle;
    private string _cameraError = "";

    private DateTime _lastStatusLog = DateTime.MinValue;
    private int _lastLoggedFaceCount = -1;
    private bool _lastLoggedOwnerPresent;

    private readonly Queue<int> _faceCountHistory = new();
    private readonly Queue<bool> _ownerHistory = new();
    private readonly Queue<bool> _strangerHistory = new();
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

    public PeekShieldSettings Settings => _settings;
    public EngineStatus Status => _status;
    public int FaceCount => _faceCount;
    public bool OwnerPresent => _ownerPresent;
    public bool IsPeekActive => _peekActive;
    public string CameraError => _cameraError;
    public bool IsEnrolled => _verifier?.IsEnrolled ?? false;
    public bool IsFaceUnlockEnrolled => _unlockVerifier?.IsEnrolled ?? false;

    public double LastMatchDistance => _verifier?.LastDistance ?? -1;
    public double LastMatchThreshold => _verifier?.LastThreshold ?? -1;

    private static string EnrollDir => Platform.EnrollDir;
    private static string UnlockDir => Path.Combine(EnrollDir, "unlock");

    public void Initialize()
    {
        _settings = PeekShieldSettings.Load();
        SecurityService.Settings = _settings;
        _verifier = new FaceVerifier();
        _verifier.Load(EnrollDir);

        _unlockVerifier = new FaceVerifier();
        _unlockVerifier.Load(UnlockDir);
        if (_unlockVerifier.IsEnrolled && !_settings.FaceUnlockEnabled) { _settings.FaceUnlockEnabled = true; _settings.Save(); }
        if (!_unlockVerifier.IsEnrolled && _settings.FaceUnlockEnabled) { _settings.FaceUnlockEnabled = false; _settings.Save(); }

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

        LoggerService.CleanupOldData(_settings.AutoCleanupDays);

        LoggerService.LogInfo("引擎初始化完成（构建签名 " + BuildConstants.BuildSignature + " 系统 " + Platform.OsLabel + "）");
        PushStatus(StatusWhenIdle());

        if (_settings.EnableSmartPeek && !_settings.Paused && ConsentService.CanProcessFace(_settings))
            StartLoop();
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

    public void StopLoop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _loopTask?.Wait(2000); } catch { }
        _loopTask = null;
        _cts?.Dispose();
        _cts = null;
        if (_camera.IsOpen) _camera.Close();
    }

    public void RestartCamera()
    {
        _camera.Close();
        _loggedFrame = false;
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
                bool should = ShouldMonitor();
                if (!should)
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

                if (!_loggedFrame)
                {
                    LoggerService.LogInfo($"摄像头就绪，帧尺寸={frame.Width}x{frame.Height}，人脸模型就绪={_faceEngine?.IsFaceReady}");
                    _loggedFrame = true;
                }

                List<FaceInfo> faces = _faceEngine!.Detect(frame, _settings.Sensitivity, _settings.LowLightEnhance, _settings.MirrorPosterFilter);
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

        PushHistory(count, owner, strangerLooking);
        int stableCount = StableFaceCount();
        bool stableOwner = StableOwner();
        bool stableStranger = StableStranger();

        _faceCount = stableCount;
        _ownerPresent = stableOwner;

        var strangerFaces = faces.Where(f => !f.IsOwner && !f.IsWhitelisted && f.LookingAtScreen && f.Embedding.Length == FaceVerifier.Dim).ToList();
        bool hasAlertableStranger = stableCount >= 1 && stableStranger && HasAlertableStranger(strangerFaces);

        if (hasAlertableStranger && !_peekActive) StartPeek(frame, strangerFaces);
        else if (!hasAlertableStranger && _peekActive) EndPeek();

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

    private void PushHistory(int count, bool owner, bool stranger)
    {
        _faceCountHistory.Enqueue(count);
        _ownerHistory.Enqueue(owner);
        _strangerHistory.Enqueue(stranger);
        while (_faceCountHistory.Count > HistorySize) _faceCountHistory.Dequeue();
        while (_ownerHistory.Count > HistorySize) _ownerHistory.Dequeue();
        while (_strangerHistory.Count > HistorySize) _strangerHistory.Dequeue();
    }

    private int StableFaceCount() => _faceCountHistory.Count == 0 ? 0 : _faceCountHistory.Max();
    private bool StableOwner() => _ownerHistory.Count(h => h) >= 3;
    private bool StableStranger() => _strangerHistory.Count(h => h) >= 3;

    private void StartPeek(Mat frame, List<FaceInfo> strangers)
    {
        _peekActive = true;
        RegisterStrangerAlert(strangers);

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
        _overlay.HideAll();
        if (_settings.RestoreOnSafe) WindowGuard.RestoreProcesses(_settings.ProtectedProcesses);
        PushStatus(_ownerPresent ? EngineStatus.Secure : EngineStatus.Monitoring);
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
        TogglePause();
        var state = _settings.Paused ? "已暂停" : "已恢复";
        LoggerService.LogInfo($"快捷键切换防护状态：{state}");
        _tray?.ShowBalloon("窥屿盾", $"智能防窥{state}");
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
        StopLoop();
        if (!await EnsureFaceEngineAsync())
        {
            _cameraError = "人脸识别模型加载失败，无法录入人脸";
            LoggerService.LogInfo("录入前模型按需加载失败");
            return false;
        }
        bool wasEnrolled = _verifier!.IsEnrolled;
        _verifier.Clear();
        bool ok = false;
        var seen = new List<float[]>();
        try
        {
            var cam = _camera;
            if (!cam.Open(_settings.CameraIndex))
            {
                _cameraError = cam.LastError ?? "摄像头打开失败";
                RestorePriorEnrollment(wasEnrolled);
                return false;
            }
            using var frame = new Mat();
            int collected = 0;
            int attempts = 0;
            for (int i = 0; i < samples + 30 && collected < samples; i++)
            {
                if (!cam.ReadFrame(frame) || frame.Empty()) { await SafeDelay(150, default); continue; }
                var faces = _recognizer!.Detect(frame);
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
            cam.Close();
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
            if (_settings.EnableSmartPeek && !_settings.Paused) StartLoop();
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
        StopLoop();
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
            if (_settings.EnableSmartPeek && !_settings.Paused) StartLoop();
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

    public async Task<bool> EnrollUnlockAsync(int samples = 10, Action<int>? progress = null)
    {
        if (!_settings.PasswordEnabled) return false;
        if (!ConsentService.CanProcessFace(_settings)) return DenyEnroll();
        StopLoop();
        if (!await EnsureFaceEngineAsync())
        {
            _cameraError = "人脸识别模型加载失败，无法录入刷脸解锁";
            LoggerService.LogInfo("刷脸解锁录入前模型按需加载失败");
            return false;
        }
        bool wasEnrolled = _unlockVerifier!.IsEnrolled;
        _unlockVerifier.Clear();
        bool ok = false;
        var seen = new List<float[]>();
        try
        {
            var cam = _camera;
            if (!cam.Open(_settings.CameraIndex))
            {
                _cameraError = cam.LastError ?? "摄像头打开失败";
                RestorePriorUnlock(wasEnrolled);
                return false;
            }
            using var frame = new Mat();
            int collected = 0;
            int attempts = 0;
            for (int i = 0; i < samples + 30 && collected < samples; i++)
            {
                if (!cam.ReadFrame(frame) || frame.Empty()) { await SafeDelay(150, default); continue; }
                var faces = _recognizer!.Detect(frame);
                if (faces.Count == 0) { await SafeDelay(200, default); continue; }
                attempts++;
                var emb = faces[0].Embedding;
                bool dup = seen.Any(s => EmbDist(emb, s) < 0.25);
                if (!dup)
                {
                    seen.Add(emb);
                    if (_unlockVerifier.AddSample(emb)) { collected++; progress?.Invoke(collected); }
                }
                await SafeDelay(300, default);
            }
            cam.Close();
            ok = collected >= 3;
            if (ok)
            {
                _unlockVerifier.Save(UnlockDir);
                _settings.FaceUnlockEnabled = true;
                LoggerService.LogInfo($"刷脸解锁录入成功：尝试={attempts} 接受={collected} 离散度={_unlockVerifier.SelfGap:F3}");
            }
            else
            {
                RestorePriorUnlock(wasEnrolled);
                LoggerService.LogInfo($"刷脸解锁录入失败：尝试={attempts} 接受={collected}（需至少 3 张合格样本{(wasEnrolled ? "，已恢复此前录入" : "")}）");
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogInfo("刷脸解锁录入过程异常：" + ex);
            RestorePriorUnlock(wasEnrolled);
            ok = false;
        }
        finally
        {
            _settings.Save();
            if (_settings.EnableSmartPeek && !_settings.Paused) StartLoop();
        }
        return ok;
    }

    private void RestorePriorUnlock(bool wasEnrolled)
    {
        if (wasEnrolled)
        {
            try { _unlockVerifier!.Load(UnlockDir); } catch { }
            _settings.FaceUnlockEnabled = _unlockVerifier!.IsEnrolled;
        }
        else
        {
            _unlockVerifier!.Clear();
            _settings.FaceUnlockEnabled = false;
        }
    }

    public async Task<bool> EnrollUnlockFromPhotoAsync(string imagePath, Action<int>? progress = null)
    {
        if (!_settings.PasswordEnabled) return false;
        if (!ConsentService.CanProcessFace(_settings)) return DenyEnroll();
        StopLoop();
        if (!await EnsureFaceEngineAsync())
        {
            _cameraError = "人脸识别模型加载失败，无法录入刷脸解锁";
            LoggerService.LogInfo("刷脸解锁照片录入前模型按需加载失败");
            return false;
        }
        bool wasEnrolled = _unlockVerifier!.IsEnrolled;
        _unlockVerifier.Clear();
        bool ok = false;
        try
        {
            if (!File.Exists(imagePath)) { RestorePriorUnlock(wasEnrolled); return false; }
            using var img = Cv2.ImRead(imagePath, ImreadModes.Color);
            if (img.Empty()) { RestorePriorUnlock(wasEnrolled); return false; }

            int collected = 0;
            var faces = _recognizer!.Detect(img);
            if (faces.Count == 0)
            {
                RestorePriorUnlock(wasEnrolled);
                LoggerService.LogInfo("刷脸解锁照片录入失败：未在照片中检测到人脸，请换一张正脸清晰照片");
                return false;
            }
            if (_unlockVerifier.AddSample(faces[0].Embedding)) collected++;
            using var flip = new Mat(); Cv2.Flip(img, flip, FlipMode.Y);
            var f2 = _recognizer.Detect(flip);
            if (f2.Count > 0 && _unlockVerifier.AddSample(f2[0].Embedding)) collected++;

            ok = collected >= 1;
            if (ok)
            {
                _unlockVerifier.Save(UnlockDir);
                _settings.FaceUnlockEnabled = true;
                LoggerService.LogInfo($"刷脸解锁照片录入成功：接受={collected} 离散度={_unlockVerifier.SelfGap:F3}");
            }
            else
            {
                RestorePriorUnlock(wasEnrolled);
                LoggerService.LogInfo($"刷脸解锁照片录入失败：接受={collected}（请换一张正脸清晰照片{(wasEnrolled ? "，已恢复此前录入" : "")})");
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogInfo("刷脸解锁照片录入过程异常：" + ex);
            RestorePriorUnlock(wasEnrolled);
            ok = false;
        }
        finally
        {
            _settings.Save();
            if (_settings.EnableSmartPeek && !_settings.Paused) StartLoop();
        }
        return ok;
    }

    public async Task<bool> VerifyUnlockAsync(Action<string>? status = null)
    {
        if (_unlockVerifier == null || !_unlockVerifier.IsEnrolled) return false;
        if (!ConsentService.CanProcessFace(_settings)) return false;
        StopLoop();
        if (!await EnsureFaceEngineAsync())
        {
            status?.Invoke("人脸识别模型加载失败，请使用密码");
            LoggerService.LogInfo("刷脸解锁验证前模型按需加载失败");
            return false;
        }
        bool ok = false;
        try
        {
            var cam = _camera;
            if (!cam.Open(_settings.CameraIndex))
            {
                status?.Invoke("摄像头打开失败，请使用密码");
                return false;
            }
            using var frame = new Mat();
            double th = FaceEngine.OwnerMatchThreshold(_settings.Sensitivity);
            int attempts = 0;
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(8) && !ok)
            {
                if (!cam.ReadFrame(frame) || frame.Empty()) { await SafeDelay(150, default); continue; }
                var faces = _recognizer!.Detect(frame);
                if (faces.Count == 0) { await SafeDelay(200, default); continue; }
                attempts++;
                var (isOwner, dist) = _unlockVerifier.Verify(faces[0].Embedding, th);
                status?.Invoke("人脸比对中…");
                if (isOwner) { ok = true; break; }
                await SafeDelay(200, default);
            }
            cam.Close();
            LoggerService.LogInfo($"刷脸解锁验证：尝试={attempts} 结果={(ok ? "通过" : "未匹配")} 阈值={th:F3} 耗时={sw.Elapsed.TotalSeconds:F1}s");
        }
        catch (Exception ex)
        {
            LoggerService.LogInfo("刷脸解锁验证异常：" + ex);
            ok = false;
        }
        finally
        {
            if (_settings.EnableSmartPeek && !_settings.Paused) StartLoop();
        }
        return ok;
    }

    public bool QuickVerifyAvailable =>
        _settings.PasswordEnabled && _settings.QuickVerifyEnabled && IsEnrolled && ConsentService.CanProcessFace(_settings);

    public async Task<bool> TryAutoVerifyAsync(Action<string>? status = null)
    {
        if (_verifier == null || !_verifier.IsEnrolled) return false;
        if (!_settings.PasswordEnabled || !_settings.QuickVerifyEnabled) return false;
        if (!ConsentService.CanProcessFace(_settings)) return false;
        StopLoop();
        if (!await EnsureFaceEngineAsync())
        {
            status?.Invoke("人脸识别模型加载失败，请使用密码");
            LoggerService.LogInfo("快捷验证前模型按需加载失败");
            return false;
        }
        bool ok = false;
        try
        {
            var cam = _camera;
            if (!cam.Open(_settings.CameraIndex))
            {
                status?.Invoke("摄像头打开失败，请使用密码");
                return false;
            }
            using var frame = new Mat();
            double th = FaceEngine.OwnerMatchThreshold(_settings.Sensitivity);
            int attempts = 0;
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(8) && !ok)
            {
                if (!cam.ReadFrame(frame) || frame.Empty()) { await SafeDelay(150, default); continue; }
                var faces = _recognizer!.Detect(frame);
                if (faces.Count == 0) { await SafeDelay(200, default); continue; }
                attempts++;
                var (isOwner, dist) = _verifier.Verify(faces[0].Embedding, th);
                status?.Invoke("正在识别机主…（无需操作）");
                if (isOwner) { ok = true; break; }
                await SafeDelay(200, default);
            }
            cam.Close();
            LoggerService.LogInfo($"快捷验证：尝试={attempts} 结果={(ok ? "通过" : "未匹配")} 阈值={th:F3} 耗时={sw.Elapsed.TotalSeconds:F1}s");
        }
        catch (Exception ex)
        {
            LoggerService.LogInfo("快捷验证异常：" + ex);
            ok = false;
        }
        finally
        {
            if (_settings.EnableSmartPeek && !_settings.Paused) StartLoop();
        }
        return ok;
    }

    public void ClearUnlock()
    {
        _unlockVerifier?.Clear();
        try
        {
            if (Directory.Exists(UnlockDir))
                foreach (var f in Directory.GetFiles(UnlockDir)) File.Delete(f);
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
        StopLoop();
        if (!await EnsureFaceEngineAsync())
        {
            _cameraError = "人脸识别模型加载失败，无法录入白名单人脸";
            LoggerService.LogInfo("白名单录入前模型按需加载失败");
            return false;
        }
        item.Verifier.Clear();
        bool ok = false;
        var seen = new List<float[]>();
        try
        {
            var cam = _camera;
            if (!cam.Open(_settings.CameraIndex))
            {
                _cameraError = cam.LastError ?? "摄像头打开失败";
                return false;
            }
            using var frame = new Mat();
            int collected = 0;
            int attempts = 0;
            for (int i = 0; i < samples + 30 && collected < samples; i++)
            {
                if (!cam.ReadFrame(frame) || frame.Empty()) { await SafeDelay(150, default); continue; }
                var faces = _recognizer!.Detect(frame);
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
            cam.Close();
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
            if (_settings.EnableSmartPeek && !_settings.Paused) StartLoop();
        }
        return ok;
    }

    public async Task<bool> EnrollWhitelistFromPhotoAsync(string id, string imagePath, Action<int>? progress = null)
    {
        if (!ConsentService.CanProcessFace(_settings)) return DenyEnroll();
        if (!_settings.WhitelistEnabled) return false;
        var item = _whitelist?.GetItem(id);
        if (item == null) return false;
        StopLoop();
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
            if (_settings.EnableSmartPeek && !_settings.Paused) StartLoop();
        }
        return ok;
    }

    private void ResetHistory()
    {
        _faceCountHistory.Clear();
        _ownerHistory.Clear();
        _strangerHistory.Clear();
    }

    private void PushStatus(EngineStatus s)
    {
        _status = s;
        Dispatcher.UIThread.Post(() =>
        {
            _tray?.SetTooltip("窥屿盾 · " + StatusText(s) + " · 人脸 " + _faceCount);
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
        _fg.Stop();
        _overlay.Dismissed -= OnOverlayDismissed;
        _overlay.HideAll();
        _hotkey?.Dispose();
        _escHotkey?.Dispose();
        _tray?.Stop();
        _recognizer?.Dispose();
        _faceEngine?.Dispose();
        _verifier?.Dispose();
        _unlockVerifier?.Dispose();
    }
}
