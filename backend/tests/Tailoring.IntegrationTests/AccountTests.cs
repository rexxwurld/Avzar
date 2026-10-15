using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Auth;
using Tailoring.Application.Common;
using Tailoring.Domain.Access;
using Tailoring.Domain.Tenancy;
using Tailoring.Infrastructure.Persistence;
using Tailoring.Infrastructure.Services;
using Xunit;

namespace Tailoring.IntegrationTests;

/// <summary>Pure rules. No database.</summary>
public sealed class SessionRulesTests
{
    private static UserSession Fresh(string current, string? previous = null) => new()
    {
        CurrentTokenHash = current, PreviousTokenHash = previous,
        IdleExpiresAtUtc = DateTime.UtcNow.AddDays(1), AbsoluteExpiresAtUtc = DateTime.UtcNow.AddDays(10),
    };

    [Fact]
    public void Current_token_rotates()
        => Assert.Equal(RefreshDecision.Rotate, SessionRules.Decide(Fresh("aaa"), "aaa", DateTime.UtcNow));

    [Fact]
    public void Previous_token_is_reuse()
        => Assert.Equal(RefreshDecision.ReuseDetected, SessionRules.Decide(Fresh("bbb", "aaa"), "aaa", DateTime.UtcNow));

    [Fact]
    public void Unknown_token_is_rejected()
        => Assert.Equal(RefreshDecision.Unknown, SessionRules.Decide(Fresh("bbb", "aaa"), "zzz", DateTime.UtcNow));

    [Fact]
    public void Revoked_session_never_rotates_even_with_the_right_token()
    {
        var s = Fresh("aaa"); s.RevokedAtUtc = DateTime.UtcNow;
        Assert.Equal(RefreshDecision.Revoked, SessionRules.Decide(s, "aaa", DateTime.UtcNow));
    }

    [Fact]
    public void Idle_and_absolute_expiry_are_enforced()
    {
        var idle = Fresh("aaa"); idle.IdleExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        Assert.Equal(RefreshDecision.Expired, SessionRules.Decide(idle, "aaa", DateTime.UtcNow));
        var abs = Fresh("aaa"); abs.AbsoluteExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1);
        Assert.Equal(RefreshDecision.Expired, SessionRules.Decide(abs, "aaa", DateTime.UtcNow));
    }

    [Fact]
    public void Sliding_idle_window_never_passes_the_absolute_limit()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var absolute = now.AddDays(5);
        Assert.Equal(absolute, SessionRules.NextIdleExpiry(now, TimeSpan.FromDays(30), absolute));
        Assert.Equal(now.AddDays(2), SessionRules.NextIdleExpiry(now, TimeSpan.FromDays(2), absolute));
    }

    [Theory]
    [InlineData("")]
    [InlineData("nodot")]
    [InlineData("not-a-guid-not-a-guid-not-a-guid-x.abcdefghijklmnopqrstuvwxyz")]
    [InlineData("0123456789abcdef0123456789abcdef.short")]
    public void Malformed_refresh_tokens_do_not_parse(string token)
        => Assert.False(SessionRules.TryParseToken(token, out _, out _));

    [Fact]
    public void Well_formed_refresh_token_parses()
    {
        var id = Guid.NewGuid();
        Assert.True(SessionRules.TryParseToken($"{id:N}.{new string('x', 43)}", out var parsed, out var secret));
        Assert.Equal(id, parsed);
        Assert.Equal(43, secret.Length);
    }
}

public sealed class BusinessHoursRulesTests
{
    private static List<DayHoursInput> Week(Func<int, DayHoursInput?>? edit = null)
        => Enumerable.Range(0, 7).Select(d => edit?.Invoke(d) ?? (d == 0
            ? new DayHoursInput(0, false, null, null)
            : new DayHoursInput(d, true, new TimeOnly(9, 0), new TimeOnly(17, 0)))).ToList();

    [Fact]
    public void A_normal_week_is_valid() => Assert.Null(BusinessHoursRules.Validate(Week()));

    [Fact]
    public void Wrong_number_of_days_is_rejected()
        => Assert.NotNull(BusinessHoursRules.Validate(Week().Take(6).ToList()));

