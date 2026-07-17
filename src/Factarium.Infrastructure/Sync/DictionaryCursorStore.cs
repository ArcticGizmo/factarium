using Factarium.Application.Sync;

namespace Factarium.Infrastructure.Sync;

/// <summary>
/// In-memory cursor store for one sync run. Seeded from the integration's persisted
/// cursor JSON and flushed back afterward. <see cref="Dirty"/> avoids a write when
/// nothing changed.
/// </summary>
internal sealed class DictionaryCursorStore : ICursorStore
{
    private readonly Dictionary<string, string?> _values;

    public DictionaryCursorStore(IDictionary<string, string?>? initial = null)
        => _values = initial is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?>(initial);

    public bool Dirty { get; private set; }

    public string? Get(string key) => _values.TryGetValue(key, out var value) ? value : null;

    public void Set(string key, string? value)
    {
        _values[key] = value;
        Dirty = true;
    }

    public IReadOnlyDictionary<string, string?> Snapshot() => _values;
}
