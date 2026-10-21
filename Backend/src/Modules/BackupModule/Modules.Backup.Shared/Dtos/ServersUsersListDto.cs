namespace Modules.Backup.Shared.Dtos;

public record ServersUsersListDto
{
    public Guid ServerId { get; set; }
    public bool IsServerDisabled { get; set; }
    public string? ServerConnectionName { get; set; }
    public int UsersWithAccess { get; set; }
}
