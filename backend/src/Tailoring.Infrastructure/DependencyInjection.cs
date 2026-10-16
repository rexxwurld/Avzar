using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tailoring.Application.Abstractions;
using Tailoring.Application.Auth;
using Tailoring.Application.Billing;
using Tailoring.Application.Collections;
using Tailoring.Application.Customers;
using Tailoring.Application.Designs;
using Tailoring.Application.Finance;
using Tailoring.Application.Measurements;
using Tailoring.Application.Documents;
using Tailoring.Application.Media;
using Tailoring.Application.Orders;
using Tailoring.Application.Pricing;
using Tailoring.Application.Storefront;
using Tailoring.Application.Production;
using Tailoring.Application.Locations;
using Tailoring.Application.Roles;
using Tailoring.Application.Staff;
using Tailoring.Infrastructure.Persistence;
using Tailoring.Infrastructure.Services;
using Tailoring.Infrastructure.Storage;

namespace Tailoring.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Requires an <see cref="ITenantContext"/> to be registered by the host.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString, IConfiguration config)
    {
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
        services.AddDistributedMemoryCache();

        services.AddIdentity<AppUser, IdentityRole<Guid>>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 10;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequireDigit = false; o.Password.RequireUppercase = false; o.Password.RequireLowercase = false;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.Lockout.AllowedForNewUsers = true;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders()
            .AddTokenProvider<PasswordResetTokenProvider>(PasswordResetTokenProvider.ProviderName);

        var auth = config.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
        services.Configure<AuthOptions>(config.GetSection("Auth"));
        // Confirmation links last hours; reset links last minutes and use their own provider. Password changes are noticed by
        // web cookies within a minute (the default is 30), so a reset or a deactivation takes effect quickly.
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(Math.Max(1, auth.EmailVerificationHours)));
        services.Configure<PasswordResetTokenProviderOptions>(o =>
        {
            o.Name = PasswordResetTokenProvider.ProviderName;
            o.TokenLifespan = TimeSpan.FromMinutes(Math.Max(5, auth.PasswordResetMinutes));
        });
        services.Configure<IdentityOptions>(o => o.Tokens.PasswordResetTokenProvider = PasswordResetTokenProvider.ProviderName);
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));

        services.Configure<AppOptions>(config.GetSection("App"));
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<Tailoring.Application.Audit.IAuditReadService, AuditReadService>();
        services.AddScoped<IEntitlementService, EntitlementService>();
        services.AddScoped<IPlanCatalogService, PlanCatalogService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<IBusinessSettingsService, BusinessSettingsService>();
        services.AddScoped<IStaffService, StaffService>();
        services.AddScoped<IOwnershipService, OwnershipService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<ILocationService, LocationService>();
        services.AddScoped<Tailoring.Application.Locations.ILocationScope, LocationScope>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<Tailoring.Application.ImportExport.ICustomerImportService, CustomerImportService>();
        services.AddScoped<Tailoring.Application.ImportExport.ISearchService, SearchService>();
        services.AddScoped<IFittingService, FittingService>();
        services.AddScoped<IMeasurementService, MeasurementService>();
        services.AddScoped<IDesignService, DesignService>();
        services.AddScoped<IPricingService, PricingService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IExpenseService, ExpenseService>();
        services.AddScoped<IInventoryService, InventoryService>();

        services.Configure<StorefrontOptions>(config.GetSection("Storefront"));
        // The key ring lives in PostgreSQL so sessions, reset/verify links and preview links survive restarts and work across instances.
        // Keys are stored unencrypted at rest; protect the database accordingly (add ProtectKeysWithCertificate later if required).
        services.AddDataProtection().SetApplicationName("avzar").PersistKeysToDbContext<AppDbContext>();
        services.AddSingleton<IDnsTxtResolver, DnsClientTxtResolver>();
        services.AddHttpClient<ICaptchaVerifier, TurnstileCaptchaVerifier>(c => c.Timeout = TimeSpan.FromSeconds(8));
        services.AddHttpClient("ai", c => c.Timeout = TimeSpan.FromMinutes(2));
        services.AddHttpClient("paystack", c => c.Timeout = TimeSpan.FromSeconds(15));
        services.AddScoped<IStorefrontService, StorefrontService>();
        services.AddScoped<IPublicSiteService, PublicSiteService>();
        services.AddScoped<IInquiryService, InquiryService>();
        services.AddScoped<ICollectionService, CollectionService>();

        services.Configure<StorageOptions>(config.GetSection("Storage"));
        var storageProvider = (config["Storage:Provider"] ?? "local").Trim().ToLowerInvariant();
        switch (storageProvider)
        {
            case "local": services.AddSingleton<IObjectStorage, LocalDiskObjectStorage>(); break;
            case "s3":
                (config.GetSection("Storage:S3").Get<S3StorageOptions>() ?? new S3StorageOptions()).Validate(); // fail at startup, not at first upload
                services.AddSingleton<IObjectStorage, S3ObjectStorage>();
                break;
            default: throw new InvalidOperationException("Storage:Provider must be 'local' or 's3'.");
        }
        services.AddScoped<IOrphanFileCleaner, OrphanFileCleaner>();

        services.Configure<DocumentOptions>(config.GetSection("Documents"));
        services.Configure<AntivirusOptions>(config.GetSection("Antivirus"));
        var av = config.GetSection("Antivirus").Get<AntivirusOptions>() ?? new AntivirusOptions();
        av.Validate();
        if (av.Provider.Trim().Equals("clamav", StringComparison.OrdinalIgnoreCase)) services.AddSingleton<IVirusScanner, ClamAvScanner>();
        else services.AddSingleton<IVirusScanner, NoOpVirusScanner>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddHostedService<OrphanCleanupWorker>();
        services.AddSingleton<IImageProcessor, ImageSharpProcessor>();
        services.AddScoped<IMediaService, MediaService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IWorkflowService, WorkflowService>();
        services.AddScoped<IProductionPlanner, ProductionPlanner>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<IShopScheduleService, ShopScheduleService>();
        services.AddScoped<IWorkerScheduleService, WorkerScheduleService>();
        services.AddScoped<IMyTaskService, MyTaskService>();
        services.AddScoped<IWorkerService, WorkerService>();

        // PDFs are drawn on demand from live records (never stored, so they cannot go stale). Needs Pdf:QuestPdfLicense.
        services.Configure<Pdf.PdfOptions>(config.GetSection("Pdf"));
        services.AddSingleton<Tailoring.Application.Pdf.IPdfRenderer, Pdf.QuestPdfRenderer>();
        services.AddScoped<Tailoring.Application.Pdf.IPdfService, Pdf.PdfService>();
        services.AddScoped<Tailoring.Application.Pdf.IBrandingService, BrandingService>();

        // Redis is optional. Without a connection string nothing below is registered and limits stay per process.
        var redisCs = config["Redis:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(redisCs))
        {
            services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(_ =>
            {
                var o = StackExchange.Redis.ConfigurationOptions.Parse(redisCs);
                o.AbortOnConnectFail = false;   // the API must start even if Redis is down
                o.ConnectTimeout = 3000;
                o.SyncTimeout = 1000;
                return StackExchange.Redis.ConnectionMultiplexer.Connect(o);
            });
            services.AddSingleton<Coordination.IRedisCounter, Coordination.RedisCounter>();
            services.AddSingleton<Coordination.IRedisJobQueue, Coordination.RedisJobQueue>();
            services.AddHostedService<Coordination.RedisJobWorker>();
            services.AddStackExchangeRedisCache(o => { o.Configuration = redisCs; o.InstanceName = "avzar:"; });
        }

        // Email: durable outbox + background dispatcher + one transport chosen by configuration (Email:Provider = log | smtp | resend).
        var email = config.GetSection("Email").Get<Email.EmailOptions>() ?? new Email.EmailOptions();
        email.Validate();
        services.Configure<Email.EmailOptions>(config.GetSection("Email"));
        switch (email.Provider.Trim().ToLowerInvariant())
        {
            case "smtp": services.AddSingleton<Tailoring.Application.Email.IEmailTransport, Email.SmtpEmailTransport>(); break;
            case "resend": services.AddHttpClient<Tailoring.Application.Email.IEmailTransport, Email.ResendEmailTransport>(c => c.Timeout = TimeSpan.FromSeconds(15)); break;
            default: services.AddSingleton<Tailoring.Application.Email.IEmailTransport, Email.LoggingEmailTransport>(); break;
        }
        services.AddScoped<IEmailSender, Email.OutboxEmailSender>();
        services.AddScoped<Email.EmailDispatchService>();
        services.AddScoped<Tailoring.Application.Email.IEmailWebhookService, Email.EmailWebhookService>();
        services.AddHostedService<Email.EmailDispatcherWorker>();
        return services;
    }
}
