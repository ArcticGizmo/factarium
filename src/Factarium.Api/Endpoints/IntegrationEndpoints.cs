using System.Text.Json;
using Factarium.Api.Scheduling;
using Factarium.Application.Security;
using Factarium.Domain.Sync;
using Factarium.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Factarium.Api.Endpoints;

public sealed record CreateGitHubIntegrationRequest(
    string Name,
    string? Cron,
    bool Enabled,
    string? Org,
    List<string>? Repos,
    string? Credential);

public sealed record CreateJiraIntegrationRequest(
    string Name,
    string? Cron,
    bool Enabled,
    string? BaseUrl,
    string? Email,
    List<string>? ProjectKeys,
    string? Jql,
    string? Credential);

public sealed record CreateClaudeIntegrationRequest(
    string Name,
    bool Enabled);

public sealed record UpdateGitHubIntegrationRequest(
    string Name,
    string? Cron,
    bool Enabled,
    string? Org,
    List<string>? Repos,
    string? Credential);

/// <summary>Update the fields common to every integration (schedule, enablement, credential).</summary>
public sealed record UpdateIntegrationRequest(
    bool Enabled,
    string? Cron,
    string? Credential);

public static class IntegrationEndpoints
{
    public static IEndpointRouteBuilder MapIntegrationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/integrations");

        group.MapGet("", async (FactariumDbContext db, IIntegrationScheduler scheduler, CancellationToken ct) =>
        {
            var integrations = await db.Integrations.OrderBy(i => i.Name).ToListAsync(ct);

            var result = new List<object>(integrations.Count);
            foreach (var i in integrations)
            {
                result.Add(new
                {
                    i.Id,
                    i.Type,
                    i.Name,
                    i.Enabled,
                    i.ScheduleCron,
                    LastRunStatus = i.LastRunStatus.ToString(),
                    i.LastRunStartedAt,
                    i.LastRunCompletedAt,
                    i.LastRunError,
                    i.LastRunRecordsWritten,
                    NextRunAt = await scheduler.GetNextRunAsync(i.Id, ct),
                    HasCredential = i.EncryptedCredential is not null,
                    Config = ConfigFor(i),
                });
            }

            return Results.Ok(result);
        });

        group.MapPost("github", (
            CreateGitHubIntegrationRequest request,
            FactariumDbContext db,
            ICredentialProtector protector,
            IIntegrationScheduler scheduler,
            TimeProvider clock,
            CancellationToken ct) =>
            CreateAsync(
                new GitHubIntegration
                {
                    Name = request.Name,
                    Enabled = request.Enabled,
                    ScheduleCron = request.Cron,
                    Org = Blank(request.Org),
                    Repos = Clean(request.Repos),
                },
                request.Credential, db, protector, scheduler, clock, ct));

        group.MapPut("github/{id:guid}", async (
            Guid id,
            UpdateGitHubIntegrationRequest request,
            FactariumDbContext db,
            ICredentialProtector protector,
            IIntegrationScheduler scheduler,
            CancellationToken ct) =>
        {
            var integration = await db.Integrations.FindAsync([id], ct);
            if (integration is null)
            {
                return Results.NotFound();
            }

            if (integration is not GitHubIntegration gh)
            {
                return Results.BadRequest("Integration is not a GitHub connection.");
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return Results.BadRequest("Name is required.");
            }

            gh.Name = request.Name;
            gh.Enabled = request.Enabled;
            gh.ScheduleCron = Blank(request.Cron);
            gh.Org = Blank(request.Org);
            gh.Repos = Clean(request.Repos);
            if (!string.IsNullOrWhiteSpace(request.Credential))
            {
                gh.EncryptedCredential = protector.Protect(request.Credential);
            }

            await db.SaveChangesAsync(ct);

            // Reschedules or unschedules based on the new enabled/cron state.
            await scheduler.ScheduleAsync(gh, ct);
            return Results.NoContent();
        });

        group.MapPost("jira", (
            CreateJiraIntegrationRequest request,
            FactariumDbContext db,
            ICredentialProtector protector,
            IIntegrationScheduler scheduler,
            TimeProvider clock,
            CancellationToken ct) =>
            CreateAsync(
                new JiraIntegration
                {
                    Name = request.Name,
                    Enabled = request.Enabled,
                    ScheduleCron = request.Cron,
                    BaseUrl = Blank(request.BaseUrl),
                    Email = Blank(request.Email),
                    ProjectKeys = Clean(request.ProjectKeys),
                    Jql = Blank(request.Jql),
                },
                request.Credential, db, protector, scheduler, clock, ct));

        group.MapPost("claude", (
            CreateClaudeIntegrationRequest request,
            FactariumDbContext db,
            ICredentialProtector protector,
            IIntegrationScheduler scheduler,
            TimeProvider clock,
            CancellationToken ct) =>
            CreateAsync(
                new ClaudeIntegration
                {
                    Name = request.Name,
                    Enabled = request.Enabled,
                },
                credential: null, db, protector, scheduler, clock, ct));

