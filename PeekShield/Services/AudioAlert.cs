using System;
using System.Diagnostics;
using System.IO;

namespace PeekShield.Services;

public static class AudioAlert
{
    private static readonly byte[] _wav = BuildBeep();

    public static void Play()
    {
        try
        {
#if WINDOWS
            Native.PlaySound(_wav, IntPtr.Zero, Native.SND_MEMORY | Native.SND_ASYNC | Native.SND_NODEFAULT);
#else
            PlayCrossPlatform();
#endif
        }
        catch { }
    }

#if !WINDOWS
    private static void PlayCrossPlatform()
    {
        string? player = Platform.IsMacOS ? "afplay" : (FindLinuxPlayer());
        if (player == null) return;
        var tmp = Path.Combine(Path.GetTempPath(), "peek_alert.wav");
        File.WriteAllBytes(tmp, _wav);
        var psi = new ProcessStartInfo
        {
            FileName = player,
            Arguments = player == "afplay" ? $"\"{tmp}\"" : $"\"{tmp}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var p = Process.Start(psi);
        p?.WaitForExit(1500);
    }

    private static string? FindLinuxPlayer()
    {
        foreach (var c in new[] { "paplay", "aplay", "play" })
        {
            try
            {
                var which = new ProcessStartInfo { FileName = "which", Arguments = c, UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
                using var p = Process.Start(which);
                var outp = p?.StandardOutput.ReadToEnd();
                if (!string.IsNullOrWhiteSpace(outp)) return c;
            }
            catch { }
        }
        return null;
    }
#endif

    private static byte[] BuildBeep()
    {
        const int rate = 44100;
        const int totalMs = 320;
        var samples = new short[rate * totalMs / 1000];
        for (int i = 0; i < samples.Length; i++)
        {
            double t = (double)i / rate;
            double f = t < 0.16 ? 880 : 1320;
            double elapsedInPhase = t < 0.16 ? t : (t - 0.16);
            double env = Math.Min(1.0, elapsedInPhase / 0.02) * Math.Min(1.0, (0.16 - elapsedInPhase) / 0.04 + 0.2);
            env = Math.Max(0, env);
            samples[i] = (short)(Math.Sin(2 * Math.PI * f * t) * 0.32 * 32767 * env);
        }

        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write("RIFF".ToCharArray());
            bw.Write(36 + samples.Length * 2);
            bw.Write("WAVE".ToCharArray());
            bw.Write("fmt ".ToCharArray());
            bw.Write(16);
            bw.Write((short)1);
            bw.Write((short)1);
            bw.Write(rate);
            bw.Write(rate * 2);
            bw.Write((short)2);
            bw.Write((short)16);
            bw.Write("data".ToCharArray());
            bw.Write(samples.Length * 2);
            foreach (var s in samples) bw.Write(s);
        }
        return ms.ToArray();
    }
}
