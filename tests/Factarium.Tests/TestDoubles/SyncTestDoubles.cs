using System.Net;
using System.Text;
using Factarium.Application.Sync;

namespace Factarium.Tests.TestDoubles;

/// <summary>Records everything written so tests can assert on the facts produced.</summary>
internal sealed class RecordingRawRecordSink : IRawRecordSink
{
    public List<RawFact> Facts { get; } = [];

    public Task<int> WriteAsync(Guid integrationId, IReadOnlyCollection<RawFact> facts, CancellationToken cancellationToken)
    {
        Facts.AddRange(facts);
        return Task.FromResult(facts.Count);
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
