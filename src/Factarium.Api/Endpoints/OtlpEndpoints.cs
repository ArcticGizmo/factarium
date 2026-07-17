using Factarium.Application.Sync;
using Factarium.Integrations.ClaudeCode;

namespace Factarium.Api.Endpoints;

/// <summary>
/// OTLP/HTTP receiver. Accepts JSON metric exports (e.g. from Claude Code with
/// <c>OTEL_EXPORTER_OTLP_PROTOCOL=http/json</c>) and pushes them into the bronze
/// tier. Logs/traces are accepted with an empty success response but not yet
/// processed.
/// </summary>
public static class OtlpEndpoints
{
    public static IEndpointRouteBuilder MapOtlpEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/metrics", async (HttpRequest request, IPushIngestionService ingest, CancellationToken ct) =>
        {
            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, ct);

            var result = await ingest.IngestAsync(ClaudeOtelPushSource.SourceName, buffer.ToArray(), ct);
            if (!result.Succeeded)
            {
                return Results.BadRequest(new { error = result.Error });
            }

            // Empty ExportMetricsServiceResponse == full success in OTLP.
            return Results.Json(new { });
        });

        // Accept (but do not yet process) logs/traces so exporters don't error.
        app.MapPost("/v1/logs", () => Results.Json(new { }));
        app.MapPost("/v1/traces", () => Results.Json(new { }));

        return app;
    }
}
