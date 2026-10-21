using Microsoft.AspNetCore.Identity;

namespace Modules.Auth.Core.Entities;

public sealed class AppUser : IdentityUser
{
    public bool IsBlocked { get; set; }
}
