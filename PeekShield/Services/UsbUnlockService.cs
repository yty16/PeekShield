using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace PeekShield.Services;

public sealed class UsbVolume
{
    public string Label { get; set; } = "";
    public string RootPath { get; set; } = "";
}

public static class UsbUnlockService
{
    private const string TokenFileName = ".peekshield_usb";

    public static bool IsSupported() => true;

    public static string GenerateToken() => Guid.NewGuid().ToString("N");

    public static string HashToken(string token)
    {
        var b = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        var sb = new System.Text.StringBuilder(b.Length * 2);
        foreach (var x in b) sb.Append(x.ToString("x2"));
        return sb.ToString();
    }

    public static string LabelOf(UsbVolume v)
    {
        if (!string.IsNullOrWhiteSpace(v.Label)) return v.Label;
        var p = v.RootPath.TrimEnd('/', '\\');
        var i = p.LastIndexOfAny(new[] { '/', '\\' });
        return i >= 0 ? p[(i + 1)..] : p;
    }

    public static List<UsbVolume> ListCandidateVolumes()
    {
        var list = new List<UsbVolume>();
        foreach (var d in DriveInfo.GetDrives())
        {
            if (!d.IsReady) continue;
            var root = d.RootDirectory.FullName;
            bool match = false;
#if WINDOWS
            match = d.DriveType == DriveType.Removable;
#elif LINUX
            match = root.StartsWith("/media/") || root.StartsWith("/run/media/") || root.StartsWith("/mnt/");
#elif MACOS
            match = root.StartsWith("/Volumes/");
#else
            match = true;
#endif
            if (!match) continue;
            var label = string.IsNullOrWhiteSpace(d.VolumeLabel)
                ? LabelOf(new UsbVolume { RootPath = root })
                : d.VolumeLabel;
            list.Add(new UsbVolume { Label = label, RootPath = root });
        }
        return list;
    }

    public static bool WriteToken(string rootPath, string token)
    {
        try
        {
            var path = Path.Combine(rootPath, TokenFileName);
            File.WriteAllText(path, token);
#if WINDOWS
            File.SetAttributes(path, FileAttributes.Hidden);
#endif
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? ReadToken(string rootPath)
    {
        try
        {
            var path = Path.Combine(rootPath, TokenFileName);
            if (!File.Exists(path)) return null;
            return File.ReadAllText(path).Trim();
        }
        catch
        {
            return null;
        }
    }

    public static bool TryUnlock(string expectedHash, bool fileMode)
    {
        if (string.IsNullOrEmpty(expectedHash)) return false;
        foreach (var d in DriveInfo.GetDrives())
        {
            if (!d.IsReady) continue;
            var root = d.RootDirectory.FullName;
            if (fileMode)
            {
                var tok = ReadToken(root);
                if (tok != null && HashToken(tok) == expectedHash) return true;
            }
            else
            {
                var serial = GetVolumeSerial(root);
                if (serial != null && HashToken(serial) == expectedHash) return true;
            }
        }
        return false;
    }

    public static string? GetVolumeSerial(string rootPath)
    {
        try
        {
#if WINDOWS
            var sbVol = new StringBuilder(256);
            uint serial = 0;
            uint maxCompLen = 0, fileSysFlags = 0;
            var sbFs = new StringBuilder(256);
            if (GetVolumeInformation(rootPath, sbVol, 256, ref serial, ref maxCompLen, ref fileSysFlags, sbFs, 256))
                return serial.ToString("X8");
#endif
            var d = new DriveInfo(rootPath);
            var fallback = (d.VolumeLabel ?? "") + "|" + rootPath;
            return fallback;
        }
        catch
        {
            return null;
        }
    }

#if WINDOWS
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeInformation(string lpRootPathName, StringBuilder lpVolumeNameBuffer, int nVolumeNameSize, ref uint lpVolumeSerialNumber, ref uint lpMaximumComponentLength, ref uint lpFileSystemFlags, StringBuilder lpFileSystemNameBuffer, int nFileSystemNameSize);
#endif
}
