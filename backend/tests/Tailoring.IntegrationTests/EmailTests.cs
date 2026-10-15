using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tailoring.Application.Common;
using Tailoring.Application.Email;
using Tailoring.Domain.Email;
using Tailoring.Infrastructure.Email;
using Tailoring.Infrastructure.Persistence;
using Xunit;

namespace Tailoring.IntegrationTests;

/// <summary>Pure rules. No database.</summary>
public sealed class EmailRulesTests
{
    [Fact]
    public void Retry_delay_grows_and_is_capped()
    {
        var delays = Enumerable.Range(1, 12).Select(EmailRetryPolicy.NextDelay).ToList();
        Assert.Equal(TimeSpan.FromSeconds(30), delays[0]);
        Assert.Equal(TimeSpan.FromSeconds(120), delays[1]);
        for (var i = 1; i < delays.Count; i++) Assert.True(delays[i] >= delays[i - 1]);
        Assert.Equal(TimeSpan.FromHours(6), delays[^1]);
        Assert.Equal(delays[0], EmailRetryPolicy.NextDelay(0)); // nonsense input is clamped
    }

    [Theory]
    [InlineData("ada@example.com")]
    [InlineData("a.b+tag@mail.example.co.ng")]
    public void Plain_addresses_are_accepted(string input)
    {
        Assert.True(EmailAddressRules.TryNormalize(input, out var n));
        Assert.Equal(input, n);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ada")]
    [InlineData("ada@localhost")]
    [InlineData("ada@@example.com")]
    [InlineData("Ada <ada@example.com>")]
    [InlineData("a@b.com,c@d.com")]
    [InlineData("ada@example.com\r\nBcc: evil@example.com")]
    public void Unusable_or_dangerous_addresses_are_rejected(string input)
        => Assert.False(EmailAddressRules.TryNormalize(input, out _));

    [Fact]
    public void Templates_encode_dynamic_values_and_keep_the_link_intact()
    {
        var mail = EmailTemplates.PasswordReset("<script>alert(1)</script>", "https://app.example.com/reset?token=a&b=c", 30);
        Assert.DoesNotContain("<script>", mail.Html);
        Assert.Contains("&lt;script&gt;", mail.Html);
        Assert.Contains("token=a&amp;b=c", mail.Html);          // HTML-encoded in the href
        Assert.Contains("https://app.example.com/reset?token=a&b=c", mail.Text); // raw in plain text
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("/relative/path")]
    [InlineData("ftp://example.com/x")]
    public void Templates_refuse_links_that_are_not_absolute_http(string link)
        => Assert.Throws<ArgumentException>(() => EmailTemplates.VerifyEmail("Ada", link, 24));

    [Fact]
    public void Templates_refuse_multiline_subjects()
        => Assert.Throws<ArgumentException>(() => EmailTemplates.Render("Hi\r\nBcc: x@y.com", "Hello", ["p"], "Go", "https://example.com", "f"));

    [Fact]
    public void Smtp_classification_separates_temporary_from_permanent()
    {
        Assert.True(SmtpEmailTransport.Classify(System.Net.Mail.SmtpStatusCode.MailboxBusy));
        Assert.True(SmtpEmailTransport.Classify(System.Net.Mail.SmtpStatusCode.ServiceNotAvailable));
        Assert.False(SmtpEmailTransport.Classify(System.Net.Mail.SmtpStatusCode.MailboxUnavailable));
        Assert.False(SmtpEmailTransport.Classify(System.Net.Mail.SmtpStatusCode.TransactionFailed));
    }

    [Fact]
    public void Options_fail_fast_when_a_real_provider_is_half_configured()
    {
        Assert.Throws<InvalidOperationException>(() => new EmailOptions { Provider = "resend", FromAddress = "no-reply@example.com" }.Validate());   // no key
        Assert.Throws<InvalidOperationException>(() => new EmailOptions { Provider = "smtp", FromAddress = "no-reply@example.com" }.Validate());     // no host
        Assert.Throws<InvalidOperationException>(() => new EmailOptions { Provider = "smtp", Smtp = { Host = "smtp.example.com" } }.Validate());     // no from
        Assert.Throws<InvalidOperationException>(() => new EmailOptions { Provider = "carrier-pigeon" }.Validate());
        new EmailOptions().Validate(); // defaults (log) are valid
        new EmailOptions { Provider = "resend", FromAddress = "no-reply@example.com", Resend = { ApiKey = "k" } }.Validate();
    }
}

/// <summary>Outbox + dispatcher against real PostgreSQL. Transports are scripted fakes; nothing leaves the machine.</summary>
public sealed class EmailDeliveryTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private sealed class ScriptedTransport(Func<OutboundEmail, int, TransportResult> script) : IEmailTransport
    {
        public readonly ConcurrentDictionary<Guid, int> Calls = new();
        public async Task<TransportResult> SendAsync(OutboundEmail email, CancellationToken ct = default)
        {
            var n = Calls.AddOrUpdate(email.MessageId, 1, (_, c) => c + 1);
            await Task.Delay(20, ct); // widen the race window for the concurrency test
            return script(email, n);
        }
    }

