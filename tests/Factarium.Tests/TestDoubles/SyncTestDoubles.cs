using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using Factarium.Application.Sync;

namespace Factarium.Tests.TestDoubles;

/// <summary>
/// In-memory bronze tier for tests: records everything written and reads it back, so
/// dependent entities (e.g. issue_changelog reading issues) see what earlier entities
/// wrote. Doubles as both the sink and the reader.
/// </summary>
internal sealed class RecordingRawRecordSink : IRawRecordSink, IRawRecordReader
{
    public List<RawFact> Facts { get; } = [];

    // The size of each WriteAsync call, so tests can assert incremental (batched) flushing.
    public List<int> WriteBatchSizes { get; } = [];

    public Task<int> WriteAsync(Guid integrationId, IReadOnlyCollection<RawFact> facts, CancellationToken cancellationToken)
    {
        WriteBatchSizes.Add(facts.Count);
        Facts.AddRange(facts);
        return Task.FromResult(facts.Count);
    }

    public async IAsyncEnumerable<RawRecordRef> ReadAsync(
        Guid integrationId,
        string entityType,
        DateTimeOffset? updatedAfter,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        foreach (var fact in Facts
                     .Where(f => f.EntityType == entityType && (updatedAfter is null || f.SourceUpdatedAt > updatedAfter))
                     .OrderBy(f => f.SourceUpdatedAt))
        {
            yield return new RawRecordRef(fact.SourceId, fact.SourceUpdatedAt, fact.Payload);
        }
    }
}

/// <summary>Runs every entity of a pull source in order, like the orchestrator does.</summary>
internal static class PullSourceRunner
{
    public static async Task<List<SyncResult>> RunAllAsync(IPullSource source, SyncContext context)
    {
        var results = new List<SyncResult>();
        foreach (var entity in source.Entities)
        {
            results.Add(await source.PullEntityAsync(entity, context, CancellationToken.None));
        }

        return results;
    }
}

internal sealed class InMemoryCursorStore : ICursorStore
{
    private readonly Dictionary<string, string?> _values = new();

    public string? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;

    public void Set(string key, string? value) => _values[key] = value;

    public IReadOnlyDictionary<string, string?> Values => _values;
}

/// <summary>Routes GitHub requests to a caller-supplied responder; records request URLs.</summary>
internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    : HttpMessageHandler
{
    public List<string> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!.ToString());
        return Task.FromResult(responder(request));
    }

    public static HttpResponseMessage Json(string body, string? link = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        if (link is not null)
        {
            response.Headers.TryAddWithoutValidation("Link", link);
        }

        return response;
    }

    public static HttpResponseMessage NotFound() => new(HttpStatusCode.NotFound);
}