    [Fact]
    public void Duplicate_weekday_is_rejected()
        => Assert.NotNull(BusinessHoursRules.Validate(Week(d => d == 6 ? new DayHoursInput(5, true, new TimeOnly(9, 0), new TimeOnly(10, 0)) : null)));

    [Fact]
    public void Open_day_needs_times_in_order()
    {
        Assert.NotNull(BusinessHoursRules.Validate(Week(d => d == 1 ? new DayHoursInput(1, true, null, null) : null)));
        Assert.NotNull(BusinessHoursRules.Validate(Week(d => d == 1 ? new DayHoursInput(1, true, new TimeOnly(17, 0), new TimeOnly(9, 0)) : null)));
        Assert.NotNull(BusinessHoursRules.Validate(Week(d => d == 1 ? new DayHoursInput(1, true, new TimeOnly(9, 0), new TimeOnly(9, 0)) : null)));
    }

    [Fact]
    public void Closed_day_cannot_carry_times()
        => Assert.NotNull(BusinessHoursRules.Validate(Week(d => d == 0 ? new DayHoursInput(0, false, new TimeOnly(9, 0), null) : null)));

    [Fact]
    public void Holiday_overrides_the_weekly_pattern()
    {
        var week = Week();
        var monday = new DateOnly(2026, 10, 12);   // a Monday
        Assert.True(BusinessHoursRules.IsOpenOn(monday, week, new HashSet<DateOnly>()));
        Assert.False(BusinessHoursRules.IsOpenOn(monday, week, new HashSet<DateOnly> { monday }));
        Assert.False(BusinessHoursRules.IsOpenOn(new DateOnly(2026, 10, 11), week, new HashSet<DateOnly>())); // Sunday closed
    }
}

