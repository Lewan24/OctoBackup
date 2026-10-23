using System.Text.Json;
using Modules.Shared.Settings;
using Microsoft.Extensions.Primitives;

namespace Server.Api.Configuration;

// One application process per data directory. Atomic replacement keeps readers on
// the last durable snapshot; the revision prevents stale administrator updates.
public sealed class FileApplicationSettings : IApplicationSettings, IDisposable
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ApplicationSettings _current;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FileApplicationSettings>? _logger;
    private readonly IDisposable _reload;
    private bool _persisted;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public FileApplicationSettings(IConfiguration config, string dataDirectory, ILogger<FileApplicationSettings>? logger = null)
    {
        _configuration = config; _logger = logger;
        _path = Path.Combine(dataDirectory, "Config", "application-settings.json");
        _persisted = File.Exists(_path);
        _current = Validate(_persisted
            ? JsonSerializer.Deserialize<ApplicationSettings>(File.ReadAllText(_path), Json) ?? throw new InvalidOperationException("Application settings file is empty.")
            : Defaults(config));
        _reload = ChangeToken.OnChange(config.GetReloadToken, ReloadDefaults);
    }

    private static ApplicationSettings Defaults(IConfiguration config) => new()
    {
        AllowedDatabaseHosts = config.GetSection("Security:AllowedDatabaseHosts").Get<string[]>() ?? [],
        RegistrationEnabled = config.GetValue<bool>("AuthSettings:EnableRegisterModule"),
        AutoConfirmAccounts = config.GetValue<bool>("AuthSettings:AutoConfirmAccount"),
        DefaultRetentionDays = config.GetValue("Backups:DefaultRetentionDays", 4),
        SchedulePollSeconds = config.GetValue("Backups:SchedulePollSeconds", 300),
        BackupQueueDelaySeconds = config.GetValue("Backups:QueueDelaySeconds", 20),
        BackupQueueSpacingSeconds = config.GetValue("Backups:QueueSpacingSeconds", 5),
        AutomaticRestoreTests = config.GetValue("RestoreTests:Automatic", false)
    };

    private void ReloadDefaults()
    {
        _gate.Wait();
        try
        {
            // A saved admin snapshot deliberately overrides all deployment defaults.
            if (_persisted) return;
            var next = Validate(Defaults(_configuration)) with { Revision = _current.Revision };
            if (JsonSerializer.Serialize(next, Json) == JsonSerializer.Serialize(_current, Json)) return;
            Volatile.Write(ref _current, next with { Revision = Guid.NewGuid().ToString("N") });
            _logger?.LogInformation("Reloaded deployment defaults for application settings");
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or FormatException)
        { _logger?.LogWarning("Ignoring invalid application-setting defaults; keeping the last valid values"); }
        finally { _gate.Release(); }
    }

    public ApplicationSettings Current
    {
        get { var current = Volatile.Read(ref _current); return current with { AllowedDatabaseHosts = [.. current.AllowedDatabaseHosts] }; }
    }

    public async Task<ApplicationSettings?> Save(ApplicationSettings proposed, string administratorId, CancellationToken token = default)
    {
        var validated = Validate(proposed);
        await _gate.WaitAsync(token);
        try
        {
            if (proposed.Revision != _current.Revision) return null;
            var saved = validated with { Revision = Guid.NewGuid().ToString("N"), UpdatedAt = DateTimeOffset.UtcNow, UpdatedBy = administratorId };
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var options = new FileStreamOptions
                {
                    Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
                    Options = FileOptions.Asynchronous | FileOptions.WriteThrough
                };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                await using (var stream = new FileStream(temporary, options))
                {
                    await JsonSerializer.SerializeAsync(stream, saved, Json, token);
                    await stream.FlushAsync(token);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporary, _path, overwrite: true);
                _persisted = true;
                Volatile.Write(ref _current, saved);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return Current;
        }
        finally { _gate.Release(); }
    }

    public static ApplicationSettings Validate(ApplicationSettings value)
    {
        if (value.AllowedDatabaseHosts is null || value.AllowedDatabaseHosts.Length > 256)
            throw new ArgumentException("Allow at most 256 database/SSH hosts.");
        if (value.DefaultRetentionDays is < 1 or > 3650)
            throw new ArgumentException("Default retention must be between 1 and 3650 days.");
        if (value.SchedulePollSeconds is < 30 or > 3600)
            throw new ArgumentException("Schedule polling must be between 30 and 3600 seconds.");
        if (value.BackupQueueDelaySeconds is < 5 or > 300 || value.BackupQueueSpacingSeconds is < 1 or > 300)
            throw new ArgumentException("Queue cancellation delay must be 5–300 seconds; spacing must be 1–300 seconds.");
        if (string.IsNullOrWhiteSpace(value.Revision) || value.Revision.Length > 64)
            throw new ArgumentException("Reload settings before saving.");
        return value with { AllowedDatabaseHosts = value.AllowedDatabaseHosts.Select(DatabaseHostPolicy.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray() };
    }

    public void Dispose() { _reload.Dispose(); _gate.Dispose(); }
}
