using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tailoring.Api.Infrastructure;
using Tailoring.Api.Security;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Auth;
using Tailoring.Infrastructure;
using Tailoring.Infrastructure.Coordination;
using Tailoring.Infrastructure.Persistence;
using Tailoring.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);
var isDev = builder.Environment.IsDevelopment();

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured (set ConnectionStrings__Default).");

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddScoped<SessionSigner>();
builder.Services.AddInfrastructure(connectionString, builder.Configuration);

// Cookie session for the web app (HttpOnly, SameSite=Lax). The mobile app uses short-lived JWTs plus rotating refresh tokens
// (enabled only when Auth:JwtSigningKey is set, 32+ characters).
var cookieSecure = isDev ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "tailoring.session";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = cookieSecure;
    o.ExpireTimeSpan = TimeSpan.FromHours(12);
    o.SlidingExpiration = true;
    o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
});

builder.Services.AddAntiforgery(o =>
{
    o.HeaderName = "X-XSRF-TOKEN";
    o.Cookie.Name = "tailoring.csrf";
    o.Cookie.SecurePolicy = cookieSecure;
    o.Cookie.SameSite = SameSiteMode.Lax;
});

var authOptions = builder.Configuration.GetSection("Auth").Get<Tailoring.Application.Abstractions.AuthOptions>() ?? new();
builder.Services.AddSingleton<JwtTokenIssuer>();
if (authOptions.MobileTokensEnabled)
{
    // "Smart" picks the scheme per request: a request with an Authorization: Bearer header is judged ONLY as a bearer request
    // (an invalid token never falls back to the cookie); everything else uses the Identity cookie.
    builder.Services.AddAuthentication(o =>
        {
            o.DefaultAuthenticateScheme = "Smart";
            o.DefaultChallengeScheme = "Smart";
        })
        .AddPolicyScheme("Smart", "Cookie or bearer", o => o.ForwardDefaultSelector = ctx =>
            ctx.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? JwtTokenIssuer.Scheme : IdentityConstants.ApplicationScheme)
        .AddJwtBearer(JwtTokenIssuer.Scheme, o =>
        {
            o.MapInboundClaims = false; // keep the claim names exactly as issued (name identifier, business_id, sid)
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = authOptions.JwtIssuer,
                ValidateAudience = true, ValidAudience = authOptions.JwtAudience,
                ValidateIssuerSigningKey = true, IssuerSigningKey = JwtTokenIssuer.Key(authOptions),
                ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            };
            o.Events = new JwtBearerEvents
            {
                // Revoking a device takes effect on its very next request, not when the access token expires.
                OnTokenValidated = async ctx =>
                {
                    var ok = Guid.TryParse(ctx.Principal?.FindFirstValue(JwtTokenIssuer.SessionClaim), out var sessionId)
                        & Guid.TryParse(ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId);
                    if (!ok || !await ctx.HttpContext.RequestServices.GetRequiredService<ISessionService>().IsActiveAsync(sessionId, userId, ctx.HttpContext.RequestAborted))
                        ctx.Fail("Session is not active.");
                },
            };
        });
}

builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddAuthorization();

builder.Services.AddControllers(o => o.Filters.Add<CsrfFilter>());
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<AppExceptionHandler>();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // Counters live in Redis when Redis:ConnectionString is set (limits then hold across replicas); otherwise they are per process.
    string Ip(HttpContext c) => c.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    IRedisCounter? Redis(HttpContext c) => c.RequestServices.GetService<IRedisCounter>();
    o.AddPolicy("auth", c => RateLimitPolicies.Fixed(Redis(c), "auth", Ip(c), 10, TimeSpan.FromMinutes(1)));
    o.AddPolicy("upload", c => RateLimitPolicies.Fixed(Redis(c), "upload", c.User.Identity?.Name ?? Ip(c), 30, TimeSpan.FromMinutes(1)));
    o.AddPolicy("public-submit", c => RateLimitPolicies.Fixed(Redis(c), "public-submit", Ip(c), 5, TimeSpan.FromMinutes(1)));
    o.AddPolicy("public", c => RateLimitPolicies.Fixed(Redis(c), "public", Ip(c), 600, TimeSpan.FromMinutes(1)));
});

var health = builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("postgres");
if (!string.IsNullOrWhiteSpace(builder.Configuration["Redis:ConnectionString"])) health.AddCheck<RedisHealthCheck>("redis");
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Behind a reverse proxy the client IP arrives in X-Forwarded-For. Only trust it when the API is reachable ONLY through that proxy,
// otherwise anyone could spoof an address and dodge the rate limits. Off by default.
if (app.Configuration.GetValue("Proxy:TrustForwardedHeaders", false))
{
    var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, ForwardLimit = 1 };
    forwarded.KnownIPNetworks.Clear();
    forwarded.KnownProxies.Clear();
    app.UseForwardedHeaders(forwarded);
}

app.UseExceptionHandler();
if (!isDev) app.UseHttpsRedirection(); // must come AFTER forwarded headers, or behind a TLS-terminating proxy (Render, Fly, Caddy) every request redirects forever
if (isDev)
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Development: always. Production: opt in with Database__MigrateOnStartup=true (hosts with no shell, e.g. Render). EF Core takes a database lock, so several instances starting together are safe.
if (isDev || app.Configuration.GetValue("Database:MigrateOnStartup", false))
{
    using var migrateScope = app.Services.CreateScope();
    await migrateScope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

// Plans are data. Seeding only inserts missing plans/keys and never overwrites admin edits.
// Development: always. Production: set Billing__SeedPlansOnStartup=true AFTER migrations have been applied.
if (app.Configuration.GetValue("Billing:SeedPlansOnStartup", isDev))
{
    using var seedScope = app.Services.CreateScope();
    await PlanSeeder.SyncAsync(seedScope.ServiceProvider.GetRequiredService<AppDbContext>());
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");
app.Run();

public partial class Program; // for WebApplicationFactory in later integration tests
