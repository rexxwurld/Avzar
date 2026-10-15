using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

/// <summary>RFC 6238 TOTP (HMAC-SHA1, 6 digits, 30 s): the algorithm authenticator apps use. Identity does not say which time step
/// matched, which replay protection needs, so the check is done here with the same window Identity uses (+/- 2 steps).</summary>
public static class Totp
{
    public const int StepSeconds = 30;
    public const int WindowSteps = 2;
    public const int Digits = 6;

    public static string Compute(byte[] key, long step)
    {
        Span<byte> message = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(message, step);
        var hash = HMACSHA1.HashData(key, message);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    public static long StepAt(DateTimeOffset moment) => moment.ToUnixTimeSeconds() / StepSeconds;

    /// <summary>The time step whose code equals <paramref name="code"/>, or null. Every step in the window is compared (no early exit).</summary>
    public static long? FindStep(byte[] key, string code, DateTimeOffset now)
    {
        if (code.Length != Digits || !code.All(char.IsAsciiDigit)) return null;
        var supplied = System.Text.Encoding.ASCII.GetBytes(code);
        long? match = null;
        var current = StepAt(now);
        for (var offset = -WindowSteps; offset <= WindowSteps; offset++)
        {
            var step = current + offset;
            var expected = System.Text.Encoding.ASCII.GetBytes(Compute(key, step));
            if (CryptographicOperations.FixedTimeEquals(expected, supplied)) match = step;
        }
        return match;
    }

    /// <summary>Identity stores the authenticator key as unpadded RFC 4648 base32. Returns null for anything else.</summary>
    public static byte[]? DecodeBase32(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var raw in text.Replace(" ", "").Replace("-", "").TrimEnd('=').ToUpperInvariant())
        {
            var value = alphabet.IndexOf(raw);
            if (value < 0) return null;
            buffer = (buffer << 5) | value; bits += 5;
            if (bits >= 8) { bits -= 8; bytes.Add((byte)((buffer >> bits) & 0xFF)); }
        }
        return bytes.Count == 0 ? null : bytes.ToArray();
    }
}

public static class TotpVerifier
{
    /// <summary>True when the code is valid AND its time step is newer than any step this user has already used. The claim is a
    /// single conditional UPDATE, so two parallel requests carrying the same code cannot both succeed.</summary>
    public static async Task<bool> VerifyAsync(AppDbContext db, UserManager<AppUser> users, AppUser user, string? code,
        DateTimeOffset? now = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        var key = Totp.DecodeBase32(await users.GetAuthenticatorKeyAsync(user));
        if (key is null) return false;

        var step = Totp.FindStep(key, code.Replace(" ", "").Replace("-", ""), now ?? DateTimeOffset.UtcNow);
        if (step is not { } matched) return false;

        var claimed = await db.Users
            .Where(u => u.Id == user.Id && (u.LastTotpStep == null || u.LastTotpStep < matched))
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastTotpStep, (long?)matched), ct);
        return claimed == 1;
    }

    /// <summary>Called when the authenticator key is replaced, because steps from the old key mean nothing for the new one.</summary>
    public static Task ClearAsync(AppDbContext db, Guid userId, CancellationToken ct = default)
        => db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.LastTotpStep, (long?)null), ct);
}
