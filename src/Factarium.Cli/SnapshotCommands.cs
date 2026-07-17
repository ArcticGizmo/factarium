using System.Diagnostics;

namespace Factarium.Cli;

/// <summary>
/// pg_dump / pg_restore wrappers for local-dev data preservation. Defaults to
/// running the tools inside the running Postgres container (via `docker exec`),
/// which needs no host-installed client tools and always matches the server
/// version. Snapshots are custom-format (.dump) files under snapshots/.
/// </summary>
internal static class SnapshotCommands
{
    private const string DefaultContainer = "factarium-postgres-1";
    private const string SnapshotsDir = "snapshots";

    public static async Task<int> SnapshotAsync(string[] args, DbConnectionInfo db)
    {
        var name = StringArg(args, "--name") ?? $"{db.Database}-{DateTime.Now:yyyyMMdd-HHmmss}";
        var container = StringArg(args, "--container") ?? DefaultContainer;
        Directory.CreateDirectory(SnapshotsDir);
        var path = Path.Combine(SnapshotsDir, name.EndsWith(".dump") ? name : $"{name}.dump");

        Console.WriteLine($"Snapshotting '{db.Database}' -> {path} (via container {container})...");

        var psi = DockerExec(container, interactive: false,
            "pg_dump", "-U", db.Username, "-Fc", "-d", db.Database);

        using var process = Start(psi);
        await using (var file = File.Create(path))
        {
            var copy = process.StandardOutput.BaseStream.CopyToAsync(file);
            var stderr = process.StandardError.ReadToEndAsync();
            await Task.WhenAll(copy, stderr);
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                Console.Error.WriteLine(await stderr);
                File.Delete(path);
                return process.ExitCode;
            }
        }

        var size = new FileInfo(path).Length;
        Console.WriteLine($"Snapshot written: {path} ({size:N0} bytes).");
        return 0;
    }

    public static async Task<int> RestoreAsync(string[] args, DbConnectionInfo db)
    {
        var name = StringArg(args, "--name") ?? PositionalPath(args);
        if (name is null)
        {
            Console.Error.WriteLine("restore requires --name <snapshot> or a path to a .dump file.");
            return 1;
        }

        var container = StringArg(args, "--container") ?? DefaultContainer;
        var path = ResolveSnapshotPath(name);
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Snapshot not found: {path}");
            return 1;
        }

        Console.WriteLine($"Restoring {path} -> '{db.Database}' (via container {container})...");

        var psi = DockerExec(container, interactive: true,
            "pg_restore", "-U", db.Username, "-d", db.Database, "--clean", "--if-exists", "--no-owner");

        using var process = Start(psi);
        var stderr = process.StandardError.ReadToEndAsync();
        await using (var file = File.OpenRead(path))
        {
            await file.CopyToAsync(process.StandardInput.BaseStream);
            process.StandardInput.Close();
        }

        await process.WaitForExitAsync();
        var errors = await stderr;

        // pg_restore exits non-zero on ignorable notices with --clean --if-exists;
        // surface stderr but treat exit code as authoritative.
        if (process.ExitCode != 0)
        {
            Console.Error.WriteLine(errors);
            return process.ExitCode;
        }

        Console.WriteLine("Restore complete.");
        return 0;
    }

    private static ProcessStartInfo DockerExec(string container, bool interactive, params string[] command)
    {
        var psi = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = interactive,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("exec");
        if (interactive)
        {
            psi.ArgumentList.Add("-i");
        }

        psi.ArgumentList.Add(container);
        foreach (var part in command)
        {
            psi.ArgumentList.Add(part);
        }

        return psi;
    }

    private static Process Start(ProcessStartInfo psi) =>
        Process.Start(psi) ?? throw new InvalidOperationException("Failed to start docker process.");

    private static string ResolveSnapshotPath(string name)
    {
        if (name.Contains('/') || name.Contains('\\') || File.Exists(name))
        {
            return name;
        }

        return Path.Combine(SnapshotsDir, name.EndsWith(".dump") ? name : $"{name}.dump");
    }

    private static string? PositionalPath(string[] args) =>
        args.Skip(1).FirstOrDefault(a => !a.StartsWith('-'));

    private static string? StringArg(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
