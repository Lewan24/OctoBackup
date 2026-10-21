namespace Modules.Auth.Shared.Dtos;

public sealed class UserDto
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Email { get; set; }
    public bool IsBlocked { get; set; }
    public bool IsEmailConfirmed { get; set; }
    public IList<string>? Roles { get; set; }
}
