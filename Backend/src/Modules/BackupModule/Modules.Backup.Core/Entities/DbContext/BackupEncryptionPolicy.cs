namespace Modules.Backup.Core.Entities.DbContext;

public sealed class BackupEncryptionPolicy
{
    public int Id { get; set; } = 1;
    public bool GlobalEnabled { get; set; }
    public string Revision { get; set; } = Guid.NewGuid().ToString("N");
}