    private async Task ResetAsync()
    {
        await using var db = fx.Create(null);
        await db.EmailOutbox.ExecuteDeleteAsync();
    }

    private static EmailDispatchService Dispatcher(AppDbContext db, IEmailTransport t, int maxAttempts = 3, int batch = 20)
        => new(db, t, Options.Create(new EmailOptions { MaxAttempts = maxAttempts, BatchSize = batch, LeaseMinutes = 5 }), NullLogger<EmailDispatchService>.Instance);

    private async Task<Guid> QueueAsync(string to, string subject = "Hello")
    {
        await using var db = fx.Create(null);
        await new OutboxEmailSender(db).QueueAsync(new EmailRequest(to, subject, "Open https://app.example.com/x?token=secret", "<p>html</p>", null, "test"));
        return await db.EmailOutbox.AsNoTracking().Where(m => m.ToAddress == to).Select(m => m.Id).SingleAsync();
    }

    private async Task<EmailOutboxMessage> LoadAsync(Guid id)
    {
        await using var db = fx.Create(null);
        return await db.EmailOutbox.AsNoTracking().SingleAsync(m => m.Id == id);
    }

    [Fact]
    public async Task Queueing_persists_a_pending_message_without_sending()
    {
        await ResetAsync();
        var id = await QueueAsync("ada@example.com");
        var m = await LoadAsync(id);
        Assert.Equal(EmailStatus.Pending, m.Status);
        Assert.Equal(0, m.Attempts);
        Assert.Contains("token=secret", m.TextBody);
    }

    [Theory]
    [InlineData("not-an-email", "Hello", "invalid_email")]
    [InlineData("ada@example.com\r\nBcc: evil@example.com", "Hello", "invalid_email")]
    [InlineData("ada@example.com", "Hello\r\nBcc: evil@example.com", "invalid_email_subject")]
    [InlineData("ada@example.com", "   ", "invalid_email_subject")]
    public async Task Bad_input_is_rejected_and_nothing_is_stored(string to, string subject, string code)
    {
        await ResetAsync();
        await using var db = fx.Create(null);
        var ex = await Assert.ThrowsAsync<AppException>(() => new OutboxEmailSender(db).QueueAsync(new EmailRequest(to, subject, "body")));
        Assert.Equal(code, ex.Code);
        await using var check = fx.Create(null);
        Assert.Equal(0, await check.EmailOutbox.CountAsync());
    }

    [Fact]
    public async Task A_successful_send_marks_sent_and_purges_the_body()
    {
        await ResetAsync();
        var id = await QueueAsync("ok@example.com");
        var t = new ScriptedTransport((_, _) => TransportResult.Ok("prov-1"));
        await using (var db = fx.Create(null)) Assert.Equal(1, await Dispatcher(db, t).DispatchOnceAsync());

        var m = await LoadAsync(id);
        Assert.Equal(EmailStatus.Sent, m.Status);
        Assert.Equal("prov-1", m.ProviderMessageId);
        Assert.Equal(1, m.Attempts);
        Assert.Equal("", m.TextBody);          // one-time links must not linger
        Assert.Null(m.HtmlBody);
        Assert.NotNull(m.BodyPurgedAtUtc);
    }

    [Fact]
    public async Task A_temporary_failure_is_rescheduled_not_resent_early_then_delivered()
    {
        await ResetAsync();
        var id = await QueueAsync("retry@example.com");
        var t = new ScriptedTransport((_, n) => n == 1 ? TransportResult.Retry("provider busy") : TransportResult.Ok("prov-2"));
        var t0 = DateTime.UtcNow;

        await using (var db = fx.Create(null)) Assert.Equal(1, await Dispatcher(db, t).DispatchOnceAsync(t0));
        var afterFirst = await LoadAsync(id);
        Assert.Equal(EmailStatus.Pending, afterFirst.Status);
        Assert.Equal("provider busy", afterFirst.LastError);
        Assert.Contains("token=secret", afterFirst.TextBody);   // still needed for the retry
        Assert.True(afterFirst.NextAttemptUtc >= t0.AddSeconds(29));

        await using (var db = fx.Create(null)) Assert.Equal(0, await Dispatcher(db, t).DispatchOnceAsync(t0.AddSeconds(10)));   // not due yet
        await using (var db = fx.Create(null)) Assert.Equal(1, await Dispatcher(db, t).DispatchOnceAsync(t0.AddSeconds(40)));

        var done = await LoadAsync(id);
        Assert.Equal(EmailStatus.Sent, done.Status);
        Assert.Equal(2, done.Attempts);
        Assert.Equal(2, t.Calls[id]);
    }

