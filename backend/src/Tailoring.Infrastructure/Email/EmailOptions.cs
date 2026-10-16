namespace Tailoring.Infrastructure.Email;

/// <summary>Configuration section "Email". Secrets come from environment variables or a secret store, never from source.</summary>
public sealed class EmailOptions
{
    /// <summary>"log" (development only: writes the FULL message, links included, to the log), "smtp" or "resend".</summary>
    public string Provider { get; set; } = "log";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "Avzar";
    public int MaxAttempts { get; set; } = 6;
    public int BatchSize { get; set; } = 20;
    public int PollSeconds { get; set; } = 5;
    public int LeaseMinutes { get; set; } = 5;
    /// <summary>Set false on hosts that must not deliver (for example a second API replica you want quiet).</summary>
    public bool DispatcherEnabled { get; set; } = true;
    public SmtpOptions Smtp { get; set; } = new();
    public ResendOptions Resend { get; set; } = new();

    public sealed class SmtpOptions
    {
        public string Host { get; set; } = "";
        public int Port { get; set; } = 587;
        public bool UseStartTls { get; set; } = true;
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public sealed class ResendOptions
    {
        public string ApiKey { get; set; } = "";
        public string BaseUrl { get; set; } = "https://api.resend.com";
        /// <summary>Signing secret of the Resend webhook ("whsec_..."). Empty = the webhook endpoint is disabled (404).</summary>
        public string WebhookSecret { get; set; } = "";
    }

    /// <summary>Fail fast at startup instead of at the first email.</summary>
    public void Validate()
    {
        var p = Provider.Trim().ToLowerInvariant();
        if (p is not ("log" or "smtp" or "resend")) throw new InvalidOperationException($"Email:Provider '{Provider}' is not supported. Use log, smtp or resend.");
        if (MaxAttempts is < 1 or > 20) throw new InvalidOperationException("Email:MaxAttempts must be 1-20.");
        if (BatchSize is < 1 or > 200) throw new InvalidOperationException("Email:BatchSize must be 1-200.");
        if (PollSeconds < 1) throw new InvalidOperationException("Email:PollSeconds must be at least 1.");
        if (LeaseMinutes < 1) throw new InvalidOperationException("Email:LeaseMinutes must be at least 1.");
        if (!string.IsNullOrWhiteSpace(Resend.WebhookSecret) && !Tailoring.Application.Email.SvixSignature.TryParseSecret(Resend.WebhookSecret, out _))
            throw new InvalidOperationException("Email:Resend:WebhookSecret must look like whsec_ followed by base64 (copy it from the Resend webhook page).");
        if (p == "log") return;
        if (!Tailoring.Application.Email.EmailAddressRules.TryNormalize(FromAddress, out _)) throw new InvalidOperationException("Email:FromAddress must be a valid address when a real provider is used.");
        if (p == "smtp" && (string.IsNullOrWhiteSpace(Smtp.Host) || Smtp.Port is < 1 or > 65535)) throw new InvalidOperationException("Email:Smtp:Host and a valid Port are required.");
        if (p == "resend" && string.IsNullOrWhiteSpace(Resend.ApiKey)) throw new InvalidOperationException("Email:Resend:ApiKey is required.");
        if (p == "resend" && !Uri.TryCreate(Resend.BaseUrl, UriKind.Absolute, out var u)) throw new InvalidOperationException("Email:Resend:BaseUrl must be an absolute URL.");
    }
}
