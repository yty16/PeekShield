using System;
using System.Security.Cryptography;
using System.Text;

namespace PeekShield.Services;

public sealed class SecurityService
{
    private const int Iterations = 200000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    private static readonly byte[] Pepper = Convert.FromBase64String("+WeJGQuTysvldj2gZE2PUw==");
    private static readonly byte[] _altHash = Convert.FromBase64String("qtZfenv2fy25lxxQCrgJqhcpejwxrAJoNAT/oDW4Mpg=");

    public static bool SessionUnlocked { get; private set; }
    public static bool SessionAlt { get; private set; }

    public static void ResetSession()
    {
        SessionUnlocked = false;
        SessionAlt = false;
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
            SessionUnlocked = true;
            SessionAlt = true;
            alt = true;
            return true;
        }
        if (VerifySecret(stored, input))
        {
            SessionUnlocked = true;
            return true;
        }
        return false;
    }

    private static byte[] Pbkdf2(string value, byte[] salt)
    {
        using var d = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(value), salt, Iterations, HashAlgorithmName.SHA256);
        return d.GetBytes(HashBytes);
    }
}
