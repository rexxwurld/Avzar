using System.Net;
using System.Net.Mail;

namespace Tailoring.Application.Email;

/// <summary>What a caller asks for. Queued durably; delivery happens in the background.</summary>
public sealed record EmailRequest(string To, string Subject, string TextBody, string? HtmlBody = null, Guid? BusinessId = null, string? TemplateKey = null);

public sealed record OutboundEmail(Guid MessageId, string To, string Subject, string TextBody, string? HtmlBody);

/// <summary>Outcome of ONE delivery attempt. Transient failures are retried; permanent ones are dead-lettered.</summary>
public sealed record TransportResult(bool Success, bool Transient, string? ProviderMessageId, string? Error)
{
    public static TransportResult Ok(string? providerId) => new(true, false, providerId, null);
    public static TransportResult Retry(string error) => new(false, true, null, Truncate(error));
    public static TransportResult Reject(string error) => new(false, false, null, Truncate(error));
    private static string Truncate(string s) => s.Length <= 400 ? s : s[..400];
}

/// <summary>Provider-specific delivery (SMTP, Resend, log). Must not throw for delivery problems; return a classified result.</summary>
public interface IEmailTransport
{
    Task<TransportResult> SendAsync(OutboundEmail email, CancellationToken ct = default);
}

public static class EmailAddressRules
{
    /// <summary>Accepts a plain address only: no display name, no whitespace, no header-injection characters.</summary>
    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(input)) return false;
        var s = input.Trim();
        if (s.Length > 254 || s.IndexOfAny(['\r', '\n', ' ', ',', ';', '<', '>', '"']) >= 0) return false;
        if (!MailAddress.TryCreate(s, out var parsed) || parsed.Address != s) return false;
        if (!parsed.Host.Contains('.') || parsed.Host.StartsWith('.') || parsed.Host.EndsWith('.')) return false;
        normalized = s;
        return true;
    }

    public static bool IsSafeHeaderValue(string? v) => v is not null && v.IndexOfAny(['\r', '\n']) < 0;
}

/// <summary>Pure backoff schedule: 30s, 2m, 8m, ~32m, ~2h, then capped at 6h.</summary>
public static class EmailRetryPolicy
{
    public static TimeSpan NextDelay(int attemptsMade)
    {
        if (attemptsMade < 1) attemptsMade = 1;
        var seconds = 30d * Math.Pow(4, Math.Min(attemptsMade - 1, 8));
        return TimeSpan.FromSeconds(Math.Min(seconds, TimeSpan.FromHours(6).TotalSeconds));
    }
}

public sealed record RenderedEmail(string Subject, string Text, string Html);

/// <summary>Pure templates. Every dynamic value is HTML-encoded; links must be absolute http(s) URLs.</summary>
public static class EmailTemplates
{
    public static RenderedEmail StaffInvitation(string fullName, string link, int validDays)
        => Render("You've been invited to join a workshop on Avzar", $"Hello {fullName},",
            [$"You have been invited to join a workshop on Avzar. The invitation is valid for {validDays} days."],
            "Accept invitation", link, "If you were not expecting this invitation, you can ignore this email.");

    public static RenderedEmail VerifyEmail(string fullName, string link, int validHours)
        => Render("Confirm your email address", $"Hello {fullName},",
            [$"Please confirm your email address to finish setting up your account. The link is valid for {validHours} hours."],
            "Confirm email", link, "If you did not create an account, you can ignore this email.");

    public static RenderedEmail PasswordReset(string fullName, string link, int validMinutes)
        => Render("Reset your password", $"Hello {fullName},",
            [$"We received a request to reset your password. The link is valid for {validMinutes} minutes and works once."],
            "Choose a new password", link, "If you did not ask for this, ignore this email; your password will not change.");

    public static RenderedEmail Render(string subject, string greeting, IReadOnlyList<string> paragraphs, string buttonLabel, string link, string footer)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Link must be an absolute http(s) URL.", nameof(link));
        if (!EmailAddressRules.IsSafeHeaderValue(subject)) throw new ArgumentException("Subject must be a single line.", nameof(subject));

        var text = string.Join("\n\n", new[] { greeting }.Concat(paragraphs).Append($"{buttonLabel}: {link}").Append(footer));

        static string E(string s) => WebUtility.HtmlEncode(s);
        var body = string.Concat(paragraphs.Select(p => $"<p style=\"margin:0 0 16px\">{E(p)}</p>"));
        var html = "<!doctype html><html><body style=\"margin:0;padding:24px;background:#f6f5f2;font-family:Arial,Helvetica,sans-serif;color:#1f1f1f\">"
            + "<div style=\"max-width:520px;margin:0 auto;background:#ffffff;padding:28px;border-radius:8px\">"
            + $"<p style=\"margin:0 0 16px\">{E(greeting)}</p>{body}"
            + $"<p style=\"margin:24px 0\"><a href=\"{E(link)}\" style=\"background:#1f3a5f;color:#ffffff;padding:12px 20px;border-radius:6px;text-decoration:none;display:inline-block\">{E(buttonLabel)}</a></p>"
            + $"<p style=\"margin:0 0 8px;font-size:13px;color:#555\">If the button does not work, copy this link into your browser:<br>{E(link)}</p>"
            + $"<p style=\"margin:16px 0 0;font-size:13px;color:#555\">{E(footer)}</p>"
            + "</div></body></html>";
        return new RenderedEmail(subject, text, html);
    }
}
