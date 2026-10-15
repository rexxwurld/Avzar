using System.ComponentModel.DataAnnotations;

namespace Tailoring.Application.Locations;

public sealed record LocationDto(Guid Id, string Name, string? Address, bool IsActive);

public sealed record CreateLocationRequest
{
    [Required, StringLength(120, MinimumLength = 2)] public string Name { get; init; } = "";
    [StringLength(500)] public string? Address { get; init; }
}

public interface ILocationService
{
    Task<IReadOnlyList<LocationDto>> ListAsync(CancellationToken ct = default);
    Task<LocationDto> CreateAsync(CreateLocationRequest request, CancellationToken ct = default);
}

/// <summary>
/// Which locations the signed-in staff member may see orders and fittings for.
/// Owners and holders of locations.all are unrestricted. Everyone else sees only their assigned locations,
/// so a member with no locations sees nothing. Calls with no signed-in user (public and system flows) are unscoped.
/// </summary>
public interface ILocationScope
{
    /// <summary>null means every location. An empty array means none.</summary>
    Task<Guid[]?> AllowedLocationIdsAsync(CancellationToken ct = default);

    /// <summary>Throws 403 location_forbidden when the caller may not act on that location.</summary>
    Task EnsureAllowedAsync(Guid locationId, CancellationToken ct = default);
}
