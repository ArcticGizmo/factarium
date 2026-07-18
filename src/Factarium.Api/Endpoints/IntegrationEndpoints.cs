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
