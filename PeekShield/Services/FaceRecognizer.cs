using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using DlibDotNet;
using DlibDotNet.Dnn;
using OpenCvSharp;

namespace PeekShield.Services;

public class DlibFace
{
    public Rect Rect;
    public float[] Embedding = Array.Empty<float>();
    public bool HasEyes;
    public double EyeAngleDeg;
}

public class FaceRecognizer : IDisposable
{
    private FrontalFaceDetector? _detector;
    private ShapePredictor? _shapePredictor;
    private LossMetric? _net;
    private bool _ready;
    private static readonly List<string> _tempCopies = new();

    public bool IsReady => _ready;

    public void Load(string shapePredictorPath, string resnetPath)
    {
        try
        {
            _detector = Dlib.GetFrontalFaceDetector();
            var spPath = ResolveLoadPath(shapePredictorPath);
            var netPath = ResolveLoadPath(resnetPath);
            _shapePredictor = ShapePredictor.Deserialize(spPath);
            _net = LossMetric.Deserialize(netPath, 0);
            _ready = true;
            LoggerService.LogInfo($"Dlib 模型加载完成（68点={shapePredictorPath} 识别网络={resnetPath}）");
        }
        catch (Exception ex)
        {
            _ready = false;
            LoggerService.LogInfo("Dlib 模型加载失败：" + ex);
        }
    }

    private static string ResolveLoadPath(string path)
    {
        if (IsAscii(path) && File.Exists(path)) return path;
#if WINDOWS
        var sb = new StringBuilder(1024);
        if (GetShortPathName(path, sb, sb.Capacity) > 0)
        {
            var shortPath = sb.ToString();
            if (IsAscii(shortPath) && File.Exists(shortPath)) return shortPath;
        }
#endif
        var tmpDir = Path.GetTempPath();
        var tmp = Path.Combine(tmpDir, Guid.NewGuid().ToString("N") + ".dat");
        File.Copy(path, tmp, true);
        _tempCopies.Add(tmp);
        return tmp;
    }

    private static bool IsAscii(string s)
    {
        foreach (var c in s)
            if (c > 127) return false;
        return true;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetShortPathName(string lpszLongPath, StringBuilder lpszShortPath, int cchBuffer);

    public List<DlibFace> Detect(Mat frame, int maxDim = 420)
    {
        var result = new List<DlibFace>();
        if (!_ready || frame == null || frame.Empty()) return result;

        double scale = 1.0;
        Mat small;
        if (frame.Width > maxDim)
        {
            scale = (double)maxDim / frame.Width;
            small = new Mat();
            Cv2.Resize(frame, small, new Size(maxDim, (int)(frame.Height * scale)), 0, 0, InterpolationFlags.Area);
        }
        else
        {
            small = frame;
        }

        Array2D<RgbPixel>? arr = null;
        try
        {
            arr = MatToArray2D(small);
            var faces = _detector!.Operator(arr, 1);
            foreach (var r in faces)
            {
                var shape = _shapePredictor!.Detect(arr, r);
                try
                {
                    var chip = ExtractChip(arr, shape);
                    if (chip == null) continue;
                    var emb = ComputeEmbedding(chip);
                    chip.Dispose();
                    if (emb == null) continue;
                    var f = new DlibFace
                    {
                        Rect = new Rect((int)(r.Left / scale), (int)(r.Top / scale), (int)(r.Width / scale), (int)(r.Height / scale)),
                        Embedding = emb,
                        HasEyes = HasBothEyes(shape),
                        EyeAngleDeg = EyeAngle(shape)
                    };
                    result.Add(f);
                }
                finally
                {
                    if (shape is IDisposable d) d.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            LoggerService.LogInfo("Dlib 检测异常：" + ex.Message);
        }
        finally
        {
            arr?.Dispose();
            if (small != frame) small.Dispose();
        }

        return NonMaxSuppress(result, 0.3);
    }

    private float[]? ComputeEmbedding(Array2D<RgbPixel> chip)
    {
        try
        {
            using var chipMat = new Matrix<RgbPixel>(chip);
            using var output = _net!.Operator(chipMat, 1);
            var desc = output[0];
            int n = (int)desc.Rows;
            var emb = new float[n];
            for (int i = 0; i < n; i++) emb[i] = (float)desc[i, 0];
            desc.Dispose();
            return emb;
        }
        catch
        {
            return null;
        }
    }

    private Array2D<RgbPixel>? ExtractChip(Array2D<RgbPixel> arr, FullObjectDetection shape)
    {
        try
        {
            var details = Dlib.GetFaceChipDetails(shape, 150, 0.25);
            return Dlib.ExtractImageChip<RgbPixel>(arr, details, InterpolationTypes.Bilinear);
        }
        catch
        {
            return null;
        }
    }

    private static bool HasBothEyes(FullObjectDetection shape)
    {
        try
        {
            shape.GetPart(36);
            shape.GetPart(45);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static double EyeAngle(FullObjectDetection shape)
    {
        try
        {
            var le = MeanPoint(shape, 36, 41);
            var re = MeanPoint(shape, 42, 47);
            return Math.Atan2(re.Y - le.Y, re.X - le.X) * 180.0 / Math.PI;
        }
        catch
        {
            return 0;
        }
    }

    private static DlibDotNet.Point MeanPoint(FullObjectDetection shape, uint a, uint b)
    {
        int sx = 0, sy = 0, c = 0;
        for (uint i = a; i <= b; i++)
        {
            var p = shape.GetPart(i);
            sx += p.X;
            sy += p.Y;
            c++;
        }
        return new DlibDotNet.Point(sx / c, sy / c);
    }

    private static Array2D<RgbPixel> MatToArray2D(Mat bgr)
    {
        int h = bgr.Height, w = bgr.Width;
        var arr = new Array2D<RgbPixel>(h, w);
        var idx = bgr.GetUnsafeGenericIndexer<Vec3b>();
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var v = idx[y, x];
                arr[y][x] = new RgbPixel(v.Item2, v.Item1, v.Item0);
            }
        return arr;
    }

    private static List<DlibFace> NonMaxSuppress(List<DlibFace> faces, double iou)
    {
        if (faces.Count <= 1) return faces;
        var kept = new List<DlibFace>();
        var ordered = faces.OrderByDescending(f => f.Rect.Width * f.Rect.Height).ToList();
        while (ordered.Count > 0)
        {
            var f = ordered[0];
            kept.Add(f);
            ordered.RemoveAt(0);
            for (int i = ordered.Count - 1; i >= 0; i--)
                if (IoU(f.Rect, ordered[i].Rect) > iou) ordered.RemoveAt(i);
        }
        return kept;
    }

    private static double IoU(Rect a, Rect b)
    {
        int x1 = Math.Max(a.X, b.X), y1 = Math.Max(a.Y, b.Y);
        int x2 = Math.Min(a.X + a.Width, b.X + b.Width), y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);
        int inter = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
        int aa = a.Width * a.Height, bb = b.Width * b.Height;
        if (aa + bb - inter <= 0) return 0;
        return (double)inter / (aa + bb - inter);
    }

    public void Dispose()
    {
        _shapePredictor?.Dispose();
        _net?.Dispose();
        foreach (var t in _tempCopies)
        {
            try { if (File.Exists(t)) File.Delete(t); } catch { }
        }
        _tempCopies.Clear();
    }
}
