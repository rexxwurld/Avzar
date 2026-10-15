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
