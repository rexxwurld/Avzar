using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tailoring.Application.Email;
using Tailoring.Domain.Email;
using Tailoring.Infrastructure.Email;
using Tailoring.Infrastructure.Persistence;
using Xunit;

namespace Tailoring.IntegrationTests;

/// <summary>Pure rules: signature scheme, event parsing, status precedence. No database.</summary>
public sealed class EmailWebhookRulesTests
{
    private const string Secret = "whsec_YXZ6YXItd2ViaG9vay10ZXN0LWtleS0wMTIzNDU2Nzg5"; // base64("avzar-webhook-test-key-0123456789")
    private const string Body = "{\"type\":\"email.delivered\",\"data\":{\"email_id\":\"abc\"}}";
    private const string Id = "msg_test_1";
    private const string Timestamp = "1760600000";
    // Computed independently with Python hmac/hashlib, not with the code under test.
    private const string KnownSignature = "fKVjMjJUEHueLvKFXzcIA/904F2F6Z+cGJBGUUNqCaI=";
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1760600000);

    private static byte[] Key() { Assert.True(SvixSignature.TryParseSecret(Secret, out var k)); return k; }

    [Fact]
    public void Signature_matches_an_independently_computed_value()
        => Assert.Equal(KnownSignature, SvixSignature.Sign(Key(), Id, Timestamp, Body));

    [Fact]
    public void A_correct_fresh_signature_verifies()
        => Assert.True(SvixSignature.Verify(Key(), Id, Timestamp, "v1," + KnownSignature, Body, Now, SvixSignature.DefaultTolerance));

    [Fact]
    public void Any_valid_signature_in_a_list_is_enough_and_unknown_versions_are_skipped()
    {
        Assert.True(SvixSignature.Verify(Key(), Id, Timestamp, "v1,AAAA v1," + KnownSignature, Body, Now, SvixSignature.DefaultTolerance));
        Assert.True(SvixSignature.Verify(Key(), Id, Timestamp, "v2,zzzz v1," + KnownSignature, Body, Now, SvixSignature.DefaultTolerance));
        Assert.False(SvixSignature.Verify(Key(), Id, Timestamp, "v2," + KnownSignature, Body, Now, SvixSignature.DefaultTolerance));
        Assert.False(SvixSignature.Verify(Key(), Id, Timestamp, KnownSignature, Body, Now, SvixSignature.DefaultTolerance)); // no version prefix
    }

    [Fact]
    public void A_changed_body_id_timestamp_or_key_fails()
    {
        var header = "v1," + KnownSignature;
        Assert.False(SvixSignature.Verify(Key(), Id, Timestamp, header, Body.Replace("delivered", "bounced"), Now, SvixSignature.DefaultTolerance));
        Assert.False(SvixSignature.Verify(Key(), "msg_other", Timestamp, header, Body, Now, SvixSignature.DefaultTolerance));
        Assert.False(SvixSignature.Verify(Key(), Id, "1760600001", header, Body, Now, SvixSignature.DefaultTolerance));
        var otherKey = Convert.FromBase64String("b3RoZXItc2VjcmV0LWtleS0wMTIzNDU2Nzg5MDEy");
        Assert.False(SvixSignature.Verify(otherKey, Id, Timestamp, header, Body, Now, SvixSignature.DefaultTolerance));
    }

    [Fact]
    public void Stale_and_future_timestamps_are_rejected_to_stop_replays()
    {
        var header = "v1," + KnownSignature;
        Assert.True(SvixSignature.Verify(Key(), Id, Timestamp, header, Body, Now.AddMinutes(4), SvixSignature.DefaultTolerance));
        Assert.False(SvixSignature.Verify(Key(), Id, Timestamp, header, Body, Now.AddMinutes(6), SvixSignature.DefaultTolerance));
        Assert.False(SvixSignature.Verify(Key(), Id, Timestamp, header, Body, Now.AddMinutes(-6), SvixSignature.DefaultTolerance));
    }

    [Theory]
    [InlineData(null, "1760600000", "v1,x")]
    [InlineData("", "1760600000", "v1,x")]
    [InlineData("id", null, "v1,x")]
    [InlineData("id", "not-a-number", "v1,x")]
    [InlineData("id", "-5", "v1,x")]
    [InlineData("id", "99999999999999999999", "v1,x")]
    [InlineData("id", "1760600000", null)]
    [InlineData("id", "1760600000", "")]
    public void Missing_or_malformed_headers_fail_without_throwing(string? id, string? ts, string? sig)
        => Assert.False(SvixSignature.Verify(Key(), id, ts, sig, Body, Now, SvixSignature.DefaultTolerance));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-secret")]
    [InlineData("whsec_%%%not-base64%%%")]
    [InlineData("whsec_AAAA")] // decodes to 3 bytes: too short to be a real key
    public void Unusable_secrets_are_rejected(string? secret) => Assert.False(SvixSignature.TryParseSecret(secret, out _));

    [Theory]
    [InlineData("email.delivered", "Delivered")]
    [InlineData("email.delivery_delayed", "Delayed")]
    [InlineData("email.bounced", "Bounced")]
    [InlineData("email.complained", "Complained")]
    [InlineData("email.sent", null)]
    [InlineData("email.opened", null)]
    [InlineData("domain.updated", null)]
    [InlineData(null, null)]
    public void Only_delivery_outcomes_are_tracked(string? type, string? expected) => Assert.Equal(expected, EmailDeliveryRules.StatusFor(type));

    [Theory]
    [InlineData(null, "Delayed", true)]
    [InlineData(null, "Delivered", true)]
    [InlineData("Delayed", "Delivered", true)]
    [InlineData("Delivered", "Bounced", true)]      // a late asynchronous bounce still counts
    [InlineData("Delayed", "Complained", true)]
    [InlineData("Delivered", "Delayed", false)]     // out-of-order: must not hide the delivery
    [InlineData("Delivered", "Delivered", false)]   // retry of the same event
    [InlineData("Bounced", "Delivered", false)]     // a bounce is never undone
    [InlineData("Bounced", "Complained", false)]    // first terminal report wins
    [InlineData("Complained", "Bounced", false)]
    public void A_report_only_moves_the_status_up(string? current, string incoming, bool apply)
        => Assert.Equal(apply, EmailDeliveryRules.ShouldApply(current, incoming));

    [Fact]
    public void Replaceable_statuses_agree_with_the_precedence_rule()
    {
        foreach (var incoming in new[] { "Delayed", "Delivered", "Bounced", "Complained" })
        foreach (var current in new[] { "Delayed", "Delivered", "Bounced", "Complained" })
            Assert.Equal(EmailDeliveryRules.ShouldApply(current, incoming), EmailDeliveryRules.ReplaceableBy(incoming).Contains(current));
    }

    [Fact]
    public void Events_parse_and_bounce_text_is_cleaned_and_capped()
    {
        var long_ = new string('x', 500);
        var json = "{\"type\":\"email.bounced\",\"data\":{\"email_id\":\"em_1\",\"bounce\":{\"message\":\"Mailbox\\r\\nfull " + long_ + "\"}}}";
        Assert.True(ResendWebhookEvent.TryParse(json, out var ev));
        Assert.Equal("email.bounced", ev.Type);
        Assert.Equal("em_1", ev.EmailId);
        Assert.NotNull(ev.Detail);
        Assert.DoesNotContain('\n', ev.Detail);
        Assert.DoesNotContain('\r', ev.Detail);
        Assert.Equal(300, ev.Detail!.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"type\":5}")]
    public void Malformed_payloads_do_not_parse(string json) => Assert.False(ResendWebhookEvent.TryParse(json, out _));

    [Fact]
    public void A_configured_webhook_secret_must_be_well_formed()
    {
        new EmailOptions { Provider = "log", Resend = { WebhookSecret = Secret } }.Validate();
        new EmailOptions { Provider = "log" }.Validate(); // empty = endpoint disabled, which is fine
        Assert.Throws<InvalidOperationException>(() => new EmailOptions { Provider = "log", Resend = { WebhookSecret = "oops" } }.Validate());
    }

    [Theory]
    [InlineData("email.complained", null, "Complained")]
    [InlineData("email.complained", "Permanent", "Complained")]
    [InlineData("email.bounced", "Permanent", "Bounced")]
    [InlineData("email.bounced", "permanent", "Bounced")]
    [InlineData("email.bounced", "Transient", null)]       // mailbox full, server down: must not block an address
    [InlineData("email.bounced", "Undetermined", null)]
    [InlineData("email.bounced", null, null)]
    [InlineData("email.delivered", "Permanent", null)]
    [InlineData("email.delivery_delayed", null, null)]
    [InlineData(null, "Permanent", null)]
    public void Only_permanent_bounces_and_complaints_suppress(string? type, string? bounceType, string? expected)
        => Assert.Equal(expected, EmailDeliveryRules.SuppressionReasonFor(type, bounceType));

    [Fact]
    public void The_bounce_type_is_parsed_and_absent_when_not_sent()
    {
        Assert.True(ResendWebhookEvent.TryParse("{\"type\":\"email.bounced\",\"data\":{\"email_id\":\"em_1\",\"bounce\":{\"type\":\"Permanent\",\"message\":\"x\"}}}", out var a));
        Assert.Equal("Permanent", a.BounceType);
        Assert.True(ResendWebhookEvent.TryParse("{\"type\":\"email.bounced\",\"data\":{\"email_id\":\"em_1\",\"bounce\":{\"message\":\"x\"}}}", out var b));
        Assert.Null(b.BounceType);
        Assert.True(ResendWebhookEvent.TryParse("{\"type\":\"email.bounced\",\"data\":{\"email_id\":\"em_1\",\"bounce\":{\"type\":7}}}", out var c));
        Assert.Null(c.BounceType);
    }

    [Theory]
    [InlineData("ada@example.com", "ada@example.com")]
    [InlineData("  Ada.Lovelace@Example.COM ", "ada.lovelace@example.com")]
    public void Suppression_keys_are_trimmed_and_lower_case(string input, string expected)
        => Assert.Equal(expected, EmailAddressRules.SuppressionKey(input));
}

