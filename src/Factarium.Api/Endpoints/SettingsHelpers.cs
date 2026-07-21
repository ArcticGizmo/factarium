using System.Text.Json;
using Factarium.Domain.Settings;
using Factarium.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Factarium.Api.Endpoints;

/// <summary>
/// Load/save helpers for per-dashboard settings (e.g. targets) held in the settings table as
/// JSON. Each dashboard owns a key (see the target records' <c>SettingsKey</c>); a missing row
/// falls back to <c>new T()</c> so the type's default initializers provide sensible starters.
/// </summary>
internal static class SettingsHelpers
{
    public static async Task<T> LoadAsync<T>(FactariumDbContext db, string key, CancellationToken ct)
        where T : new()
    {
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null)
        {
            return new T();
        }
        return JsonSerializer.Deserialize<T>(row.Value, JsonSerializerOptions.Web) ?? new T();
    }

    public static async Task SaveAsync<T>(FactariumDbContext db, TimeProvider clock, string key, T value, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(value, JsonSerializerOptions.Web);
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null)
        {
            db.Settings.Add(new Setting { Key = key, Value = json, UpdatedAt = clock.GetUtcNow() });
        }
        else
        {
            row.Value = json;
            row.UpdatedAt = clock.GetUtcNow();
        }
        await db.SaveChangesAsync(ct);
    }
}
