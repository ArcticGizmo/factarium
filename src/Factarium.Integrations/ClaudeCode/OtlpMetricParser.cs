using System.Text.Json;

namespace Factarium.Integrations.ClaudeCode;

/// <summary>A single OTLP metric data point flattened for storage.</summary>
public sealed record OtlpMetricPoint(
    string Name,
    string? Unit,
    double Value,
    long TimeUnixNano,
    IReadOnlyDictionary<string, string> Attributes);

/// <summary>
/// Parses an OTLP/HTTP JSON <c>ExportMetricsServiceRequest</c> into flat data
/// points. Merges resource-level attributes (where Claude Code puts user/session
/// info) with per-point attributes; reads sum, gauge, and histogram-count points.
/// </summary>
public static class OtlpMetricParser
{
    public static List<OtlpMetricPoint> Parse(ReadOnlySpan<byte> json)
    {
        var points = new List<OtlpMetricPoint>();
        using var doc = JsonDocument.Parse(json.ToArray());

        if (!doc.RootElement.TryGetProperty("resourceMetrics", out var resourceMetrics)
            || resourceMetrics.ValueKind != JsonValueKind.Array)
        {
            return points;
        }

        foreach (var rm in resourceMetrics.EnumerateArray())
        {
            var resourceAttrs = rm.TryGetProperty("resource", out var resource)
                ? ReadAttributes(resource)
                : new Dictionary<string, string>();

            if (!rm.TryGetProperty("scopeMetrics", out var scopeMetrics))
            {
                continue;
            }

            foreach (var sm in scopeMetrics.EnumerateArray())
            {
                if (!sm.TryGetProperty("metrics", out var metrics))
                {
                    continue;
                }

                foreach (var metric in metrics.EnumerateArray())
                {
                    var name = metric.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (name is null)
                    {
                        continue;
                    }

                    var unit = metric.TryGetProperty("unit", out var u) ? u.GetString() : null;
                    foreach (var dp in DataPoints(metric))
                    {
                        var attrs = new Dictionary<string, string>(resourceAttrs);
                        foreach (var kv in ReadAttributes(dp))
                        {
                            attrs[kv.Key] = kv.Value;
                        }

                        points.Add(new OtlpMetricPoint(name, unit, Value(dp), TimeUnixNano(dp), attrs));
                    }
                }
            }
        }

        return points;
    }

    private static IEnumerable<JsonElement> DataPoints(JsonElement metric)
    {
        foreach (var kind in new[] { "sum", "gauge", "histogram" })
        {
            if (metric.TryGetProperty(kind, out var body)
                && body.TryGetProperty("dataPoints", out var dps)
                && dps.ValueKind == JsonValueKind.Array)
            {
                foreach (var dp in dps.EnumerateArray())
                {
                    yield return dp;
                }
            }
        }
    }

    private static double Value(JsonElement dp)
    {
        if (dp.TryGetProperty("asDouble", out var d) && d.ValueKind == JsonValueKind.Number)
        {
            return d.GetDouble();
        }

        if (dp.TryGetProperty("asInt", out var i))
        {
            // OTLP JSON encodes int64 as a string.
            return i.ValueKind == JsonValueKind.String && long.TryParse(i.GetString(), out var parsed)
                ? parsed
                : i.ValueKind == JsonValueKind.Number ? i.GetInt64() : 0;
        }

        // Histogram data points: use the count.
        if (dp.TryGetProperty("count", out var c))
        {
            return c.ValueKind == JsonValueKind.String && long.TryParse(c.GetString(), out var parsed) ? parsed
                : c.ValueKind == JsonValueKind.Number ? c.GetInt64() : 0;
        }

        return 0;
    }

    private static long TimeUnixNano(JsonElement dp) =>
        dp.TryGetProperty("timeUnixNano", out var t)
        && (t.ValueKind == JsonValueKind.String
            ? long.TryParse(t.GetString(), out var nanos)
            : t.TryGetInt64(out nanos))
            ? nanos
            : 0;

    private static Dictionary<string, string> ReadAttributes(JsonElement owner)
    {
        var result = new Dictionary<string, string>();
        if (!owner.TryGetProperty("attributes", out var attrs) || attrs.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var attr in attrs.EnumerateArray())
        {
            var key = attr.TryGetProperty("key", out var k) ? k.GetString() : null;
            if (key is null || !attr.TryGetProperty("value", out var value))
            {
                continue;
            }

            result[key] = ReadAnyValue(value);
        }

        return result;
    }

    private static string ReadAnyValue(JsonElement value) => value switch
    {
        _ when value.TryGetProperty("stringValue", out var s) => s.GetString() ?? "",
        _ when value.TryGetProperty("intValue", out var i) => i.ValueKind == JsonValueKind.String ? i.GetString() ?? "" : i.GetRawText(),
        _ when value.TryGetProperty("doubleValue", out var d) => d.GetRawText(),
        _ when value.TryGetProperty("boolValue", out var b) => b.GetRawText(),
        _ => "",
    };
}
