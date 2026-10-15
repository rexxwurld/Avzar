using Microsoft.AspNetCore.Identity;

namespace Tailoring.Infrastructure.Persistence;

public class AppUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
