using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

namespace PeekShield.Services;

public class FaceInfo
{
    public Rect Rect;
    public bool IsOwner;
    public double Score;
    public bool HasEyes;
    public double EyeAngleDeg;
    public bool LookingAtScreen;
    public float[] Embedding = Array.Empty<float>();
}

public class FaceEngine : IDisposable
{
    private readonly FaceRecognizer _recognizer;
    private readonly FaceVerifier _verifier;

    private static readonly double[] AngleTol = { 30, 22, 15 };
    private static readonly double[] CenterTol = { 0.50, 0.38, 0.28 };
    private static readonly double[] MinSizeFrac = { 0.10, 0.13, 0.16 };
    private static readonly double[] OwnerThresh = { 0.55, 0.48, 0.40 };

    public double LastFrameMean { get; private set; }
    public double LastFrameStd { get; private set; }
    public int LastRawFaceCount { get; private set; }

    public FaceEngine(FaceRecognizer recognizer, FaceVerifier verifier)
    {
        _recognizer = recognizer;
        _verifier = verifier;
    }

    public bool IsFaceReady => _recognizer.IsReady;

    public List<FaceInfo> Detect(Mat frame, int sensitivity, bool lowLight, bool mirrorPosterFilter)
    {
        sensitivity = Math.Clamp(sensitivity, 0, 2);
        var list = new List<FaceInfo>();

        var gray = new Mat();
        Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.MeanStdDev(gray, out var m, out var s);
        LastFrameMean = m.Val0;
        LastFrameStd = s.Val0;
        gray.Dispose();

        var dlibFaces = _recognizer.Detect(frame);
        LastRawFaceCount = dlibFaces.Count;

        foreach (var df in dlibFaces)
        {
            var (isOwner, score) = _verifier.IsEnrolled
                ? _verifier.Verify(df.Embedding, OwnerThresh[sensitivity])
                : (false, double.MaxValue);

            double fw = (double)df.Rect.Width / frame.Width;
            double cx = (df.Rect.X + df.Rect.Width / 2.0) / frame.Width;
            double offset = Math.Abs(cx - 0.5) * 2;

            bool looking = df.HasEyes
                && Math.Abs(df.EyeAngleDeg) <= AngleTol[sensitivity]
                && fw >= MinSizeFrac[sensitivity]
                && offset <= CenterTol[sensitivity];

            if (mirrorPosterFilter && !df.HasEyes && fw > 0.55) continue;

            list.Add(new FaceInfo
            {
                Rect = df.Rect,
                IsOwner = isOwner,
                Score = score,
                HasEyes = df.HasEyes,
                EyeAngleDeg = df.EyeAngleDeg,
                LookingAtScreen = looking,
                Embedding = df.Embedding ?? Array.Empty<float>()
            });
        }
        return list;
    }

    public void Dispose()
    {
    }
}
