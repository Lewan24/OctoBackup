using System.ComponentModel.DataAnnotations;

namespace Modules.Auth.Shared.ActionsRequests;

public sealed class LoginRequest
{
    [Required] [EmailAddress] [MaxLength(254)] public string Email { get; set; } = null!;

    [Required] [MaxLength(128)] public string Password { get; set; } = null!;

    [Required] public bool RememberMe { get; set; } = true;
}
