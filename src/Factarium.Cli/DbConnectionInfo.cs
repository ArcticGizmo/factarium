namespace Factarium.Cli;

/// <summary>Parsed Postgres connection parameters for the snapshot/restore tools.</summary>
internal sealed record DbConnectionInfo(string Host, int Port, string Database, string Username, string Password)
{
    public static DbConnectionInfo Parse(string connectionString)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq > 0)
            {
                map[part[..eq].Trim()] = part[(eq + 1)..].Trim();
            }
        }

        string Get(params string[] keys) => keys
            .Select(k => map.TryGetValue(k, out var v) ? v : null)
            .FirstOrDefault(v => v is not null) ?? string.Empty;

        return new DbConnectionInfo(
            Host: Get("Host", "Server"),
            Port: int.TryParse(Get("Port"), out var port) ? port : 5432,
            Database: Get("Database", "Db"),
            Username: Get("Username", "User Id", "UserId", "Uid"),
            Password: Get("Password", "Pwd"));
    }
}
