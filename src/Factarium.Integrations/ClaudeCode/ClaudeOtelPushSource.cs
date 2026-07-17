using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Factarium.Application.Sync;
using Microsoft.Extensions.Logging;

namespace Factarium.Integrations.ClaudeCode;

/// <summary>
/// Push source for Claude Code OpenTelemetry metrics. Parses an OTLP/JSON metrics
/// export and writes one raw record per data point (source "claude-code",
/// entity "metric"), enriched with the merged attributes so the transform can
/// attribute usage to a person and slice by session/model.
/// </summary>
public sealed class ClaudeOtelPushSource(ILogger<ClaudeOtelPushSource> logger) : IPushSource
{
    public const string SourceName = "claude-code";

    public string Type => SourceName;

    public async Task<SyncResult> IngestAsync(SyncContext context, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        List<OtlpMetricPoint> points;
        try
        {
            points = OtlpMetricParser.Parse(payload.Span);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Rejected malformed OTLP metrics payload");
            return SyncResult.Failed("Malformed OTLP JSON payload.");
        }

        var facts = new List<RawFact>(points.Count);
        foreach (var point in points)
        {
            var occurredAt = point.TimeUnixNano > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(point.TimeUnixNano / 1_000_000)
                : (DateTimeOffset?)null;

            var body = JsonSerializer.Serialize(new
            {
                name = point.Name,
                unit = point.Unit,
                value = point.Value,
                timeUnixNano = point.TimeUnixNano,
                attributes = point.Attributes,
            });

            facts.Add(new RawFact(SourceName, "metric", StableId(point), body, occurredAt));
        }

        var written = await context.Sink.WriteAsync(context.IntegrationId, facts, cancellationToken);
        logger.LogInformation("Ingested {Count} Claude Code metric points", written);
        return SyncResult.Ok(written);
    }

    // Deterministic id so replays of the same export upsert rather than duplicate.
    private static string StableId(OtlpMetricPoint point)
    {
        var attrs = string.Join('|', point.Attributes.OrderBy(a => a.Key).Select(a => $"{a.Key}={a.Value}"));
        var raw = $"{point.Name}|{point.TimeUnixNano}|{attrs}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexStringLower(hash);
    }
}
