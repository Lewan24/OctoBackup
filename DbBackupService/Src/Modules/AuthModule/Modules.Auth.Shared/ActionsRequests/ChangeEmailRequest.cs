using System.ComponentModel.DataAnnotations;

namespace Modules.Auth.Shared.ActionsRequests;

public sealed class ChangeEmailRequest
{
    [Required] [MaxLength(60)] public string NewEmail { get; set; } = null!;

    [Required]
    [MaxLength(60)]
    [Compare(nameof(NewEmail))]
    public string ConfirmNewEmail { get; set; } = null!;
}