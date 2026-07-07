using System.Diagnostics;
using Kumburgaz.Web.Models;

namespace Kumburgaz.Web.Services;

public class BackupService(
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILogger<BackupService> logger)
{
    public string BackupDirectory
    {
        get
        {
            var configured = configuration["Backups:Directory"];
            var path = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(environment.ContentRootPath, "App_Data", "Backups")
                : configured;
            return Path.IsPathRooted(path) ? path : Path.Combine(environment.ContentRootPath, path);
        }
    }

    public IReadOnlyList<BackupFileViewModel> ListBackups()
    {
        Directory.CreateDirectory(BackupDirectory);
        return Directory.GetFiles(BackupDirectory)
            .Select(x => new FileInfo(x))
            .OrderByDescending(x => x.CreationTimeUtc)
            .Select(x => new BackupFileViewModel
            {
                FileName = x.Name,
                Size = x.Length,
                CreatedAt = x.CreationTimeUtc
            })
            .ToList();
    }

    public async Task<string> CreateBackupAsync(string reason, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(BackupDirectory);
        var connectionString = configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");

        if (IsSqlite(connectionString))
        {
            var source = ResolveSqlitePath(connectionString);
            var target = Path.Combine(BackupDirectory, $"kumburgaz-{reason}-{stamp}.db");
            await using var sourceStream = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            await using var targetStream = File.Create(target);
            await sourceStream.CopyToAsync(targetStream, cancellationToken);
            return target;
        }

        var pgTarget = Path.Combine(BackupDirectory, $"kumburgaz-{reason}-{stamp}.dump");
        await RunProcessAsync("pg_dump", BuildPgDumpArgs(connectionString, pgTarget), connectionString, cancellationToken);
        return pgTarget;
    }

    public async Task RestoreAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await CreateBackupAsync("restore-oncesi", cancellationToken);
        var connectionString = configuration.GetConnectionString("DefaultConnection") ?? string.Empty;

        if (IsSqlite(connectionString))
        {
            var target = ResolveSqlitePath(connectionString);
            File.Copy(filePath, target, overwrite: true);
            return;
        }

        await RunProcessAsync("pg_restore", BuildPgRestoreArgs(connectionString, filePath), connectionString, cancellationToken);
    }

    public string ResolveBackupPath(string fileName)
    {
        var safeFile = Path.GetFileName(fileName);
        var fullPath = Path.Combine(BackupDirectory, safeFile);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Yedek bulunamadı.", safeFile);
        }

        return fullPath;
    }

    public void PruneOldBackups()
    {
        var retentionDays = int.TryParse(configuration["Backups:RetentionDays"], out var days) ? days : 30;
        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        foreach (var file in Directory.GetFiles(BackupDirectory).Select(x => new FileInfo(x)).Where(x => x.CreationTimeUtc < cutoff))
        {
            try
            {
                file.Delete();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Yedek silinemedi: {File}", file.FullName);
            }
        }
    }

    private string ResolveSqlitePath(string connectionString)
    {
        var dataSource = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Split('=', 2))
            .Where(x => x.Length == 2)
            .FirstOrDefault(x => string.Equals(x[0].Trim(), "Data Source", StringComparison.OrdinalIgnoreCase))?[1].Trim();

        if (string.IsNullOrWhiteSpace(dataSource))
        {
            throw new InvalidOperationException("SQLite Data Source bulunamadı.");
        }

        return Path.IsPathRooted(dataSource) ? dataSource : Path.Combine(environment.ContentRootPath, dataSource);
    }

    private static bool IsSqlite(string connectionString)
    {
        return connectionString.TrimStart().StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildPgDumpArgs(string connectionString, string target)
    {
        return $"{BuildPgConnectionArgs(connectionString)} -Fc -f \"{target}\"";
    }

    private static string BuildPgRestoreArgs(string connectionString, string source)
    {
        return $"{BuildPgConnectionArgs(connectionString)} --clean --if-exists \"{source}\"";
    }

    private static string BuildPgConnectionArgs(string connectionString)
    {
        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Split('=', 2))
            .Where(x => x.Length == 2)
            .ToDictionary(x => x[0].Trim(), x => x[1].Trim(), StringComparer.OrdinalIgnoreCase);

        var host = parts.GetValueOrDefault("Host", "localhost");
        var port = parts.GetValueOrDefault("Port", "5432");
        var database = parts.GetValueOrDefault("Database", string.Empty);
        var username = parts.GetValueOrDefault("Username", parts.GetValueOrDefault("User ID", string.Empty));
        return $"-h \"{host}\" -p \"{port}\" -U \"{username}\" -d \"{database}\"";
    }

    private static async Task RunProcessAsync(string fileName, string arguments, string connectionString, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var password = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Split('=', 2))
            .Where(x => x.Length == 2)
            .FirstOrDefault(x => string.Equals(x[0].Trim(), "Password", StringComparison.OrdinalIgnoreCase))?[1].Trim();
        if (!string.IsNullOrWhiteSpace(password))
        {
            start.Environment["PGPASSWORD"] = password;
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{fileName} başlatılamadı.");
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException($"{fileName} başarısız: {error}");
        }
    }
}
