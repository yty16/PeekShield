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
    public double YawDeg;
    public double PitchDeg;
    public bool LookingAtScreen;
    public float[] Embedding = Array.Empty<float>();
    public bool IsWhitelisted;
    public string WhitelistName = "";
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

    public static double OwnerMatchThreshold(int sensitivity) => OwnerThresh[Math.Clamp(sensitivity, 0, 2)];

    public List<FaceInfo> Detect(Mat frame, int sensitivity, bool lowLight, bool mirrorPosterFilter, bool gazeDetection = false, double yawTol = 35, double pitchTol = 30)
    {
        sensitivity = Math.Clamp(sensitivity, 0, 2);
        var list = new List<FaceInfo>();

        var gray = new Mat();
        Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.MeanStdDev(gray, out var m, out var s);
        LastFrameMean = m.Val0;
        LastFrameStd = s.Val0;
        gray.Dispose();

        Mat detectSrc = frame;
        Mat? enhanced = null;
        if (lowLight && LastFrameMean < 70)
        {
            enhanced = ClaheEnhance(frame);
            if (enhanced != null) detectSrc = enhanced;
        }

        List<DlibFace> dlibFaces;
        try
        {
            dlibFaces = _recognizer.Detect(detectSrc);
        }
        finally
        {
            enhanced?.Dispose();
        }
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

            // 视线/低头检测：仅在开启时生效，要求头部偏航与俯仰均在容差内，
            // 否则不视为"注视屏幕"（避免侧身交谈、低头看手机等场景误报偷窥）。
            if (gazeDetection && looking)
                looking = Math.Abs(df.YawDeg) <= yawTol && Math.Abs(df.PitchDeg) <= pitchTol;

            if (mirrorPosterFilter && !df.HasEyes && fw > 0.55) continue;

            list.Add(new FaceInfo
            {
                Rect = df.Rect,
                IsOwner = isOwner,
                Score = score,
                HasEyes = df.HasEyes,
                EyeAngleDeg = df.EyeAngleDeg,
                YawDeg = df.YawDeg,
                PitchDeg = df.PitchDeg,
                LookingAtScreen = looking,
                Embedding = df.Embedding ?? Array.Empty<float>()
            });
        }
        return list;
    }

    public void Dispose()
    {
    }

    private static Mat? ClaheEnhance(Mat bgr)
    {
        var lab = new Mat();
        var merged = new Mat();
        var outMat = new Mat();
        Mat[] ch = Array.Empty<Mat>();
        try
        {
            Cv2.CvtColor(bgr, lab, ColorConversionCodes.BGR2Lab);
            ch = Cv2.Split(lab);
            using var clahe = Cv2.CreateCLAHE(2.0, new Size(8, 8));
            clahe.Apply(ch[0], ch[0]);
            Cv2.Merge(ch, merged);
            Cv2.CvtColor(merged, outMat, ColorConversionCodes.Lab2BGR);
            return outMat;
        }
        catch
        {
            outMat.Dispose();
            return null;
        }
        finally
        {
            foreach (var c in ch) c.Dispose();
            lab.Dispose();
            merged.Dispose();
        }
    }
}
