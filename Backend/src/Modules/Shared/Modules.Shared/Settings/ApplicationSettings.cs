using System.Net;
using System.Text.RegularExpressions;

namespace Modules.Shared.Settings;

public sealed record ApplicationSettings
{
    public string Revision { get; init; } = "initial";
    public string[] AllowedDatabaseHosts { get; init; } = [];
    public bool RegistrationEnabled { get; init; }
    public bool AutoConfirmAccounts { get; init; }
    public int DefaultRetentionDays { get; init; } = 4;
    public int SchedulePollSeconds { get; init; } = 300;
    public int BackupQueueDelaySeconds { get; init; } = 20;
    public int BackupQueueSpacingSeconds { get; init; } = 5;
    public DateTimeOffset? UpdatedAt { get; init; }
    public string? UpdatedBy { get; init; }
}

public interface IApplicationSettings
{
    ApplicationSettings Current { get; }
}

public static class DatabaseHostPolicy
{
    public const string DeniedMessage = "Host is not approved. An administrator can add it in Application settings > Allowed database hosts.";

    public static string Normalize(string? value)
    {
        var host = value?.Trim().TrimEnd('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(host) || host.Length > 253 || host.Contains('%'))
            throw new ArgumentException("Enter a hostname or IP address, without a URL, port, path or wildcard.");
        if (IPAddress.TryParse(host, out var address)) return address.ToString();
        if (!Regex.IsMatch(host, @"\A[a-z0-9](?:[a-z0-9.-]*[a-z0-9])?\z") ||
            host.Split('.').Any(label => label.Length is 0 or > 63 || label.StartsWith('-') || label.EndsWith('-')))
            throw new ArgumentException("Enter a hostname or IP address, without a URL, port, path or wildcard.");
        return host;
    }

    public static bool Allows(ApplicationSettings settings, string? host)
    {
        try { return settings.AllowedDatabaseHosts.Contains(Normalize(host), StringComparer.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
    }

    public static bool AllowsConnection(ApplicationSettings settings, string? host, bool usesTunnel, string? sshHost, string? remoteHost)
        => Allows(settings, host) && (!usesTunnel || (Allows(settings, sshHost) && Allows(settings, remoteHost)));
}
