using System.ComponentModel.DataAnnotations;

namespace Modules.Auth.Core.Entities;

public sealed class TokenModel
{
    [Key] public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(100)] public string? Email { get; set; }

    [MaxLength(100)] public string? Token { get; set; }

    public DateTime ExpirationDate { get; set; } = DateTime.UtcNow.AddHours(8);
}