    [Fact]
    public async Task Repeated_temporary_failures_end_in_a_dead_letter_after_the_attempt_limit()
    {
        await ResetAsync();
        var id = await QueueAsync("dead@example.com");
        var t = new ScriptedTransport((_, _) => TransportResult.Retry("still down"));
        var t0 = DateTime.UtcNow;
        for (var i = 0; i < 3; i++)
            await using (var db = fx.Create(null)) await Dispatcher(db, t, maxAttempts: 3).DispatchOnceAsync(t0.AddHours(i + 1));

        var m = await LoadAsync(id);
        Assert.Equal(EmailStatus.Failed, m.Status);
        Assert.Equal(3, m.Attempts);
        Assert.StartsWith("Gave up after 3 attempts", m.LastError);
        Assert.Equal("", m.TextBody);

        await using (var db = fx.Create(null)) Assert.Equal(0, await Dispatcher(db, t, 3).DispatchOnceAsync(t0.AddDays(2)));   // dead letters stay dead
        Assert.Equal(3, t.Calls[id]);
    }

    [Fact]
    public async Task A_permanent_rejection_fails_immediately_without_retrying()
    {
        await ResetAsync();
        var id = await QueueAsync("bounce@example.com");
        var t = new ScriptedTransport((_, _) => TransportResult.Reject("mailbox does not exist"));
        await using (var db = fx.Create(null)) await Dispatcher(db, t).DispatchOnceAsync();

        var m = await LoadAsync(id);
        Assert.Equal(EmailStatus.Failed, m.Status);
        Assert.Equal(1, m.Attempts);
        Assert.StartsWith("Rejected:", m.LastError);
    }

    [Fact]
    public async Task A_transport_that_throws_is_treated_as_temporary_and_the_message_is_kept()
    {
        await ResetAsync();
        var id = await QueueAsync("boom@example.com");
        var t = new ScriptedTransport((_, _) => throw new InvalidOperationException("kaboom"));
        await using (var db = fx.Create(null)) await Dispatcher(db, t).DispatchOnceAsync();

        var m = await LoadAsync(id);
        Assert.Equal(EmailStatus.Pending, m.Status);
        Assert.Equal(1, m.Attempts);
        Assert.DoesNotContain("kaboom", m.LastError);   // exception text is not stored
    }

    [Fact]
    public async Task Concurrent_dispatchers_send_each_message_exactly_once()
    {
        await ResetAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < 12; i++) ids.Add(await QueueAsync($"user{i}@example.com"));
        var t = new ScriptedTransport((_, _) => TransportResult.Ok("p"));

        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var db = fx.Create(null);
            var d = Dispatcher(db, t, batch: 3);
            while (await d.DispatchOnceAsync() > 0) { }
        }));

        foreach (var id in ids)
        {
            Assert.Equal(1, t.Calls[id]);
            Assert.Equal(EmailStatus.Sent, (await LoadAsync(id)).Status);
        }
    }

    [Fact]
    public async Task A_row_leased_by_another_dispatcher_is_skipped_until_the_lease_expires()
    {
        await ResetAsync();
        var id = await QueueAsync("lease@example.com");
        var now = DateTime.UtcNow;
        await using (var db = fx.Create(null))
            await db.EmailOutbox.Where(m => m.Id == id).ExecuteUpdateAsync(s => s.SetProperty(m => m.LeaseUntilUtc, now.AddMinutes(5)));

        var t = new ScriptedTransport((_, _) => TransportResult.Ok("p"));
        await using (var db = fx.Create(null)) Assert.Equal(0, await Dispatcher(db, t).DispatchOnceAsync(now));
        await using (var db = fx.Create(null)) Assert.Equal(1, await Dispatcher(db, t).DispatchOnceAsync(now.AddMinutes(6)));   // crashed dispatcher: lease expired
        Assert.Equal(EmailStatus.Sent, (await LoadAsync(id)).Status);
    }
}
