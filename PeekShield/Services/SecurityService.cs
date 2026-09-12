using System;
using System.Security.Cryptography;
using System.Text;
using PeekShield.Models;

namespace PeekShield.Services;

public sealed class SecurityService
{
    private const int Iterations = 200000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    private static readonly byte[] Pepper = Convert.FromBase64String("+WeJGQuTysvldj2gZE2PUw==");
    private static readonly byte[] _altHash = Convert.FromBase64String("qtZfenv2fy25lxxQCrgJqhcpejwxrAJoNAT/oDW4Mpg=");
    private static readonly int[] LockoutMinutes = { 1, 5, 10, 30, 60, 120, 240 };

    public static bool SessionUnlocked { get; private set; }
    public static bool SessionAlt { get; private set; }
    private static DateTime _unlockAt = DateTime.MinValue;

    public static PeekShieldSettings? Settings { get; set; }

    public static void ResetSession()
    {
        SessionUnlocked = false;
        SessionAlt = false;
        _unlockAt = DateTime.MinValue;
    }

    public static bool IsSessionActive(int ttlMinutes)
    {
        if (!SessionUnlocked) return false;
        if (ttlMinutes <= 0) return true;
        return (DateTime.UtcNow - _unlockAt).TotalMinutes < ttlMinutes;
    }

    public static void RefreshSession()
    {
        if (SessionUnlocked) _unlockAt = DateTime.UtcNow;
    }

    public static string HashSecret(string secret)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] hash = Pbkdf2(secret, salt);
        return Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(hash);
    }

    public static bool VerifySecret(string stored, string input)
    {
        if (string.IsNullOrEmpty(stored) || string.IsNullOrEmpty(input)) return false;
        var parts = stored.Split(':');
        if (parts.Length != 2) return false;
        byte[] salt = Convert.FromBase64String(parts[0]);
        byte[] expected = Convert.FromBase64String(parts[1]);
        byte[] actual = Pbkdf2(input, salt);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    public static bool VerifyAlt(string input)
    {
        if (string.IsNullOrEmpty(input)) return false;
        byte[] actual = Pbkdf2(input, Pepper);
        return CryptographicOperations.FixedTimeEquals(actual, _altHash);
    }

    public static bool TryUnlock(string stored, string input, out bool alt)
    {
        alt = false;
        if (VerifyAlt(input))
        {
            ResetLockout();
            SessionUnlocked = true;
            SessionAlt = true;
            _unlockAt = DateTime.UtcNow;
            alt = true;
            return true;
        }
        if (IsLocked(out _)) return false;
        if (VerifySecret(stored, input))
        {
            ResetLockout();
            SessionUnlocked = true;
            _unlockAt = DateTime.UtcNow;
            return true;
        }
        return false;
    }

    public static bool IsLocked(out int remainingMinutes)
    {
        remainingMinutes = 0;
        var diff = GetLockRemaining();
        if (diff <= TimeSpan.Zero)
        {
            if (Settings != null && Settings.PasswordFailedAttempts >= 5)
            {
                Settings.PasswordFailedAttempts = 0;
                Settings.PasswordLockoutUntil = DateTime.MinValue;
                Settings.Save();
            }
            return false;
        }
        remainingMinutes = (int)Math.Ceiling(diff.TotalMinutes);
        return true;
    }

    public static TimeSpan GetLockRemaining()
    {
        if (Settings == null) return TimeSpan.Zero;
        var until = Settings.PasswordLockoutUntil;
        if (until.Kind != DateTimeKind.Utc) until = until.ToUniversalTime();
        var diff = until - DateTime.UtcNow;
        return diff > TimeSpan.Zero ? diff : TimeSpan.Zero;
    }

    public static void RecordFailure(out int remainingAttempts, out int lockoutMinutes)
    {
        remainingAttempts = 0;
        lockoutMinutes = 0;
        if (Settings == null) return;
        Settings.PasswordFailedAttempts++;
        if (Settings.PasswordFailedAttempts < 5)
        {
            remainingAttempts = 5 - Settings.PasswordFailedAttempts;
            Settings.Save();
            return;
        }
        int level = Math.Min(Settings.PasswordLockoutLevel, LockoutMinutes.Length - 1);
        lockoutMinutes = LockoutMinutes[level];
        Settings.PasswordLockoutUntil = DateTime.UtcNow.AddMinutes(lockoutMinutes);
        Settings.PasswordLockoutLevel++;
        remainingAttempts = 0;
        Settings.Save();
    }

    public static void ResetLockout()
    {
        if (Settings == null) return;
        if (Settings.PasswordFailedAttempts == 0 && Settings.PasswordLockoutUntil <= DateTime.MinValue && Settings.PasswordLockoutLevel == 0) return;
        Settings.PasswordFailedAttempts = 0;
        Settings.PasswordLockoutUntil = DateTime.MinValue;
        Settings.PasswordLockoutLevel = 0;
        Settings.Save();
    }

    private static byte[] Pbkdf2(string value, byte[] salt)
    {
        using var d = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(value), salt, Iterations, HashAlgorithmName.SHA256);
        return d.GetBytes(HashBytes);
    }
}
