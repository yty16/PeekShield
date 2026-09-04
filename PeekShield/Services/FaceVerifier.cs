using System;
using System.Collections.Generic;
using System.IO;

namespace PeekShield.Services;

public class FaceVerifier
{
    public const int Dim = 128;

    private readonly object _lock = new();
    private readonly List<float[]> _samples = new();
    private double _selfGap;

    public bool IsEnrolled { get { lock (_lock) return _samples.Count > 0; } }
    public int SampleCount { get { lock (_lock) return _samples.Count; } }

    public double LastDistance { get; private set; } = double.MaxValue;
    public double LastThreshold { get; private set; }

    public double SelfGap { get { lock (_lock) return _selfGap; } }

    public void Clear()
    {
        lock (_lock)
        {
            _samples.Clear();
            _selfGap = 0;
        }
    }

    public void Dispose() => Clear();

    public bool AddSample(float[] desc)
    {
        if (desc == null || desc.Length != Dim) return false;
        lock (_lock)
        {
            _samples.Add((float[])desc.Clone());
            RecomputeStats();
        }
        return true;
    }

    private void RecomputeStats()
    {
        _selfGap = 0;
        if (_samples.Count < 2) return;
        double total = 0; int pairs = 0;
        for (int i = 0; i < _samples.Count; i++)
            for (int j = i + 1; j < _samples.Count; j++)
            {
                total += L2(_samples[i], _samples[j]);
                pairs++;
            }
        _selfGap = total / pairs;
    }

    public (bool isOwner, double distance) Verify(float[]? desc, double floor)
    {
        LastDistance = double.MaxValue;
        LastThreshold = floor;
        if (desc == null || desc.Length != Dim) return (false, double.MaxValue);

        double best;
        lock (_lock)
        {
            if (_samples.Count == 0) return (false, double.MaxValue);
            best = double.MaxValue;
            foreach (var s in _samples)
            {
                double d = L2(s, desc);
                if (d < best) best = d;
            }
        }

        double th = floor;
        if (th < 0.1) th = 0.1;
        if (th > 0.75) th = 0.75;

        LastThreshold = th;
        LastDistance = best;
        return (best < th, best);
    }

    private static double L2(float[] a, float[] b)
    {
        double s = 0;
        for (int i = 0; i < a.Length; i++)
        {
            double d = a[i] - b[i];
            s += d * d;
        }
        return Math.Sqrt(s);
    }

    public void Save(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "embeddings.bin");
            lock (_lock)
            {
                using var fs = File.Create(path);
                using var bw = new BinaryWriter(fs);
                bw.Write(_samples.Count);
                foreach (var s in _samples)
                    foreach (var v in s) bw.Write(v);
            }
        }
        catch { }
    }

    public void Load(string dir)
    {
        Clear();
        try
        {
            var path = Path.Combine(dir, "embeddings.bin");
            if (!File.Exists(path)) return;
            var loaded = new List<float[]>();
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);
            int n = br.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                var arr = new float[Dim];
                for (int j = 0; j < Dim; j++) arr[j] = br.ReadSingle();
                loaded.Add(arr);
            }
            lock (_lock)
            {
                _samples.Clear();
                _samples.AddRange(loaded);
                RecomputeStats();
            }
        }
        catch { }
    }
}