/// <summary>Real PostgreSQL plus real ASP.NET Core Identity, so token, lockout and 2FA behaviour is the framework's own.</summary>
public sealed class AccountTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private sealed class CapturingEmail : IEmailSender
    {
        public List<Tailoring.Application.Email.EmailRequest> Sent { get; } = [];
        public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
        { Sent.Add(new(to, subject, body)); return Task.CompletedTask; }
        public Task QueueAsync(Tailoring.Application.Email.EmailRequest request, CancellationToken ct = default)
        { Sent.Add(request); return Task.CompletedTask; }
    }

    private sealed class Harness : IAsyncDisposable
    {
        public ServiceProvider Provider = null!;
        public IServiceScope Scope = null!;
        public AppDbContext Db = null!;
        public UserManager<AppUser> Users = null!;
        public CapturingEmail Email = new();
        public AccountService Account = null!;
        public SessionService Sessions = null!;
        public ValueTask DisposeAsync() { Scope.Dispose(); return Provider.DisposeAsync(); }
    }

    private Harness Build(Guid? businessId = null, Guid? userId = null, AuthOptions? auth = null, IPermissionService? perms = null)
    {
        var db = fx.CreateFor(new FakeUserTenant(businessId, userId));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(db);
        services.AddSingleton<ITenantContext>(new FakeUserTenant(businessId, userId));
        services.AddDataProtection();
        services.AddIdentityCore<AppUser>(o =>
            {
                o.User.RequireUniqueEmail = true; o.Password.RequiredLength = 10; o.Password.RequireNonAlphanumeric = false;
                o.Lockout.MaxFailedAccessAttempts = 5; o.Lockout.AllowedForNewUsers = true;
                o.Tokens.PasswordResetTokenProvider = PasswordResetTokenProvider.ProviderName;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders()
            .AddTokenProvider<PasswordResetTokenProvider>(PasswordResetTokenProvider.ProviderName);
        services.Configure<PasswordResetTokenProviderOptions>(o => { o.Name = PasswordResetTokenProvider.ProviderName; o.TokenLifespan = TimeSpan.FromMinutes(30); });
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var authOptions = Options.Create(auth ?? new AuthOptions { EmailThrottleSeconds = 120 });
        var h = new Harness { Provider = provider, Scope = scope, Db = db, Users = users };
        h.Sessions = new SessionService(db, users, perms ?? new AllowAllPermissions(), authOptions, NullLogger<SessionService>.Instance);
        h.Account = new AccountService(db, users, h.Email, h.Sessions, new FakeUserTenant(businessId, userId), new AuditLogger(db),
            Options.Create(new AppOptions { WebBaseUrl = "https://app.example.com" }), authOptions, NullLogger<AccountService>.Instance);
        return h;
    }

    private static string Unique() => Guid.NewGuid().ToString("N")[..10];
    private const string Pw = "correct-horse-battery";

    private async Task<AppUser> NewUserAsync(Harness h, bool confirmed = false)
    {
        var u = new AppUser { UserName = $"{Unique()}@test.local", FullName = "Ada Test", EmailConfirmed = confirmed };
        u.Email = u.UserName;
        Assert.True((await h.Users.CreateAsync(u, Pw)).Succeeded);
        return u;
    }

    private static string TokenFromLink(string text)
    {
        var i = text.IndexOf("token=", StringComparison.Ordinal) + 6;
        var end = text.IndexOfAny(['\n', ' ', '\r'], i);
        return text[i..(end < 0 ? text.Length : end)];
    }

    // ---------- email verification ----------

    [Fact]
    public async Task Verification_email_link_confirms_the_address_and_is_throttled()
    {
        await using var h = Build();
        var u = await NewUserAsync(h);
        await h.Account.SendEmailVerificationAsync(u.Id);
        var mail = Assert.Single(h.Email.Sent);
        Assert.Equal(u.Email, mail.To);
        Assert.Contains("/verify-email?userId=" + u.Id, mail.TextBody);

        await h.Account.ConfirmEmailAsync(new ConfirmEmailRequest { UserId = u.Id, Token = TokenFromLink(mail.TextBody) });
        Assert.True((await h.Users.FindByIdAsync(u.Id.ToString()))!.EmailConfirmed);

        var again = await Assert.ThrowsAsync<AppException>(() => h.Account.SendEmailVerificationAsync(u.Id));
        Assert.Equal("email_already_confirmed", again.Code);
    }

    [Fact]
    public async Task Garbage_or_foreign_confirmation_tokens_are_rejected_with_one_error()
    {
        await using var h = Build();
        var a = await NewUserAsync(h); var b = await NewUserAsync(h);
        await h.Account.SendEmailVerificationAsync(a.Id);
        var tokenForA = TokenFromLink(h.Email.Sent[0].TextBody);

        foreach (var (id, token) in new[] { (a.Id, "%%%"), (b.Id, tokenForA), (Guid.NewGuid(), tokenForA) })
        {
            var ex = await Assert.ThrowsAsync<AppException>(() => h.Account.ConfirmEmailAsync(new ConfirmEmailRequest { UserId = id, Token = token }));
            Assert.Equal("invalid_token", ex.Code);
        }
    }

    // ---------- password reset ----------

    [Fact]
    public async Task Reset_flow_changes_the_password_once_and_ends_device_sessions()
    {
        var biz = Guid.NewGuid();
        await using var h = Build();
        var u = await NewUserAsync(h);
        var pair = await h.Sessions.CreateAsync(u.Id, biz, "Pixel");

        await h.Account.RequestPasswordResetAsync(u.Email!);
        var mail = Assert.Single(h.Email.Sent);
        var token = TokenFromLink(mail.TextBody);

        await h.Account.ResetPasswordAsync(new ResetPasswordRequest { UserId = u.Id, Token = token, NewPassword = "a-brand-new-password" });
        Assert.True(await h.Users.CheckPasswordAsync((await h.Users.FindByIdAsync(u.Id.ToString()))!, "a-brand-new-password"));

        // single use
        var reuse = await Assert.ThrowsAsync<AppException>(() =>
            h.Account.ResetPasswordAsync(new ResetPasswordRequest { UserId = u.Id, Token = token, NewPassword = "another-new-password-1" }));
        Assert.Equal("invalid_token", reuse.Code);

        // the device that was signed in can no longer refresh
        var refresh = await Assert.ThrowsAsync<AppException>(() => h.Sessions.RefreshAsync(pair.RefreshToken));
        Assert.Equal(401, refresh.StatusCode);
    }

    [Fact]
    public async Task Reset_request_for_an_unknown_address_sends_nothing_and_does_not_fail()
    {
        await using var h = Build();
        await h.Account.RequestPasswordResetAsync($"nobody-{Unique()}@test.local");
        Assert.Empty(h.Email.Sent);
    }

    [Fact]
    public async Task Reset_token_is_bound_to_the_account_and_a_weak_password_is_refused()
    {
        await using var h = Build();
        var a = await NewUserAsync(h); var b = await NewUserAsync(h);
        await h.Account.RequestPasswordResetAsync(a.Email!);
        var token = TokenFromLink(h.Email.Sent[0].TextBody);

        var wrongUser = await Assert.ThrowsAsync<AppException>(() =>
            h.Account.ResetPasswordAsync(new ResetPasswordRequest { UserId = b.Id, Token = token, NewPassword = "a-brand-new-password" }));
        Assert.Equal("invalid_token", wrongUser.Code);

        var weak = await Assert.ThrowsAsync<AppException>(() =>
            h.Account.ResetPasswordAsync(new ResetPasswordRequest { UserId = a.Id, Token = token, NewPassword = "short" }));
        Assert.Equal("weak_password", weak.Code);
    }

    [Fact]
    public async Task Confirmation_token_cannot_be_used_to_reset_a_password()
    {
        await using var h = Build();
        var u = await NewUserAsync(h);
        await h.Account.SendEmailVerificationAsync(u.Id);
        var confirmToken = TokenFromLink(h.Email.Sent[0].TextBody);
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            h.Account.ResetPasswordAsync(new ResetPasswordRequest { UserId = u.Id, Token = confirmToken, NewPassword = "a-brand-new-password" }));
        Assert.Equal("invalid_token", ex.Code);
    }

    [Fact]
    public async Task Change_password_needs_the_current_one()
    {
        await using var h = Build();
        var u = await NewUserAsync(h);
        var wrong = await Assert.ThrowsAsync<AppException>(() =>
            h.Account.ChangePasswordAsync(u.Id, new ChangePasswordRequest { CurrentPassword = "nope-nope-nope", NewPassword = "a-brand-new-password" }));
        Assert.Equal("wrong_password", wrong.Code);
        await h.Account.ChangePasswordAsync(u.Id, new ChangePasswordRequest { CurrentPassword = Pw, NewPassword = "a-brand-new-password" });
        Assert.True(await h.Users.CheckPasswordAsync(u, "a-brand-new-password"));
    }

    [Fact]
    public async Task Repeated_reset_and_verification_requests_are_throttled_per_address()
    {
        await using var h = Build();
        var u = await NewUserAsync(h);
        var real = new Tailoring.Infrastructure.Email.OutboxEmailSender(h.Db);
        var svc = new AccountService(h.Db, h.Users, real, h.Sessions, new FakeUserTenant(null, null), new AuditLogger(h.Db),
            Options.Create(new AppOptions()), Options.Create(new AuthOptions { EmailThrottleSeconds = 120 }), NullLogger<AccountService>.Instance);

        await svc.RequestPasswordResetAsync(u.Email!);
        await svc.RequestPasswordResetAsync(u.Email!);   // silently ignored, same outcome for the caller
        Assert.Equal(1, await h.Db.EmailOutbox.CountAsync(m => m.ToAddress == u.Email && m.TemplateKey == "password_reset"));

        await svc.SendEmailVerificationAsync(u.Id);
        var ex = await Assert.ThrowsAsync<AppException>(() => svc.SendEmailVerificationAsync(u.Id));
        Assert.Equal(429, ex.StatusCode);
    }

    // ---------- two-factor ----------

    private static string TotpNow(string base32Key)
    {
        // RFC 6238, SHA-1, 6 digits, 30 s: the same algorithm authenticator apps use.
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var clean = base32Key.Replace(" ", "").ToUpperInvariant();
        var bytes = new List<byte>(); int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c); bits += 5;
            if (bits >= 8) { bits -= 8; bytes.Add((byte)((buffer >> bits) & 0xFF)); }
        }
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var msg = BitConverter.GetBytes(counter); if (BitConverter.IsLittleEndian) Array.Reverse(msg);
        var hash = HMACSHA1.HashData(bytes.ToArray(), msg);
        var offset = hash[^1] & 0x0F;
        var code = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (code % 1_000_000).ToString("D6");
    }

    [Fact]
    public async Task Two_factor_setup_enable_and_disable()
    {
        await using var h = Build();
        var u = await NewUserAsync(h);

        var setup = await h.Account.BeginTwoFactorSetupAsync(u.Id);
        Assert.StartsWith("otpauth://totp/", setup.OtpAuthUri);
        Assert.Contains(setup.SharedKey, setup.OtpAuthUri);

        var bad = await Assert.ThrowsAsync<AppException>(() => h.Account.EnableTwoFactorAsync(u.Id, new EnableTwoFactorRequest { Code = "000000" }));
        Assert.Equal("invalid_code", bad.Code);
        Assert.False((await h.Account.GetSecurityAsync(u.Id)).TwoFactorEnabled);

        var codes = await h.Account.EnableTwoFactorAsync(u.Id, new EnableTwoFactorRequest { Code = TotpNow(setup.SharedKey) });
        Assert.Equal(10, codes.Codes.Count);
        var sec = await h.Account.GetSecurityAsync(u.Id);
        Assert.True(sec.TwoFactorEnabled);
        Assert.Equal(10, sec.RecoveryCodesLeft);

        // disabling needs the password AND a second factor
        var noCode = await Assert.ThrowsAsync<AppException>(() => h.Account.DisableTwoFactorAsync(u.Id, new DisableTwoFactorRequest { Password = Pw }));
        Assert.Equal("invalid_code", noCode.Code);
        var u2 = (await h.Users.FindByIdAsync(u.Id.ToString()))!;
        await h.Users.ResetAccessFailedCountAsync(u2);

        await h.Account.DisableTwoFactorAsync(u.Id, new DisableTwoFactorRequest { Password = Pw, RecoveryCode = codes.Codes[0] });
        Assert.False((await h.Account.GetSecurityAsync(u.Id)).TwoFactorEnabled);
        // the old authenticator secret is dead: a fresh setup gets a different key
        var again = await h.Account.BeginTwoFactorSetupAsync(u.Id);
        Assert.NotEqual(setup.SharedKey, again.SharedKey);
    }

    [Fact]
    public async Task A_recovery_code_works_exactly_once()
    {
        await using var h = Build();
        var u = await NewUserAsync(h);
        var setup = await h.Account.BeginTwoFactorSetupAsync(u.Id);
        var codes = await h.Account.EnableTwoFactorAsync(u.Id, new EnableTwoFactorRequest { Code = TotpNow(setup.SharedKey) });
        var user = (await h.Users.FindByIdAsync(u.Id.ToString()))!;

        Assert.True(await SecondFactorProbe.VerifyAsync(h.Users, user, null, codes.Codes[0]));
        Assert.False(await SecondFactorProbe.VerifyAsync(h.Users, user, null, codes.Codes[0]));
        Assert.True(await SecondFactorProbe.VerifyAsync(h.Users, user, null, codes.Codes[1]));
    }

    // ---------- sessions (refresh tokens) ----------

    [Fact]
    public async Task Refresh_rotates_and_replaying_the_old_token_kills_the_session()
    {
        var biz = Guid.NewGuid();
        await using var h = Build();
        var u = await NewUserAsync(h);
        var first = await h.Sessions.CreateAsync(u.Id, biz, "iPhone");

        var second = (await h.Sessions.RefreshAsync(first.RefreshToken)).Tokens;
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.Equal(first.SessionId, second.SessionId);

        // thief replays the first token
        var replay = await Assert.ThrowsAsync<AppException>(() => h.Sessions.RefreshAsync(first.RefreshToken));
        Assert.Equal("token_reuse_detected", replay.Code);

        // the legitimate newest token is now dead too
        var dead = await Assert.ThrowsAsync<AppException>(() => h.Sessions.RefreshAsync(second.RefreshToken));
        Assert.Equal(401, dead.StatusCode);
        Assert.False(await h.Sessions.IsActiveAsync(first.SessionId, u.Id));
    }

    [Fact]
    public async Task Parallel_refreshes_of_one_token_rotate_at_most_once()
    {
        var biz = Guid.NewGuid();
        await using var seed = Build();
        var u = await NewUserAsync(seed);
        var pair = await seed.Sessions.CreateAsync(u.Id, biz, "Pixel");

        var attempts = Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var h = Build();
            try { await h.Sessions.RefreshAsync(pair.RefreshToken); return "ok"; }
            catch (AppException ex) { return ex.Code; }
        });
        var results = await Task.WhenAll(attempts);
        Assert.Equal(1, results.Count(r => r == "ok"));
    }

    [Fact]
    public async Task Sessions_are_private_to_their_owner_and_can_be_revoked()
    {
        var biz = Guid.NewGuid();
        await using var h = Build();
        var a = await NewUserAsync(h); var b = await NewUserAsync(h);
        var pa = await h.Sessions.CreateAsync(a.Id, biz, "A phone");

        Assert.Single(await h.Sessions.ListAsync(a.Id));
        Assert.Empty(await h.Sessions.ListAsync(b.Id));

        var foreign = await Assert.ThrowsAsync<AppException>(() => h.Sessions.RevokeByIdAsync(b.Id, pa.SessionId));
        Assert.Equal("session_not_found", foreign.Code);
        Assert.True(await h.Sessions.IsActiveAsync(pa.SessionId, a.Id));

        await h.Sessions.RevokeByIdAsync(a.Id, pa.SessionId);
        Assert.False(await h.Sessions.IsActiveAsync(pa.SessionId, a.Id));
        Assert.Empty(await h.Sessions.ListAsync(a.Id));
    }

    [Fact]
    public async Task Logout_needs_a_real_token_and_is_idempotent()
    {
        var biz = Guid.NewGuid();
        await using var h = Build();
        var u = await NewUserAsync(h);
        var pair = await h.Sessions.CreateAsync(u.Id, biz, "Pixel");

        await h.Sessions.RevokeAsync($"{pair.SessionId:N}.{new string('z', 43)}"); // right session id, wrong secret: ignored
        Assert.True(await h.Sessions.IsActiveAsync(pair.SessionId, u.Id));

        await h.Sessions.RevokeAsync(pair.RefreshToken);
        await h.Sessions.RevokeAsync(pair.RefreshToken);
        Assert.False(await h.Sessions.IsActiveAsync(pair.SessionId, u.Id));
    }

    [Fact]
    public async Task Session_count_is_bounded_and_the_oldest_is_dropped()
    {
        var biz = Guid.NewGuid();
        await using var h = Build(auth: new AuthOptions { MaxSessionsPerUser = 2 });
        var u = await NewUserAsync(h);
        var s1 = await h.Sessions.CreateAsync(u.Id, biz, "one");
        await Task.Delay(20);
        var s2 = await h.Sessions.CreateAsync(u.Id, biz, "two");
        await Task.Delay(20);
        var s3 = await h.Sessions.CreateAsync(u.Id, biz, "three");

        Assert.False(await h.Sessions.IsActiveAsync(s1.SessionId, u.Id));
        Assert.True(await h.Sessions.IsActiveAsync(s2.SessionId, u.Id));
        Assert.True(await h.Sessions.IsActiveAsync(s3.SessionId, u.Id));
    }

    [Fact]
    public async Task A_deactivated_membership_cannot_refresh()
    {
        var biz = Guid.NewGuid();
        await using var seed = Build();
        var u = await NewUserAsync(seed);
        var pair = await seed.Sessions.CreateAsync(u.Id, biz, "Pixel");

        await using var h = Build(perms: new NoAccessPermissions());
        var ex = await Assert.ThrowsAsync<AppException>(() => h.Sessions.RefreshAsync(pair.RefreshToken));
        Assert.Equal("session_revoked", ex.Code);
        Assert.False(await h.Sessions.IsActiveAsync(pair.SessionId, u.Id));
    }

    [Fact]
    public async Task Creating_a_session_for_a_non_member_is_refused()
    {
        await using var h = Build(perms: new NoAccessPermissions());
        var u = await NewUserAsync(h);
        var ex = await Assert.ThrowsAsync<AppException>(() => h.Sessions.CreateAsync(u.Id, Guid.NewGuid(), "x"));
        Assert.Equal("not_a_member", ex.Code);
    }

    private sealed class NoAccessPermissions : IPermissionService
    {
        public Task<MembershipAccess?> GetAccessAsync(Guid userId, Guid businessId, CancellationToken ct = default)
            => Task.FromResult<MembershipAccess?>(null);
    }

    // ---------- export and deletion ----------

    [Fact]
    public async Task Export_lists_the_users_own_memberships_only()
    {
        await using var seedDb = Build();
        var a = await NewUserAsync(seedDb); var b = await NewUserAsync(seedDb);
        Guid bizA, bizB;
        await using (var db = fx.Create(null))
        {
            var ba = new Business { Name = "A-" + Unique() }; var bb = new Business { Name = "B-" + Unique() };
            db.Businesses.AddRange(ba, bb);
            db.Memberships.Add(new BusinessMembership { BusinessId = ba.Id, UserId = a.Id, DisplayName = "Ada", IsOwner = true });
            db.Memberships.Add(new BusinessMembership { BusinessId = bb.Id, UserId = b.Id, DisplayName = "Bob" });
            await db.SaveChangesAsync();
            bizA = ba.Id; bizB = bb.Id;
        }
        var export = await seedDb.Account.ExportAsync(a.Id);
        var m = Assert.Single(export.Memberships);
        Assert.Equal(bizA, m.BusinessId);
        Assert.NotEqual(bizB, m.BusinessId);
        Assert.Equal(a.Email, export.Email);
    }

    [Fact]
    public async Task Owners_cannot_delete_their_account_but_staff_are_anonymised_and_locked_out()
    {
        await using var h = Build();
        var owner = await NewUserAsync(h); var staff = await NewUserAsync(h);
        Guid biz;
        await using (var db = fx.Create(null))
        {
            var b = new Business { Name = "Del-" + Unique() };
            db.Businesses.Add(b);
            db.Memberships.Add(new BusinessMembership { BusinessId = b.Id, UserId = owner.Id, DisplayName = "Owner", IsOwner = true });
            db.Memberships.Add(new BusinessMembership { BusinessId = b.Id, UserId = staff.Id, DisplayName = "Staff Person" });
            await db.SaveChangesAsync();
            biz = b.Id;
        }

        var blocked = await Assert.ThrowsAsync<AppException>(() => h.Account.DeleteAccountAsync(owner.Id, new DeleteAccountRequest { Password = Pw }));
        Assert.Equal("owner_of_business", blocked.Code);

        var originalEmail = staff.Email!;
        var wrong = await Assert.ThrowsAsync<AppException>(() => h.Account.DeleteAccountAsync(staff.Id, new DeleteAccountRequest { Password = "not-the-password" }));
        Assert.Equal("wrong_password", wrong.Code);

        await h.Account.DeleteAccountAsync(staff.Id, new DeleteAccountRequest { Password = Pw });

        Assert.Null(await h.Users.FindByEmailAsync(originalEmail));
        var after = (await h.Users.FindByIdAsync(staff.Id.ToString()))!;
        Assert.Equal("Deleted user", after.FullName);
        Assert.Null(after.PasswordHash);
        Assert.True(await h.Users.IsLockedOutAsync(after));

        await using var check = fx.Create(null);
        var m = await check.Memberships.IgnoreQueryFilters().SingleAsync(x => x.UserId == staff.Id);
        Assert.Equal(MembershipStatus.Deactivated, m.Status);
        Assert.Equal("Deleted user", m.DisplayName);
        // the owner's membership in the same business is untouched
        Assert.Equal(MembershipStatus.Active, (await check.Memberships.IgnoreQueryFilters().SingleAsync(x => x.UserId == owner.Id)).Status);
        _ = biz;
    }
}

