using System.Security.Cryptography;

namespace Clipton.Core;

public static class HistoryAccessLockCredential
{
    public const int MinPinLength = 4;
    public const int MaxPinLength = 12;
    public const int DefaultTimeoutMinutes = 5;
    public static readonly int[] AllowedTimeoutMinutes = [0, 1, 5, 15, 30, 60];
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 210_000;

    public static bool IsValidPin(string? pin)
    {
        return pin is { Length: >= MinPinLength and <= MaxPinLength }
            && pin.All(char.IsDigit);
    }

    public static (string Salt, string Hash) Create(string pin)
    {
        if (!IsValidPin(pin))
        {
            throw new ArgumentException("PIN must be 4 to 12 digits.", nameof(pin));
        }

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = DeriveHash(pin, salt);
        return (Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public static bool Verify(string pin, string? saltBase64, string? hashBase64)
    {
        if (!IsValidPin(pin) || !HasCredential(saltBase64, hashBase64))
        {
            return false;
        }

        try
        {
            var salt = Convert.FromBase64String(saltBase64!);
            var expected = Convert.FromBase64String(hashBase64!);
            if (salt.Length != SaltSize || expected.Length != HashSize)
            {
                return false;
            }

            var actual = DeriveHash(pin, salt);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static bool HasCredential(string? saltBase64, string? hashBase64)
    {
        return IsExpectedBase64Length(saltBase64, SaltSize)
            && IsExpectedBase64Length(hashBase64, HashSize);
    }

    public static int NormalizeTimeoutMinutes(int minutes)
    {
        return AllowedTimeoutMinutes.Contains(minutes) ? minutes : DefaultTimeoutMinutes;
    }

    private static bool IsExpectedBase64Length(string? value, int decodedLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            return Convert.FromBase64String(value).Length == decodedLength;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] DeriveHash(string pin, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            pin,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            HashSize);
    }
}
