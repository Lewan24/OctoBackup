using System.Security.Cryptography;
using Renci.SshNet;
using Modules.Shared.Common;

namespace Modules.Backup.Core.StaticClasses;

public static class SshHostKeys
{
    // Map exact SSH hostname to OpenSSH SHA256 fingerprint in a mounted JSON file.
    public static void Configure(SshClient client, string host)
    {
        var file = DbCommon.SshHostKeysFile;
        if (string.IsNullOrWhiteSpace(file)) throw new InvalidOperationException("SSH host-key pins are not configured.");
        var pins = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));
        if (pins is null || !pins.TryGetValue(host, out var expected)) throw new InvalidOperationException("No SSH host-key pin exists for this host.");
        client.HostKeyReceived += (_, e) =>
        {
            var actual = "SHA256:" + Convert.ToBase64String(SHA256.HashData(e.HostKey)).TrimEnd('=');
            e.CanTrust = CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(actual), System.Text.Encoding.UTF8.GetBytes(expected));
        };
    }
}
