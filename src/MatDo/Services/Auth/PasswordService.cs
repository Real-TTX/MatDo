using System.Security.Cryptography;
using MatDo.Data;
using Microsoft.AspNetCore.Identity;

namespace MatDo.Services;

/// <summary>Password hashes (PBKDF2 with a salt, the format of ASP.NET Core) and the rules for a new password.</summary>
public sealed class PasswordService
{
    public const int MinLength = 10;

    private static readonly string DummyHash = new PasswordHasher<User>().HashPassword(null!, Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));

    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(null!, password);

    public bool Verify(string? hash, string password)
        => !string.IsNullOrEmpty(hash) && _hasher.VerifyHashedPassword(null!, hash, password) != PasswordVerificationResult.Failed;

    /// <summary>Something to compare against when an address does not exist, so that the answer takes as long as for a real account.</summary>
    public void SpendTime(string password) => _hasher.VerifyHashedPassword(null!, DummyHash, password);

    /// <summary>The message for a password that is not allowed, or <c>null</c>.</summary>
    public static string? Check(string? password, string? email = null)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
        {
            return $"Das Passwort braucht mindestens {MinLength} Zeichen.";
        }

        if (password.Length > 200)
        {
            return "Das Passwort ist zu lang.";
        }

        var local = email?.Split('@')[0];
        if (!string.IsNullOrEmpty(local) && local.Length >= 3 && password.Contains(local, StringComparison.OrdinalIgnoreCase))
        {
            return "Das Passwort darf die E-Mail-Adresse nicht enthalten.";
        }

        return null;
    }
}
