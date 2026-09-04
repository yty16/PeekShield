using System;
using System.IO;
using System.Linq;

namespace PeekShield.Services;

public class PeekShieldEngine : IDisposable
{
    public static PeekShieldEngine Instance { get; } = new PeekShieldEngine();

    private CameraService _cam = new();
    private FaceRecognizer _recognizer = new();
    private FaceVerifier _verifier = new();
    private FaceEngine _faceEngine = null!;
    private bool _ready;

    public bool IsFaceReady => _recognizer.IsReady;
    public bool IsEnrolled => _verifier.IsEnrolled;
    public int EnrolledCount => _verifier.SampleCount;

    public CameraService Camera => _cam;
    public FaceEngine FaceEngine => _faceEngine;

    public void Initialize()
    {
        LoggerService.LogInfo("引擎启动（构建签名 " + BuildConstants._buildToken + " 系统 " + Platform.OsLabel + "）");
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

        _ready = true;
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

    public void Dispose()
    {
        try { _cam.Dispose(); } catch { }
        try { _recognizer?.Dispose(); } catch { }
    }
}