/// <summary>Test-only bridge to the internal second-factor helper (InternalsVisibleTo is not set up).</summary>
internal static class SecondFactorProbe
{
    public static async Task<bool> VerifyAsync(UserManager<AppUser> users, AppUser user, string? code, string? recovery)
    {
        if (!string.IsNullOrWhiteSpace(code))
            return await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, code.Replace(" ", ""));
        return !string.IsNullOrWhiteSpace(recovery) && (await users.RedeemTwoFactorRecoveryCodeAsync(user, recovery.Trim())).Succeeded;
    }
}

public sealed class BusinessSettingsTests(PostgresFixture fx) : IClassFixture<PostgresFixture>
{
    private static readonly Guid Actor = Guid.NewGuid();

    private async Task<Guid> NewBusinessAsync()
    {
        await using var db = fx.Create(null);
        var b = new Business { Name = "Hours-" + Guid.NewGuid().ToString("N")[..8], TimeZoneId = "Africa/Lagos" };
        db.Businesses.Add(b);
        await db.SaveChangesAsync();
        return b.Id;
    }

    private BusinessSettingsService Svc(Guid bid) { var db = fx.Create(bid); return new(db, new FakeUserTenant(bid, Actor), new AuditLogger(db)); }

    private static SetBusinessHoursRequest Week(string opens = "08:00", string closes = "18:00")
        => new() { Days = Enumerable.Range(0, 7).Select(d => d == 0 ? new DayHoursDto(0, false, null, null) : new DayHoursDto(d, true, opens, closes)).ToList() };

