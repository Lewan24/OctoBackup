using System.ComponentModel.DataAnnotations;

namespace Modules.Auth.Shared.ActionsRequests;

public sealed class ChangePasswordRequest
{
    [Required] [MaxLength(128)] public string CurrentPassword { get; set; } = null!;

    [Required] [MaxLength(128)] public string NewPassword { get; set; } = null!;

    [Required]
    [MaxLength(128)]
    [Compare(nameof(NewPassword))]
    public string ConfirmNewPassword { get; set; } = null!;
}
