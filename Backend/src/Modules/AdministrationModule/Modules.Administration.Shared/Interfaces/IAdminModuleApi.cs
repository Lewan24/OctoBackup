namespace Modules.Administration.Shared.Interfaces;

public interface IAdminModuleApi
{
    Task<bool> AmIAdmin(string? username);
}
