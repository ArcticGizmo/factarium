using System.Text.Json;
using Factarium.Domain.Canonical;
using Factarium.Domain.People;
using Factarium.Domain.Sync;
using Factarium.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Factarium.Infrastructure.Transform;

/// <summary>
/// Turns raw Claude Code OTEL metric records into canonical usage metrics, creating
/// a "claude-code" SourceIdentity per user (email/id). Maps OTEL metric names to
/// normalized keys and splits lines-of-code by its add/remove attribute.
/// </summary>
internal sealed class ClaudeOtelTransformService(FactariumDbContext db, TimeProvider clock)
{
    private const string Source = "claude-code";

    public async Task<int> TransformAsync(CancellationToken cancellationToken)
    {
        var records = await db.RawRecords
            .Where(r => r.Source == Source && r.EntityType == "metric")
            .ToListAsync(cancellationToken);

        var identities = await EnsureIdentitiesAsync(records, cancellationToken);

        var existing = await db.CanonicalUsageMetrics
            .Where(m => m.Source == Source)
            .ToDictionaryAsync(m => m.ExternalId, cancellationToken);

        foreach (var record in records)
        {
            using var doc = JsonDocument.Parse(record.Payload);
            var root = doc.RootElement;
            var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (name is null)
            {
                continue;
            }

            var attributes = ReadAttributes(root);
            var metricKey = MapMetricKey(name, attributes);
            if (metricKey is null)
            {
                continue; // metric we don't surface yet
            }

            if (!existing.TryGetValue(record.SourceId, out var metric))
            {
                metric = new CanonicalUsageMetric { Source = Source, ExternalId = record.SourceId, MetricKey = metricKey };
                db.CanonicalUsageMetrics.Add(metric);
                existing[record.SourceId] = metric;
            }

            var login = Login(attributes);
            metric.MetricKey = metricKey;
            metric.Value = root.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
            metric.ActorLogin = login;
            metric.ActorIdentityId = login is not null && identities.TryGetValue(login, out var id) ? id : null;
            metric.SessionId = attributes.GetValueOrDefault("session.id");
            metric.Model = attributes.GetValueOrDefault("model");
            metric.OccurredAt = record.SourceUpdatedAt;
        }

        await db.SaveChangesAsync(cancellationToken);
        return records.Count;
    }

    private async Task<Dictionary<string, Guid>> EnsureIdentitiesAsync(List<RawRecord> records, CancellationToken cancellationToken)
    {
        var existing = await db.SourceIdentities
            .Where(i => i.Source == Source)
            .ToListAsync(cancellationToken);

        var map = existing.ToDictionary(i => i.Login, i => i.Id, StringComparer.OrdinalIgnoreCase);
        var discovered = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in records)
        {
            using var doc = JsonDocument.Parse(record.Payload);
            var attributes = ReadAttributes(doc.RootElement);
            var login = Login(attributes);
            if (login is not null)
            {
                discovered[login] = attributes.GetValueOrDefault("user.email") ?? login;
            }
        }

        var now = clock.GetUtcNow();
        foreach (var (login, display) in discovered)
        {
            if (map.ContainsKey(login))
            {
                continue;
            }

            var identity = new SourceIdentity
            {
                Id = Guid.NewGuid(),
                Source = Source,
                Login = login,
                DisplayName = display ?? login,
                PersonId = null,
                FirstSeenAt = now,
            };
            db.SourceIdentities.Add(identity);
            map[login] = identity.Id;
        }

        await db.SaveChangesAsync(cancellationToken);
        return map;
    }

    private static string? Login(Dictionary<string, string> attributes) =>
        attributes.GetValueOrDefault("user.email")
        ?? attributes.GetValueOrDefault("user.id")
        ?? attributes.GetValueOrDefault("user.account_uuid");

    private static string? MapMetricKey(string otelName, Dictionary<string, string> attributes) => otelName switch
    {
        "claude_code.cost.usage" => "cc_cost_usd",
        "claude_code.token.usage" => "cc_tokens",
        "claude_code.session.count" => "cc_sessions",
        "claude_code.commit.count" => "cc_commits",
        "claude_code.pull_request.count" => "cc_pull_requests",
        "claude_code.lines_of_code.count" =>
            attributes.GetValueOrDefault("type")?.ToLowerInvariant() == "removed" ? "cc_lines_removed" : "cc_lines_added",
        _ => null,
    };

    private static Dictionary<string, string> ReadAttributes(JsonElement root)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in attrs.EnumerateObject())
            {
                result[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString() ?? ""
                    : prop.Value.GetRawText();
            }
        }

        return result;
    }
}
