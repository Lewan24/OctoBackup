using System.ComponentModel.DataAnnotations;

namespace Modules.Auth.Shared.ActionsRequests;

public sealed class ChangeEmailRequest
{
    [Required] [EmailAddress] [MaxLength(100)] public string NewEmail { get; set; } = null!;

    [Required] public string CurrentPassword { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    [Compare(nameof(NewEmail))]
    public string ConfirmNewEmail { get; set; } = null!;
}
