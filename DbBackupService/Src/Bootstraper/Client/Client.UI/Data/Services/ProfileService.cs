using Modules.Auth.Shared.ActionsRequests;
using OneOf;
using OneOf.Types;

namespace Client.UI.Data.Services;

public sealed class ProfileService(TokenHttpClientService api)
{
    public async Task<OneOf<Success, string>> ChangeEmail(ChangeEmailRequest request) 
        => await api.PostAsync("/api/auth/ChangeEmail", request);

    public async Task<OneOf<Success, string>>? ChangePassword(ChangePasswordRequest request) 
        => await api.PostAsync("/api/auth/ChangePassword", request);
}