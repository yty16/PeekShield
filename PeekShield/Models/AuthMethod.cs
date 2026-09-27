using System;
using System.Text.Json;

namespace PeekShield.Models;

public enum AuthMethodKind
{
    Password = 0,
    Face = 1,
    System = 2,
    Usb = 3,
    QuickFace = 4
}

public class AuthMethodEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public AuthMethodKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string OptionsJson { get; set; } = "";
    public List<string> Operations { get; set; } = new();

    public AuthUsbOptions GetUsbOptions() =>
        string.IsNullOrEmpty(OptionsJson) ? new AuthUsbOptions() : JsonSerializer.Deserialize<AuthUsbOptions>(OptionsJson) ?? new AuthUsbOptions();

    public void SetUsbOptions(AuthUsbOptions v) => OptionsJson = JsonSerializer.Serialize(v);

    public AuthPasswordOptions GetPasswordOptions() =>
        string.IsNullOrEmpty(OptionsJson) ? new AuthPasswordOptions() : JsonSerializer.Deserialize<AuthPasswordOptions>(OptionsJson) ?? new AuthPasswordOptions();

    public void SetPasswordOptions(AuthPasswordOptions v) => OptionsJson = JsonSerializer.Serialize(v);
}

public class AuthUsbOptions
{
    public bool UseFileMode { get; set; } = false;
    public string TokenHash { get; set; } = "";
    public string DriveLabel { get; set; } = "";
    public string SerialHash { get; set; } = "";
}

public class AuthPasswordOptions
{
    public string PasswordHash { get; set; } = "";
    public string SecurityQuestion { get; set; } = "";
    public string SecurityAnswerHash { get; set; } = "";
}