        group.MapPut("{id:guid}", async (
            Guid id,
            UpdateIntegrationRequest request,
            FactariumDbContext db,
            ICredentialProtector protector,
            IIntegrationScheduler scheduler,
            CancellationToken ct) =>
        {
            var integration = await db.Integrations.FindAsync([id], ct);
            if (integration is null)
            {
                return Results.NotFound();
            }

            integration.Enabled = request.Enabled;
            integration.ScheduleCron = Blank(request.Cron);
            if (!string.IsNullOrWhiteSpace(request.Credential))
            {
                integration.EncryptedCredential = protector.Protect(request.Credential);
            }

            await db.SaveChangesAsync(ct);

            // Reschedules or unschedules based on the new enabled/cron state.
            await scheduler.ScheduleAsync(integration, ct);
            return Results.NoContent();
        });

        // Raw records replicated for an integration, grouped into entity-type tabs.
        group.MapGet("{id:guid}/records/summary", async (Guid id, FactariumDbContext db, CancellationToken ct) =>
        {
            var integration = await db.Integrations.FindAsync([id], ct);
            if (integration is null)
            {
                return Results.NotFound();
            }

            var entityTypes = await db.RawRecords
                .Where(r => r.IntegrationId == id)
                .GroupBy(r => r.EntityType)
                .Select(g => new { EntityType = g.Key, Count = g.Count() })
                .OrderBy(x => x.EntityType)
                .ToListAsync(ct);

            return Results.Ok(new { integration.Id, integration.Name, integration.Type, EntityTypes = entityTypes });
        });

        group.MapGet("{id:guid}/records", async (
            Guid id,
            string? entityType,
            DateTimeOffset? from,
            DateTimeOffset? to,
            int? page,
            int? pageSize,
            FactariumDbContext db,
            CancellationToken ct) =>
        {
            if (!await db.Integrations.AnyAsync(i => i.Id == id, ct))
            {
                return Results.NotFound();
            }

            var size = Math.Clamp(pageSize ?? 25, 1, 200);
            var pageNumber = Math.Max(page ?? 1, 1);

            var query = db.RawRecords.Where(r => r.IntegrationId == id);
            if (!string.IsNullOrWhiteSpace(entityType))
            {
                query = query.Where(r => r.EntityType == entityType);
            }

            // Date range filters the source-reported timestamp (e.g. a commit's date).
            if (from is not null)
            {
                query = query.Where(r => r.SourceUpdatedAt >= from);
            }

            if (to is not null)
            {
                query = query.Where(r => r.SourceUpdatedAt <= to);
            }

            var total = await query.CountAsync(ct);

            var records = await query
                .OrderByDescending(r => r.SourceUpdatedAt ?? r.FetchedAt)
                .Skip((pageNumber - 1) * size)
                .Take(size)
                .Select(r => new
                {
                    r.Id,
                    r.EntityType,
                    r.SourceId,
                    r.SourceUpdatedAt,
                    r.FirstSeenAt,
                    r.FetchedAt,
                    r.Version,
                    r.Payload,
                })
                .ToListAsync(ct);

            // Payload is stored as a JSON string; parse it so it serializes back as real JSON.
            var result = records.Select(r => new
            {
                r.Id,
                r.EntityType,
                r.SourceId,
                r.SourceUpdatedAt,
                r.FirstSeenAt,
                r.FetchedAt,
                r.Version,
                Payload = JsonSerializer.Deserialize<JsonElement>(r.Payload),
            });

            return Results.Ok(new { Total = total, Page = pageNumber, PageSize = size, Records = result });
        });

        group.MapPost("{id:guid}/sync", async (
            Guid id, FactariumDbContext db, IIntegrationScheduler scheduler, CancellationToken ct) =>
        {
            if (!await db.Integrations.AnyAsync(i => i.Id == id, ct))
            {
                return Results.NotFound();
            }

            await scheduler.TriggerNowAsync(id, ct);
            return Results.Accepted($"/api/integrations/{id}");
        });

        group.MapDelete("{id:guid}", async (
            Guid id, FactariumDbContext db, IIntegrationScheduler scheduler, CancellationToken ct) =>
        {
            var integration = await db.Integrations.FindAsync([id], ct);
            if (integration is null)
            {
                return Results.NotFound();
            }

            await scheduler.UnscheduleAsync(id, ct);
            db.Integrations.Remove(integration);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        return app;
    }

    private static async Task<IResult> CreateAsync(
        Integration integration,
        string? credential,
        FactariumDbContext db,
        ICredentialProtector protector,
        IIntegrationScheduler scheduler,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(integration.Name))
        {
            return Results.BadRequest("Name is required.");
        }

        integration.Id = Guid.NewGuid();
        integration.CreatedAt = clock.GetUtcNow();
        integration.EncryptedCredential = string.IsNullOrWhiteSpace(credential)
            ? null
            : protector.Protect(credential);

        db.Integrations.Add(integration);
        await db.SaveChangesAsync(ct);

        if (integration is { Enabled: true, ScheduleCron: not null })
        {
            await scheduler.ScheduleAsync(integration, ct);
        }

        return Results.Created($"/api/integrations/{integration.Id}", new { integration.Id });
    }

    /// <summary>The type-specific fields projected for a row, so each page can bind its own config.</summary>
    private static object? ConfigFor(Integration integration) => integration switch
    {
        GitHubIntegration gh => new { gh.Org, gh.Repos },
        JiraIntegration jira => new { jira.BaseUrl, jira.Email, jira.ProjectKeys, jira.Jql },
        _ => null,
    };

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<string> Clean(List<string>? values) =>
        values is null
            ? []
            : values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToList();
}
