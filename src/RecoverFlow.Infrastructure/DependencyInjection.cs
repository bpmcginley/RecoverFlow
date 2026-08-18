using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RecoverFlow.Application.Audit;
using RecoverFlow.Application.Backtest;
using RecoverFlow.Application.Billing;
using RecoverFlow.Application.Common;
using RecoverFlow.Application.Connect;
using RecoverFlow.Application.Dashboard;
using RecoverFlow.Application.Recovery;
using RecoverFlow.Infrastructure.Email;
using RecoverFlow.Infrastructure.Jobs;
using RecoverFlow.Infrastructure.Persistence;
using RecoverFlow.Infrastructure.Security;
using RecoverFlow.Infrastructure.Stripe;

namespace RecoverFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<AppDbContext>(o => o
            .UseNpgsql(config["Database:ConnectionString"])
            .UseSnakeCaseNamingConvention());
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());

        services.AddScoped<PaymentRecoveryService>();
        services.AddScoped<MerchantDashboardService>();
        services.AddScoped<RetryExecutionService>();
        services.AddScoped<IStripeWebhookProcessor, StripeWebhookProcessor>();
        services.AddScoped<IStripeInvoicePayer, StripeInvoicePayer>();
        services.AddScoped<IRetryJobScheduler, HangfireRetryJobScheduler>();

        services.AddScoped<IEmailSender, SendGridEmailSender>();
        services.AddScoped<DunningEmailService>();
        services.AddScoped<SendGridEventProcessor>();

        services.AddScoped<MerchantBillingService>();
        services.AddScoped<IPlatformFeeInvoicer, StripePlatformFeeInvoicer>();

        services.AddScoped<AccountBacktestService>();
        services.AddScoped<IHistoricalInvoiceReader, StripeHistoricalInvoiceReader>();
        services.AddScoped<IBacktestJobScheduler, HangfireBacktestJobScheduler>();

        // Marketplace audit app. Read-only and stateless: no service, no scheduler, no
        // persistence — the controller reads, renders, emails, and forgets.
        services.AddScoped<IRetryWasteReader, StripeRetryWasteReader>();

        services.AddSingleton<ITokenEncryptor, TokenEncryptor>();
        // Registered by hand because StripeOAuthClient takes an optional HttpClient that only
        // tests supply; letting the container pick a constructor would be a silent coin toss.
        services.AddScoped<IStripeOAuthClient>(sp =>
            new StripeOAuthClient(sp.GetRequiredService<IOptions<StripeOptions>>()));
        services.AddScoped<StripeConnectService>();
        services.AddScoped<MerchantStripeTokenProvider>();

        return services;
    }
}