    [Fact]
    public async Task Never_saved_hours_show_defaults_and_store_nothing()
    {
        var bid = await NewBusinessAsync();
        var schedule = await Svc(bid).GetScheduleAsync();
        Assert.Equal("Africa/Lagos", schedule.TimeZoneId);
        Assert.Equal(7, schedule.Days.Count);
        Assert.False(schedule.Days[0].IsOpen);
        await using var db = fx.Create(bid);
        Assert.Equal(0, await db.BusinessHours.CountAsync());
    }

    [Fact]
    public async Task Hours_round_trip_and_can_be_replaced()
    {
        var bid = await NewBusinessAsync();
        var first = await Svc(bid).SetHoursAsync(Week());
        Assert.Equal("08:00", first.Days[1].OpensAt);
        var second = await Svc(bid).SetHoursAsync(Week("10:00", "16:30"));
        Assert.Equal("16:30", second.Days[3].ClosesAt);
        await using var db = fx.Create(bid);
        Assert.Equal(7, await db.BusinessHours.CountAsync());
    }

    [Fact]
    public async Task Invalid_hours_are_rejected_and_nothing_is_saved()
    {
        var bid = await NewBusinessAsync();
        var ex = await Assert.ThrowsAsync<AppException>(() => Svc(bid).SetHoursAsync(Week("18:00", "08:00")));
        Assert.Equal("invalid_hours", ex.Code);
        var bad = await Assert.ThrowsAsync<AppException>(() => Svc(bid).SetHoursAsync(Week("8am", "6pm")));
        Assert.Equal("invalid_hours", bad.Code);
        await using var db = fx.Create(bid);
        Assert.Equal(0, await db.BusinessHours.CountAsync());
    }

