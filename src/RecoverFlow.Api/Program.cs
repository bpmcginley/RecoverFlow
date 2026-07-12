using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecoverFlow.Api.Auth;
using RecoverFlow.Api.Middleware;
using RecoverFlow.Application.Billing;
using RecoverFlow.Application.Common;
using RecoverFlow.Infrastructure;
using RecoverFlow.Infrastructure.Persistence;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // PaaS hosts (Render, Heroku) inject the listening port via $PORT. Unset locally,
    // where Kestrel falls back to launchSettings.json.
    var port = Environment.GetEnvironmentVariable("PORT");
    if (!string.IsNullOrEmpty(port))
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

    // Managed Postgres is handed out as a URL (postgresql://user:pass@host/db), but
    // Npgsql only speaks key-value form. Normalize once so EF and Hangfire both get it.
    builder.Configuration["Database:ConnectionString"] =
        NormalizePostgresConnectionString(builder.Configuration["Database:ConnectionString"]);

    builder.Host.UseSerilog((ctx, cfg) => cfg
        .ReadFrom.Configuration(ctx.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console());

    // Render terminates TLS at its edge and forwards over HTTP with X-Forwarded-* headers.
    // Honor them so the request scheme reads as HTTPS (needed for correct OAuth redirects).
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        o.KnownNetworks.Clear();
        o.KnownProxies.Clear();
    });

    builder.Services.Configure<StripeOptions>(builder.Configuration.GetSection(StripeOptions.Section));
    builder.Services.Configure<RetryOptions>(builder.Configuration.GetSection(RetryOptions.Section));
    builder.Services.Configure<EncryptionOptions>(builder.Configuration.GetSection(EncryptionOptions.Section));
    builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.Section));
    builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.Section));
    builder.Services.Configure<BillingOptions>(builder.Configuration.GetSection(BillingOptions.Section));

    // Stripe.net calls (including the OAuth token exchange) authenticate using this
    // static platform key rather than per-request options.
    Stripe.StripeConfiguration.ApiKey = builder.Configuration[$"{StripeOptions.Section}:SecretKey"];

    builder.Services.AddDataProtection();
    builder.Services.AddInfrastructure(builder.Configuration);

    // Passwordless dashboard auth: a signed magic-link token drops this cookie.
    builder.Services.AddSingleton<MagicLinkTokenService>();
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(o =>
        {
            o.Cookie.Name = "rf_session";
            o.Cookie.HttpOnly = true;
            o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.ExpireTimeSpan = TimeSpan.FromDays(14);
            o.SlidingExpiration = true;
            // The dashboard is fetch-driven: an unauthenticated API/auth call must get a
            // 401 it can react to, not a 302 to an HTML login page the fetch can't follow.
            o.Events.OnRedirectToLogin = ctx =>
            {
                if (ctx.Request.Path.StartsWithSegments("/v1") || ctx.Request.Path.StartsWithSegments("/auth"))
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                else
                    ctx.Response.Redirect(ctx.RedirectUri);
                return Task.CompletedTask;
            };
        });
    builder.Services.AddAuthorization();

    builder.Services.AddHangfire(cfg => cfg
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(o =>
            o.UseNpgsqlConnection(builder.Configuration["Database:ConnectionString"])));
    builder.Services.AddHangfireServer();

    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    builder.Services.AddHealthChecks();

    var app = builder.Build();

    // Apply any pending EF migrations on boot so a fresh managed database self-provisions.
    using (var scope = app.Services.CreateScope())
    {
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();

        // Monthly billing (1st, 06:00 UTC). DI-resolved manager rather than the static
        // RecurringJob API — JobStorage.Current isn't reliably set this early. The job
        // registration persists in Postgres, so flipping Enabled off must actively remove it.
        var recurring = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
        if (scope.ServiceProvider.GetRequiredService<IOptions<BillingOptions>>().Value.Enabled)
            recurring.AddOrUpdate<MerchantBillingService>(
                "monthly-billing",
                s => s.RunMonthlyBillingAsync(CancellationToken.None),
                "0 6 1 * *",
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
        else
            recurring.RemoveIfExists("monthly-billing");
    }

    app.UseForwardedHeaders();
    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
        app.UseHangfireDashboard(); // /hangfire — dev only until dashboard auth is added
    }

    app.UseHttpsRedirection();

    // On the app subdomain, send the bare root straight to the dashboard shell.
    app.Use(async (ctx, next) =>
    {
        if (ctx.Request.Path == "/" &&
            ctx.Request.Host.Host.StartsWith("app.", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.Redirect("/app/");
            return;
        }
        await next();
    });

    app.UseDefaultFiles();
    app.UseStaticFiles();

    app.UseAuthentication();
    app.UseMiddleware<TenantResolutionMiddleware>(); // reads the auth claim → tenant scope
    app.UseAuthorization();

    app.MapControllers();
    app.MapHealthChecks("/health");

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "RecoverFlow API terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Converts a postgres:// URL (as handed out by Render/Heroku) into the key-value form
// Npgsql expects. A string already in key-value form is returned unchanged.
static string NormalizePostgresConnectionString(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw)) return raw ?? "";
    if (!raw.StartsWith("postgres://") && !raw.StartsWith("postgresql://")) return raw;

    var uri = new Uri(raw);
    var creds = uri.UserInfo.Split(':', 2);
    var user = Uri.UnescapeDataString(creds[0]);
    var pass = creds.Length > 1 ? Uri.UnescapeDataString(creds[1]) : "";
    var database = uri.AbsolutePath.TrimStart('/');
    var dbPort = uri.Port > 0 ? uri.Port : 5432;

    return $"Host={uri.Host};Port={dbPort};Database={database};Username={user};Password={pass};" +
           "SSL Mode=Require;Trust Server Certificate=true";
}
