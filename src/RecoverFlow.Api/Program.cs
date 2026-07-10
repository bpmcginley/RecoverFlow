using Hangfire;
using Hangfire.PostgreSql;
using RecoverFlow.Application.Common;
using RecoverFlow.Infrastructure;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, cfg) => cfg
        .ReadFrom.Configuration(ctx.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console());

    builder.Services.Configure<StripeOptions>(builder.Configuration.GetSection(StripeOptions.Section));
    builder.Services.Configure<RetryOptions>(builder.Configuration.GetSection(RetryOptions.Section));
    builder.Services.Configure<EncryptionOptions>(builder.Configuration.GetSection(EncryptionOptions.Section));

    builder.Services.AddDataProtection();
    builder.Services.AddInfrastructure(builder.Configuration);

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

    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
        app.UseHangfireDashboard(); // /hangfire — dev only until dashboard auth is added
    }

    app.UseHttpsRedirection();
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
