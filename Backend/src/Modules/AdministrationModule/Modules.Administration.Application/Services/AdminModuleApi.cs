using Modules.Administration.Shared.Interfaces;

namespace Modules.Administration.Application.Services;

internal class AdminModuleApi(AdminService service) : IAdminModuleApi
{
    public async Task<bool> AmIAdmin(string? username)
    {
        var result = await service.IsUserAdmin(username);

        return result ?? false;
    }
}
