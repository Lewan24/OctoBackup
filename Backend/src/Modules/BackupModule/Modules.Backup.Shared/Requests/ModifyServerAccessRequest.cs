namespace Modules.Backup.Shared.Requests;

public record ModifyServerAccessRequest(Guid ServerId, string UserEmail);
