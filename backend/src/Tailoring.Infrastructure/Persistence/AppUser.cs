using Microsoft.AspNetCore.Identity;

namespace Tailoring.Infrastructure.Persistence;

public class AppUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = "";
    /// <summary>Newest TOTP time step already accepted (replay protection). Written only by <c>TotpVerifier</c>, never by normal saves.</summary>
    public long? LastTotpStep { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
