using System.Collections.Generic;

namespace PeekShield.Models;

public class ConfigBackup
{
    public string FormatVersion { get; set; } = "1.0";
    public string AppVersion { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public List<string> Items { get; set; } = new();
    public string SettingsJson { get; set; } = "";
    public string OwnerFace { get; set; } = "";
    public List<BackupFace> AuthFaces { get; set; } = new();
    public List<BackupFace> WhitelistFaces { get; set; } = new();
}

public class BackupFace
{
    public string Id { get; set; } = "";
    public string Data { get; set; } = "";
}

public class KydFile
{
    public string Magic { get; set; } = "KYD1";
    public bool Encrypted { get; set; }
    public string Salt { get; set; } = "";
    public string Iv { get; set; } = "";
    public string Data { get; set; } = "";
    public string SuperBlob { get; set; } = "";
    public string SuperIv { get; set; } = "";
}

public class BackupExportRequest
{
    public bool IncludeSettings { get; set; } = true;
    public bool IncludeOwnerFace { get; set; }
    public bool IncludeWhitelistFaces { get; set; }
    public bool IncludeAuthFaces { get; set; }
    public string? FilePassword { get; set; }
}

public class BackupExportResult
{
    public bool Success { get; set; }
    public string Error { get; set; } = "";
    public List<string> Items { get; set; } = new();
    public bool Encrypted { get; set; }
}

public class BackupImportResult
{
    public bool Success { get; set; }
    public string Error { get; set; } = "";
    public List<string> Items { get; set; } = new();
}
