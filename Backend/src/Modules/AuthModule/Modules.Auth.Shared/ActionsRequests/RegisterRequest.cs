using System.ComponentModel.DataAnnotations;

namespace Modules.Auth.Shared.ActionsRequests;

public sealed class RegisterRequest
{
    [Required] [EmailAddress] public string? Email { get; set; }

    [Required] public string? Password { get; set; }

    [Required]
    [Compare(nameof(Password), ErrorMessage = "Passwords do not match!")]
    public string? PasswordConfirm { get; set; }
}
