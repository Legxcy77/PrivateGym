using Microsoft.AspNetCore.Identity;
using PrivateGym.Models;

namespace PrivateGym.Services;

public static class PasswordHelper
{
    private static readonly PasswordHasher<User> _hasher = new();

    public static string HashPassword(User user, string plainPassword)
    {
        return _hasher.HashPassword(user, plainPassword);
    }

    /// <summary>
    /// Verifies password against ASP.NET Identity hash or legacy plaintext.
    /// Automatically upgrades legacy plaintext passwords to hashed format when matched.
    /// </summary>
    public static bool VerifyPassword(User user, string inputPassword, out bool needsRehash)
    {
        needsRehash = false;
        if (string.IsNullOrEmpty(user.PasswordHash) || string.IsNullOrEmpty(inputPassword))
            return false;

        // Legacy plaintext fallback (for seeded demo accounts before first login)
        if (!user.PasswordHash.StartsWith("AQAAAA", StringComparison.Ordinal))
        {
            if (user.PasswordHash == inputPassword)
            {
                needsRehash = true;
                return true;
            }
            return false;
        }

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, inputPassword);
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            needsRehash = true;
            return true;
        }
        return result == PasswordVerificationResult.Success;
    }
}