/// <summary>The webhook service against a real PostgreSQL: signed events update the outbox, nothing else does.</summary>
public sealed class EmailWebhookServiceTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private const string Secret = "whsec_YXZ6YXItd2ViaG9vay10ZXN0LWtleS0wMTIzNDU2Nzg5";

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private EmailWebhookService Service(AppDbContext db, string secret = Secret)
        => new(db, Options.Create(new EmailOptions { Resend = { WebhookSecret = secret } }), NullLogger<EmailWebhookService>.Instance, new FixedClock(Now));

    private static (string Body, string Id, string Ts, string Sig) Signed(string type, string emailId, string? bounceMessage = null, DateTimeOffset? at = null, string? bounceType = null)
    {
        SvixSignature.TryParseSecret(Secret, out var key);
        var parts = new List<string>();
        if (bounceType is not null) parts.Add("\"type\":\"" + bounceType + "\"");
        if (bounceMessage is not null) parts.Add("\"message\":\"" + bounceMessage + "\"");
        var bounce = parts.Count == 0 ? "" : ",\"bounce\":{" + string.Join(",", parts) + "}";
        var body = "{\"type\":\"" + type + "\",\"data\":{\"email_id\":\"" + emailId + "\"" + bounce + "}}";
        var id = "msg_" + Guid.NewGuid().ToString("N");
        var ts = (at ?? Now).ToUnixTimeSeconds().ToString();
        return (body, id, ts, "v1," + SvixSignature.Sign(key, id, ts, body));
    }

    private async Task<WebhookOutcome> SendAsync(string type, string emailId, string? bounce = null, string? bounceType = null)
    {
        var e = Signed(type, emailId, bounce, bounceType: bounceType);
        await using var db = fx.Create(null);
        return await Service(db).HandleResendAsync(e.Body, e.Id, e.Ts, e.Sig);
    }

    private async Task<string> SeedAsync(string address = "ada@example.com")
    {
        var providerId = "em_" + Guid.NewGuid().ToString("N");
        await using var db = fx.Create(null);
        db.EmailOutbox.Add(new EmailOutboxMessage { ToAddress = address, Subject = "Hi", TextBody = "x", Status = EmailStatus.Sent, ProviderMessageId = providerId, SentAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return providerId;
    }

    private async Task<EmailOutboxMessage> LoadAsync(string providerId)
    {
        await using var db = fx.Create(null);
        return await db.EmailOutbox.AsNoTracking().SingleAsync(m => m.ProviderMessageId == providerId);
    }

    [Fact]
    public async Task Without_a_configured_secret_the_endpoint_is_disabled_and_nothing_changes()
    {
        var pid = await SeedAsync();
        var e = Signed("email.delivered", pid);
        await using var db = fx.Create(null);
        Assert.Equal(WebhookOutcome.Disabled, await Service(db, secret: "").HandleResendAsync(e.Body, e.Id, e.Ts, e.Sig));
        Assert.Null((await LoadAsync(pid)).DeliveryStatus);
    }

    [Fact]
    public async Task A_bad_signature_changes_nothing()
    {
        var pid = await SeedAsync();
        var e = Signed("email.bounced", pid);
        await using var db = fx.Create(null);
        Assert.Equal(WebhookOutcome.InvalidSignature, await Service(db).HandleResendAsync(e.Body.Replace("bounced", "delivered"), e.Id, e.Ts, e.Sig));
        Assert.Equal(WebhookOutcome.InvalidSignature, await Service(db).HandleResendAsync(e.Body, e.Id, e.Ts, "v1,AAAA"));
        Assert.Equal(WebhookOutcome.InvalidSignature, await Service(db).HandleResendAsync(e.Body, null, null, null));
        Assert.Null((await LoadAsync(pid)).DeliveryStatus);
    }

    [Fact]
    public async Task A_replayed_old_message_is_rejected_even_with_a_valid_signature()
    {
        var pid = await SeedAsync();
        var e = Signed("email.delivered", pid, at: Now.AddMinutes(-10));
        await using var db = fx.Create(null);
        Assert.Equal(WebhookOutcome.InvalidSignature, await Service(db).HandleResendAsync(e.Body, e.Id, e.Ts, e.Sig));
        Assert.Null((await LoadAsync(pid)).DeliveryStatus);
    }

    [Fact]
    public async Task A_delivery_report_is_recorded_and_a_duplicate_changes_nothing()
    {
        var pid = await SeedAsync();
        Assert.Equal(WebhookOutcome.Applied, await SendAsync("email.delivered", pid));
        var m = await LoadAsync(pid);
        Assert.Equal(EmailDeliveryStatus.Delivered, m.DeliveryStatus);
        Assert.NotNull(m.DeliveryUpdatedAtUtc);
        Assert.Equal(WebhookOutcome.NoChange, await SendAsync("email.delivered", pid));
    }

    [Fact]
    public async Task A_bounce_is_recorded_with_its_reason_and_is_never_undone()
    {
        var pid = await SeedAsync();
        Assert.Equal(WebhookOutcome.Applied, await SendAsync("email.delivery_delayed", pid));
        Assert.Equal(WebhookOutcome.Applied, await SendAsync("email.bounced", pid, "Mailbox does not exist"));
        Assert.Equal(WebhookOutcome.NoChange, await SendAsync("email.delivered", pid)); // late, out of order
        Assert.Equal(WebhookOutcome.NoChange, await SendAsync("email.complained", pid));
        var m = await LoadAsync(pid);
        Assert.Equal(EmailDeliveryStatus.Bounced, m.DeliveryStatus);
        Assert.Equal("Mailbox does not exist", m.DeliveryDetail);
    }

    [Fact]
    public async Task A_late_delayed_report_does_not_hide_a_delivery_but_a_later_bounce_replaces_it()
    {
        var pid = await SeedAsync();
        await SendAsync("email.delivered", pid);
        Assert.Equal(WebhookOutcome.NoChange, await SendAsync("email.delivery_delayed", pid));
        Assert.Equal(EmailDeliveryStatus.Delivered, (await LoadAsync(pid)).DeliveryStatus);
        Assert.Equal(WebhookOutcome.Applied, await SendAsync("email.bounced", pid));
        Assert.Equal(EmailDeliveryStatus.Bounced, (await LoadAsync(pid)).DeliveryStatus);
    }

    [Fact]
    public async Task Untracked_events_and_unknown_messages_are_acknowledged_without_effect()
    {
        var pid = await SeedAsync();
        Assert.Equal(WebhookOutcome.Ignored, await SendAsync("email.opened", pid));
        Assert.Equal(WebhookOutcome.UnknownMessage, await SendAsync("email.delivered", "em_" + Guid.NewGuid().ToString("N")));
        Assert.Null((await LoadAsync(pid)).DeliveryStatus);
    }

    [Fact]
    public async Task Parallel_conflicting_reports_converge_on_the_most_severe()
    {
        var pid = await SeedAsync();
        var tasks = new[] { "email.delivered", "email.bounced", "email.delivered", "email.delivery_delayed", "email.bounced" }.Select(t => SendAsync(t, pid));
        await Task.WhenAll(tasks);
        Assert.Equal(EmailDeliveryStatus.Bounced, (await LoadAsync(pid)).DeliveryStatus);
    }

    [Fact]
    public async Task One_message_report_never_touches_another_message()
    {
        var a = await SeedAsync();
        var b = await SeedAsync();
        await SendAsync("email.bounced", a);
        Assert.Null((await LoadAsync(b)).DeliveryStatus);
    }

    private static string UniqueAddress(string mixedCaseTag = "Case") => $"{mixedCaseTag}.{Guid.NewGuid():N}@Example.com";

    private async Task<List<EmailSuppression>> SuppressionsAsync(string address)
    {
        var key = EmailAddressRules.SuppressionKey(address);
        await using var db = fx.Create(null);
        return await db.EmailSuppressions.AsNoTracking().Where(x => x.Address == key).ToListAsync();
    }

    [Fact]
    public async Task A_permanent_bounce_suppresses_the_address_in_normalised_form()
    {
        var address = UniqueAddress();
        var pid = await SeedAsync(address);
        Assert.Equal(WebhookOutcome.Applied, await SendAsync("email.bounced", pid, "No such user", "Permanent"));
        var rows = await SuppressionsAsync(address);
        var row = Assert.Single(rows);
        Assert.Equal(EmailSuppressionReason.Bounced, row.Reason);
        Assert.Equal(address.ToLowerInvariant(), row.Address);
    }

    [Theory]
    [InlineData("Transient")]
    [InlineData("Undetermined")]
    [InlineData(null)]
    public async Task Other_bounces_do_not_suppress(string? bounceType)
    {
        var address = UniqueAddress();
        var pid = await SeedAsync(address);
        Assert.Equal(WebhookOutcome.Applied, await SendAsync("email.bounced", pid, "Mailbox full", bounceType));
        Assert.Empty(await SuppressionsAsync(address));
    }

    [Fact]
    public async Task A_complaint_suppresses_the_address()
    {
        var address = UniqueAddress();
        var pid = await SeedAsync(address);
        Assert.Equal(WebhookOutcome.Applied, await SendAsync("email.complained", pid));
        Assert.Equal(EmailSuppressionReason.Complained, Assert.Single(await SuppressionsAsync(address)).Reason);
    }

    [Fact]
    public async Task Replays_and_a_second_message_to_the_same_address_keep_a_single_row_with_the_first_reason()
    {
        var address = UniqueAddress();
        var first = await SeedAsync(address);
        var second = await SeedAsync(address);
        await SendAsync("email.bounced", first, null, "Permanent");
        await SendAsync("email.bounced", first, null, "Permanent");   // duplicate delivery
        await SendAsync("email.complained", second);                  // a later, different report for the same address
        var row = Assert.Single(await SuppressionsAsync(address));
        Assert.Equal(EmailSuppressionReason.Bounced, row.Reason);
    }

    [Fact]
    public async Task A_permanent_bounce_still_suppresses_when_the_status_was_already_Bounced()
    {
        // A transient bounce is recorded first (rank 3); the permanent one then changes no status but must still suppress.
        var address = UniqueAddress();
        var pid = await SeedAsync(address);
        Assert.Equal(WebhookOutcome.Applied, await SendAsync("email.bounced", pid, null, "Transient"));
        Assert.Empty(await SuppressionsAsync(address));
        Assert.Equal(WebhookOutcome.NoChange, await SendAsync("email.bounced", pid, null, "Permanent"));
        Assert.Single(await SuppressionsAsync(address));
    }

    [Fact]
    public async Task A_bad_signature_or_an_unknown_message_suppresses_nothing()
    {
        var address = UniqueAddress();
        var pid = await SeedAsync(address);
        var e = Signed("email.bounced", pid, null, bounceType: "Permanent");
        await using (var db = fx.Create(null))
            Assert.Equal(WebhookOutcome.InvalidSignature, await Service(db).HandleResendAsync(e.Body, e.Id, e.Ts, "v1,AAAA"));
        Assert.Equal(WebhookOutcome.UnknownMessage, await SendAsync("email.bounced", "em_" + Guid.NewGuid().ToString("N"), null, "Permanent"));
        Assert.Empty(await SuppressionsAsync(address));
        await using var all = fx.Create(null);
        Assert.False(await all.EmailSuppressions.AnyAsync(x => x.Address == EmailAddressRules.SuppressionKey(address)));
    }

    [Fact]
    public async Task Parallel_permanent_bounces_for_one_address_converge_on_one_row()
    {
        var address = UniqueAddress();
        var ids = new[] { await SeedAsync(address), await SeedAsync(address), await SeedAsync(address) };
        await Task.WhenAll(ids.SelectMany(id => new[] { SendAsync("email.bounced", id, null, "Permanent"), SendAsync("email.bounced", id, null, "Permanent") }));
        Assert.Single(await SuppressionsAsync(address));
    }
}

/// <summary>Through the real HTTP pipeline: no cookie, no CSRF token, only the signature.</summary>
[Collection("api")]
public sealed class EmailWebhookApiTests(ApiFactory factory)
{
    private static HttpRequestMessage Request(string body, string? id, string? ts, string? sig)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/email/resend") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (id is not null) req.Headers.Add("svix-id", id);
        if (ts is not null) req.Headers.Add("svix-timestamp", ts);
        if (sig is not null) req.Headers.Add("svix-signature", sig);
        return req;
    }

    private static (string Id, string Ts, string Sig) Sign(string body)
    {
        SvixSignature.TryParseSecret(ApiFactory.WebhookSecret, out var key);
        var id = "msg_" + Guid.NewGuid().ToString("N");
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        return (id, ts, "v1," + SvixSignature.Sign(key, id, ts, body));
    }

    [Fact]
    public async Task An_unsigned_call_is_refused_with_401_not_a_csrf_error()
    {
        using var http = factory.NewClient(cookies: false);
        using var res = await http.SendAsync(Request("{\"type\":\"email.delivered\"}", null, null, null));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task A_forged_signature_is_refused()
    {
        using var http = factory.NewClient(cookies: false);
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        using var res = await http.SendAsync(Request("{\"type\":\"email.delivered\"}", "msg_x", ts, "v1,AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task A_correctly_signed_call_updates_the_outbox_and_needs_no_cookie_or_csrf_token()
    {
        var pid = "em_" + Guid.NewGuid().ToString("N");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.EmailOutbox.Add(new EmailOutboxMessage { ToAddress = "ada@example.com", Subject = "Hi", TextBody = "x", Status = EmailStatus.Sent, ProviderMessageId = pid });
            await db.SaveChangesAsync();
        }

        var body = "{\"type\":\"email.bounced\",\"data\":{\"email_id\":\"" + pid + "\",\"bounce\":{\"message\":\"No such user\"}}}";
        var (id, ts, sig) = Sign(body);
        using var http = factory.NewClient(cookies: false);
        using var res = await http.SendAsync(Request(body, id, ts, sig));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        using var check = factory.Services.CreateScope();
        var row = await check.ServiceProvider.GetRequiredService<AppDbContext>().EmailOutbox.AsNoTracking().SingleAsync(m => m.ProviderMessageId == pid);
        Assert.Equal(EmailDeliveryStatus.Bounced, row.DeliveryStatus);
        Assert.Equal("No such user", row.DeliveryDetail);
    }

    [Fact]
    public async Task A_signed_event_for_an_unknown_message_is_acknowledged_so_the_provider_stops_retrying()
    {
        var body = "{\"type\":\"email.delivered\",\"data\":{\"email_id\":\"em_does_not_exist\"}}";
        var (id, ts, sig) = Sign(body);
        using var http = factory.NewClient(cookies: false);
        using var res = await http.SendAsync(Request(body, id, ts, sig));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}
