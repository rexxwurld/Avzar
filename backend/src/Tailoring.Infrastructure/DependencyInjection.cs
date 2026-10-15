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

        services.AddIdentity<AppUser, IdentityRole<Guid>>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 10;
                o.Password.RequireNonAlphanumeric = false;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.Lockout.AllowedForNewUsers = true;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        services.Configure<AppOptions>(config.GetSection("App"));
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IEntitlementService, EntitlementService>();
        services.AddScoped<IPlanCatalogService, PlanCatalogService>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IStaffService, StaffService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<ILocationService, LocationService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IFittingService, FittingService>();
        services.AddScoped<IMeasurementService, MeasurementService>();
        services.AddScoped<IDesignService, DesignService>();
        services.AddScoped<IPricingService, PricingService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IExpenseService, ExpenseService>();

        services.Configure<StorefrontOptions>(config.GetSection("Storefront"));
        services.AddDataProtection();   // production: persist keys (see docs/RUNBOOK.md) so preview links survive restarts and work across instances
        services.AddSingleton<IDnsTxtResolver, DnsClientTxtResolver>();
        services.AddHttpClient<ICaptchaVerifier, TurnstileCaptchaVerifier>(c => c.Timeout = TimeSpan.FromSeconds(8));
        services.AddScoped<IStorefrontService, StorefrontService>();
        services.AddScoped<IPublicSiteService, PublicSiteService>();
        services.AddScoped<IInquiryService, InquiryService>();
        services.AddScoped<ICollectionService, CollectionService>();

        services.Configure<StorageOptions>(config.GetSection("Storage"));
        if (!string.Equals(config["Storage:Provider"] ?? "local", "local", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only Storage:Provider=local is implemented. An S3-compatible provider is a deployment-hardening task.");
        services.AddSingleton<IObjectStorage, LocalDiskObjectStorage>();
        services.AddSingleton<IImageProcessor, ImageSharpProcessor>();
        services.AddScoped<IMediaService, MediaService>();
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IWorkflowService, WorkflowService>();
        services.AddScoped<IProductionPlanner, ProductionPlanner>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<IMyTaskService, MyTaskService>();
        services.AddScoped<IWorkerService, WorkerService>();

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
        services.AddHostedService<Email.EmailDispatcherWorker>();
        return services;
    }
}
