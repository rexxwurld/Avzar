using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailoring.Infrastructure.Persistence;

namespace Tailoring.Infrastructure.Services;

/// <summary>Separate options so password-reset tokens can live for minutes while email-confirmation tokens live for hours.</summary>
public sealed class PasswordResetTokenProviderOptions : DataProtectionTokenProviderOptions { }

public sealed class PasswordResetTokenProvider(
    IDataProtectionProvider dataProtection, IOptions<PasswordResetTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<AppUser>> logger)
    : DataProtectorTokenProvider<AppUser>(dataProtection, new OptionsWrapper<DataProtectionTokenProviderOptions>(options.Value), logger)
{
    public const string ProviderName = "PasswordReset";
}

internal static class LinkToken
{
    /// <summary>Identity tokens contain characters that are awkward in URLs; the link carries them base64url-encoded.</summary>
    public static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    public static bool TryDecode(string? encoded, out string token)
    {
        token = "";
        if (string.IsNullOrWhiteSpace(encoded) || encoded.Length > 2000) return false;
        try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded)); return token.Length > 0; }
        catch (FormatException) { return false; }
    }
}

internal static class SecondFactor
{
    /// <summary>True when the authenticator code or an unused recovery code is valid. A recovery code is consumed by a successful check.</summary>
    public static async Task<bool> VerifyAsync(UserManager<AppUser> users, AppUser user, string? code, string? recoveryCode)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            var clean = code.Replace(" ", "").Replace("-", "");
            return await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, clean);
        }
        if (!string.IsNullOrWhiteSpace(recoveryCode))
            return (await users.RedeemTwoFactorRecoveryCodeAsync(user, recoveryCode.Trim())).Succeeded;
        return false;
    }
}
