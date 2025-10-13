namespace Modules.Backup.Core.Entities.DbContext;

public sealed record DbServerTunnel
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public required string ServerHost { get; set; }
    public ushort SshPort { get; set; } = 22;
    public required string Username { get; set; }
    public bool UsePasswordAuth { get; set; } = true;
    public string? Password { get; set; }
    public string? PrivateKeyContent { get; set; }
    public string? PrivateKeyPassphrase { get; set; }
    public ushort LocalPort { get; set; }
    public required string RemoteHost { get; set; }
    public ushort RemotePort { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; }
}