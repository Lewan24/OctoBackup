namespace Modules.Backup.Core.Entities.DbContext;

public sealed class BackupEncryptionKey
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? ActiveScope { get; set; }
    public string Scope { get; set; } = "global";
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    public required string Salt { get; set; }
    public required string ProtectedKey { get; set; }
    public string? ProtectedRecoveryPassword { get; set; }
}
