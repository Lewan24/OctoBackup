using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Server.Api.Configuration;

public sealed class SettingsReloadLog
{
    public ILogger? Logger { get; set; }
    public Action<string>? Observe { get; set; }
    internal void Write(string file, string result)
    {
        // Never attach parser exceptions or configuration values: JSON can contain secrets.
        Observe?.Invoke(result);
        if (result == "applied") Logger?.LogInformation("Reloaded runtime settings from {SettingsFile}", file);
        else if (result == "startup") Logger?.LogWarning("Startup-only settings changed in {SettingsFile}; they remain unchanged until the API restarts", file);
        else Logger?.LogWarning("Ignoring invalid or unavailable settings file {SettingsFile}; keeping its last valid settings", file);
    }
}

public static class ReloadableAppSettings
{
    public static SettingsReloadLog Configure(ConfigurationManager configuration, string contentRoot)
    {
        var log = new SettingsReloadLog();
        var sources = configuration.Sources;
        var insertAt = 0;
        for (var i = 0; i < sources.Count; i++)
            if (sources[i] is JsonConfigurationSource json && IsAppSettings(json.Path)) insertAt = i + 1;
        var runtime = configuration["Configuration:ReloadFile"];
        var extra = new JsonConfigurationSource {
            Path = string.IsNullOrWhiteSpace(runtime) ? Path.Combine(contentRoot, "appsettings.Runtime.json") : Path.GetFullPath(runtime, contentRoot),
            Optional = true, ReloadOnChange = true };
        extra.ResolveFileProvider();
        // Every JSON file remains below user-secrets, environment and command line.
        sources.Insert(insertAt, extra);
        for (var i = 0; i < sources.Count; i++)
            if (sources[i] is JsonConfigurationSource json && (ReferenceEquals(json, extra) || IsAppSettings(json.Path)))
                sources[i] = new WatchedSettingsSource(json, log);
        return log;
    }

    private static bool IsAppSettings(string? path) => Path.GetFileName(path ?? "").StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)
        && Path.GetExtension(path ?? "").Equals(".json", StringComparison.OrdinalIgnoreCase);

    internal static bool IsLive(string key) => new[] { "RestoreTests", "DatabaseTools", "SqlServer", "Sqlite", "Backups", "Logging",
        "Security:AllowedDatabaseHosts", "AuthSettings:EnableRegisterModule", "AuthSettings:AutoConfirmAccount" }
        .Any(prefix => key.Equals(prefix, StringComparison.OrdinalIgnoreCase) || key.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase));

    private sealed class WatchedSettingsSource(JsonConfigurationSource source, SettingsReloadLog log) : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            source.EnsureDefaults(builder);
            return new WatchedSettingsProvider(source.FileProvider!, source.Path!, source.Optional, log);
        }
    }

    private sealed class WatchedSettingsProvider : ConfigurationProvider, IDisposable
    {
        private readonly IFileProvider _files;
        private readonly string _path;
        private readonly bool _optional;
        private readonly SettingsReloadLog _log;
        private readonly object _gate = new();
        private readonly Timer _timer;
        private IDisposable? _watch;
        private bool _loaded, _disposed;

        public WatchedSettingsProvider(IFileProvider files, string path, bool optional, SettingsReloadLog log)
        {
            _files = files; _path = path; _optional = optional; _log = log;
            _timer = new Timer(_ => Read(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public override void Load()
        {
            Read();
            _watch ??= ChangeToken.OnChange(() => _files.Watch(_path), () => {
                lock (_gate) if (!_disposed) _timer.Change(300, Timeout.Infinite);
            });
        }

        private void Read()
        {
            var changed = false;
            lock (_gate)
            {
                if (_disposed) return;
                try
                {
                    var file = _files.GetFileInfo(_path);
                    if (!file.Exists)
                    {
                        if (!_loaded && _optional) { _loaded = true; return; }
                        throw new IOException("Settings file is unavailable.");
                    }
                    using var stream = file.CreateReadStream();
                    using var parsed = new ConfigurationManager();
                    parsed.AddJsonStream(stream);
                    var next = parsed.AsEnumerable().ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
                    if (_loaded)
                    {
                        if (Data.Any(p => !IsLive(p.Key) && (!next.TryGetValue(p.Key, out var value) || value != p.Value)) ||
                            next.Any(p => !IsLive(p.Key) && (!Data.TryGetValue(p.Key, out var value) || value != p.Value)))
                            _log.Write(Path.GetFileName(_path), "startup");
                        next = Data.Where(p => !IsLive(p.Key)).Concat(next.Where(p => IsLive(p.Key)))
                            .ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
                    }
                    changed = _loaded && (Data.Count != next.Count || next.Any(p => !Data.TryGetValue(p.Key, out var value) || value != p.Value));
                    Data = next; _loaded = true;
                    if (changed) _log.Write(Path.GetFileName(_path), "applied");
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or System.Text.Json.JsonException)
                {
                    if (!_loaded) throw new InvalidOperationException("An appsettings file could not be loaded. Check JSON syntax and file access before starting the API.");
                    _log.Write(Path.GetFileName(_path), "invalid");
                }
            }
            if (changed) OnReload();
        }

        public void Dispose()
        {
            IDisposable? watch;
            lock (_gate) { _disposed = true; watch = _watch; _watch = null; }
            watch?.Dispose(); _timer.Dispose();
        }
    }
}