    [Fact]
    public async Task Holidays_are_unique_per_date_and_private_to_the_business()
    {
        var a = await NewBusinessAsync(); var b = await NewBusinessAsync();
        var date = new DateOnly(2026, 12, 25);
        var h = await Svc(a).AddHolidayAsync(new AddHolidayRequest { Date = date, Name = "Christmas" });

        var dup = await Assert.ThrowsAsync<AppException>(() => Svc(a).AddHolidayAsync(new AddHolidayRequest { Date = date, Name = "Again" }));
        Assert.Equal("holiday_exists", dup.Code);
        await Svc(b).AddHolidayAsync(new AddHolidayRequest { Date = date, Name = "Same day, other business" }); // allowed

        Assert.Single((await Svc(a).GetScheduleAsync()).Holidays);

        // another business's holiday id behaves like a missing one
        var foreign = await Assert.ThrowsAsync<AppException>(() => Svc(b).RemoveHolidayAsync(h.Id));
        Assert.Equal("holiday_not_found", foreign.Code);
        await Svc(a).RemoveHolidayAsync(h.Id);
        Assert.Empty((await Svc(a).GetScheduleAsync()).Holidays);
    }

    [Fact]
    public async Task Parallel_first_saves_leave_exactly_seven_rows()
    {
        var bid = await NewBusinessAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            try { await Svc(bid).SetHoursAsync(Week()); return "ok"; }
            catch (AppException ex) { return ex.Code; }
        }));
        Assert.Contains("ok", results);
        Assert.All(results, r => Assert.True(r is "ok" or "hours_conflict"));
        await using var db = fx.Create(bid);
        Assert.Equal(7, await db.BusinessHours.CountAsync());
    }
}
