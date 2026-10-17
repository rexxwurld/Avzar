using Tailoring.Application.Locations;

namespace Tailoring.IntegrationTests;

/// <summary>
/// Explicit scope for isolated service tests that intentionally model an unrestricted caller/system flow.
/// Production services must always receive a registered ILocationScope; this helper is test-only.
/// </summary>
internal sealed class UnrestrictedLocationScope : ILocationScope
{
    public Task<Guid[]?> AllowedLocationIdsAsync(CancellationToken ct = default)
        => Task.FromResult<Guid[]?>(null);

    public Task EnsureAllowedAsync(Guid locationId, CancellationToken ct = default)
        => Task.CompletedTask;
}
