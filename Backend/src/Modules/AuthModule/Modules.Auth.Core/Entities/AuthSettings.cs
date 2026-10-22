namespace Modules.Auth.Core.Entities;

public sealed record AuthSettings
{
    public bool AutoConfirmAccount { get; init; }
    public int DefaultTokenExpirationTimeInMinutes { get; init; } = 480;
    public bool EnableRegisterModule { get; init; }
    public string? MainAdminEmailAddress { get; init; }
}
