using System.ComponentModel.DataAnnotations;

namespace Tailoring.Application.Roles;

public sealed record RoleDto(Guid Id, string Name, bool IsSystem, IReadOnlyList<string> Permissions);

public sealed record CreateRoleRequest
{
    [Required, StringLength(60, MinimumLength = 2)] public string Name { get; init; } = "";
    public List<string> Permissions { get; init; } = new();
}

public sealed record SetRolePermissionsRequest
{
    public List<string> Permissions { get; init; } = new();
}

public interface IRoleService
{
    Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct = default);
    IReadOnlyList<string> ListAvailablePermissions();
    Task<RoleDto> CreateAsync(CreateRoleRequest request, CancellationToken ct = default);
    Task<RoleDto> SetPermissionsAsync(Guid roleId, SetRolePermissionsRequest request, CancellationToken ct = default);
}
