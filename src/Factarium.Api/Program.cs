using Factarium.Api.Endpoints;
using Factarium.Api.Scheduling;
using Factarium.Api.Security;
using Factarium.Application.Security;
using Factarium.Infrastructure;
using Factarium.Infrastructure.Persistence;
using Factarium.Integrations;
using Microsoft.EntityFrameworkCore;
using Quartz;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console());

    builder.Services.AddFactariumInfrastructure(builder.Configuration);
    builder.Services.AddFactariumIntegrations();
    builder.Services.AddSingleton<ICurrentUserAccessor, LocalCurrentUserAccessor>();

    // Background scheduling: Quartz executes integration syncs; our DB holds the schedule.
    builder.Services.AddQuartz(q =>
    {
        // Staleness-gated transform + aggregate on a fixed cadence.
        var pipelineJob = new JobKey("pipeline");
        q.AddJob<PipelineJob>(o => o.WithIdentity(pipelineJob).StoreDurably());
        q.AddTrigger(t => t
            .ForJob(pipelineJob)
            .WithIdentity("pipeline-cron")
            .WithCronSchedule("0 0/5 * * * ?"));
    });
    builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
    builder.Services.AddTransient<IntegrationSyncJob>();
    builder.Services.AddSingleton<IIntegrationScheduler, QuartzIntegrationScheduler>();
    builder.Services.AddHostedService<SchedulerStartup>();

    // Authorization seam: policies are declared now (permissive in local mode) so
    // endpoints can carry RequireAuthorization(...) without a rewrite later.
    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy(FactariumRoles.Administrator, policy => policy.RequireAssertion(_ => true));
        options.AddPolicy(FactariumRoles.Viewer, policy => policy.RequireAssertion(_ => true));
    });

    const string devCorsPolicy = "spa-dev";
    builder.Services.AddCors(options => options.AddPolicy(devCorsPolicy, policy => policy
        .WithOrigins("http://localhost:4601")
        .AllowAnyHeader()
        .AllowAnyMethod()));

    var app = builder.Build();

    if (app.Configuration.GetValue("Factarium:MigrateOnStartup", true))
    {
        await app.Services.MigrateAndSeedAsync();
    }

    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseCors(devCorsPolicy);
    }

    // Serve the built Vue SPA when present (single-exe / container image).
    app.UseDefaultFiles();
    app.UseStaticFiles();

    var api = app.MapGroup("/api");

    api.MapGet("/health", async (FactariumDbContext db, CancellationToken ct) =>
    {
        var dbReachable = await db.Database.CanConnectAsync(ct);
        var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        return Results.Ok(new
        {
            status = dbReachable ? "healthy" : "degraded",
            database = dbReachable ? "connected" : "unreachable",
            version,
            environment = app.Environment.EnvironmentName,
        });
    });

    api.MapGet("/me", (ICurrentUserAccessor currentUser) =>
    {
        var user = currentUser.Current;
        return Results.Ok(new
        {
            user.Id,
            user.UserName,
            user.DisplayName,
            user.Roles,
        });
    });

    app.MapIntegrationEndpoints();
    app.MapPeopleEndpoints();
    app.MapDashboardEndpoints();
    app.MapPipelineEndpoints();
    app.MapLiveEndpoints();
    app.MapOtlpEndpoints();

    // Debug-only endpoints (e.g. clear the database) exist ONLY in local dev.
    if (app.Environment.IsDevelopment())
    {
        app.MapDebugEndpoints();
    }

    // SPA fallback: any non-API route serves index.html for client-side routing.
    app.MapFallbackToFile("index.html");

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Factarium API terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Exposed so integration tests can reference the API host.</summary>
public partial class Program;
