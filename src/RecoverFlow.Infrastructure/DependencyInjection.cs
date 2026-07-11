using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RecoverFlow.Application.Common;
using RecoverFlow.Application.Connect;
using RecoverFlow.Application.Recovery;
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

        services.AddScoped<PaymentRecoveryService>();
        services.AddScoped<RetryExecutionService>();
        services.AddScoped<IStripeWebhookProcessor, StripeWebhookProcessor>();
        services.AddScoped<IStripeInvoicePayer, StripeInvoicePayer>();
        services.AddScoped<IRetryJobScheduler, HangfireRetryJobScheduler>();

        services.AddSingleton<ITokenEncryptor, TokenEncryptor>();
        services.AddScoped<IStripeOAuthClient, StripeOAuthClient>();
        services.AddScoped<StripeConnectService>();

        return services;
    }
}
