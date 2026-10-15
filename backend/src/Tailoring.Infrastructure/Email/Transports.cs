using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tailoring.Application.Email;

namespace Tailoring.Infrastructure.Email;

/// <summary>Development only. Logs the whole message (including any links), so never use it where logs are shared.</summary>
public sealed class LoggingEmailTransport(ILogger<LoggingEmailTransport> log) : IEmailTransport
{
    public Task<TransportResult> SendAsync(OutboundEmail email, CancellationToken ct = default)
    {
        log.LogInformation("EMAIL (not delivered) to={To} subject={Subject}\n{Body}", email.To, email.Subject, email.TextBody);
        return Task.FromResult(TransportResult.Ok("log"));
    }
}

/// <summary>
/// SMTP via System.Net.Mail. Works with any SMTP relay (STARTTLS on 587 or implicit TLS is NOT supported by this class;
/// use 587/STARTTLS or a provider's HTTP API). If you need implicit TLS (465) or OAuth, swap in a MailKit transport behind IEmailTransport.
/// </summary>
public sealed class SmtpEmailTransport(IOptions<EmailOptions> options, ILogger<SmtpEmailTransport> log) : IEmailTransport
{
    public async Task<TransportResult> SendAsync(OutboundEmail email, CancellationToken ct = default)
    {
        var o = options.Value;
        try
        {
            using var msg = new MailMessage
            {
                From = new MailAddress(o.FromAddress, o.FromName),
                Subject = email.Subject,
                Body = email.TextBody,
                IsBodyHtml = false,
                SubjectEncoding = Encoding.UTF8,
                BodyEncoding = Encoding.UTF8,
            };
            msg.To.Add(new MailAddress(email.To));
            msg.Headers.Add("X-Avzar-Message-Id", email.MessageId.ToString());
            if (email.HtmlBody is not null)
                msg.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(email.HtmlBody, Encoding.UTF8, "text/html"));

            using var client = new SmtpClient(o.Smtp.Host, o.Smtp.Port)
            {
                EnableSsl = o.Smtp.UseStartTls,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 15_000,
            };
            if (!string.IsNullOrEmpty(o.Smtp.Username))
                client.Credentials = new NetworkCredential(o.Smtp.Username, o.Smtp.Password);

            await client.SendMailAsync(msg, ct);
            return TransportResult.Ok(null); // plain SMTP gives no provider message id
        }
        catch (SmtpException ex)
        {
            log.LogWarning("SMTP send failed: {Status}", ex.StatusCode);
            return Classify(ex.StatusCode) ? TransportResult.Retry($"SMTP {ex.StatusCode}") : TransportResult.Reject($"SMTP {ex.StatusCode}");
        }
        catch (Exception ex) when (ex is SocketException or IOException or TimeoutException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            return TransportResult.Retry($"SMTP connection problem: {ex.GetType().Name}");
        }
        catch (FormatException ex)
        {
            return TransportResult.Reject($"Invalid address or header: {ex.GetType().Name}");
        }
    }

    /// <summary>True when the failure is temporary (4xx-style or connectivity). 5xx codes are permanent.</summary>
    public static bool Classify(SmtpStatusCode code) => code is
        SmtpStatusCode.ServiceNotAvailable or SmtpStatusCode.MailboxBusy or SmtpStatusCode.LocalErrorInProcessing
        or SmtpStatusCode.InsufficientStorage or SmtpStatusCode.ClientNotPermitted or SmtpStatusCode.GeneralFailure;
}

/// <summary>Resend HTTP API (https://resend.com/docs/api-reference/emails/send-email). The message id is sent as the Idempotency-Key.</summary>
public sealed class ResendEmailTransport(HttpClient http, IOptions<EmailOptions> options, ILogger<ResendEmailTransport> log) : IEmailTransport
{
    public async Task<TransportResult> SendAsync(OutboundEmail email, CancellationToken ct = default)
    {
        var o = options.Value;
        var payload = new Dictionary<string, object?>
        {
            ["from"] = string.IsNullOrWhiteSpace(o.FromName) ? o.FromAddress : $"{o.FromName.Replace("\"", "")} <{o.FromAddress}>",
            ["to"] = new[] { email.To },
            ["subject"] = email.Subject,
            ["text"] = email.TextBody,
        };
        if (email.HtmlBody is not null) payload["html"] = email.HtmlBody;

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{o.Resend.BaseUrl.TrimEnd('/')}/emails")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.Resend.ApiKey);
        req.Headers.Add("Idempotency-Key", email.MessageId.ToString("N"));

        try
        {
            using var res = await http.SendAsync(req, ct);
            if (res.IsSuccessStatusCode)
            {
                try
                {
                    using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
                    return TransportResult.Ok(doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null);
                }
                catch (JsonException) { return TransportResult.Ok(null); } // accepted; id unreadable
            }

            var code = (int)res.StatusCode;
            log.LogWarning("Resend returned {Status} for message {MessageId}", code, email.MessageId);
            // 429 rate limit, 409 concurrent idempotent request, 5xx provider trouble: try again. Other 4xx will not improve by retrying.
            return code is 429 or 409 or >= 500 or 408
                ? TransportResult.Retry($"Resend HTTP {code}")
                : TransportResult.Reject($"Resend HTTP {code}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return TransportResult.Retry($"Resend request failed: {ex.GetType().Name}");
        }
    }
}
