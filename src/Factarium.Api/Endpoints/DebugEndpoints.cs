using Factarium.Api.Scheduling;
using Factarium.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Factarium.Api.Endpoints;

/// <summary>
/// Local-dev-only helpers. These endpoints are mapped ONLY when the host runs in
/// the Development environment (see Program.cs), so they never exist in the
/// container/single-exe (Production) builds.
/// </summary>
public static class DebugEndpoints
{
    // Every data table except schema/auth plumbing (app_users, migrations history, dashboard
    // settings, and the Data Protection key ring are preserved). sync_runs cascades from
    // integrations. All canonical tiers are listed explicitly — they have no FK to integrations,
    // so CASCADE won't reach them.
    private const string TruncateSql = """
        TRUNCATE integrations, raw_records,
                 canonical_repositories, canonical_commits, canonical_pull_requests,
                 canonical_reviews, canonical_issues, canonical_issue_segments,
                 canonical_issue_sprint_memberships, canonical_usage_metrics, canonical_worklogs,
                 daily_metrics, source_identities, people, pipeline_steps
        RESTART IDENTITY CASCADE;
        """;

    public static IEndpointRouteBuilder MapDebugEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/debug");

        // POST /api/debug/reset — wipe all synced/derived data (keeps schema + local user).
        group.MapPost("reset", async (
            FactariumDbContext db,
            IIntegrationScheduler scheduler,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            // Unschedule any Quartz jobs first so nothing fires against deleted rows.
            var integrationIds = await db.Integrations.Select(i => i.Id).ToListAsync(ct);
            foreach (var id in integrationIds)
            {
                await scheduler.UnscheduleAsync(id, ct);
            }

            await db.Database.ExecuteSqlRawAsync(TruncateSql, ct);

            loggerFactory.CreateLogger("Debug").LogWarning(
                "Database cleared via /api/debug/reset ({Count} integration(s) unscheduled)", integrationIds.Count);

            return Results.Ok(new { cleared = true, integrationsRemoved = integrationIds.Count });
        });

        return app;
    }
}
