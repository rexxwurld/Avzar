using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailoring.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOneOwnerGuardAndLocationsAllBackfill : Migration
    {
        /// <summary>
        /// Businesses created before the "locations.all" permission existed have Manager and Accountant roles without it, so their
        /// holders were treated as location-restricted. This gives the permission to those SYSTEM roles only, and skips any role whose
        /// permissions were ever edited through the API (an audit row "role.permissions_changed" exists): that omission may be deliberate.
        /// Idempotent. Public so a test can run exactly this SQL.
        /// </summary>
        public const string BackfillLocationsAllSql = """
            INSERT INTO "RolePermissions" ("RoleId", "BusinessId", "Permission")
            SELECT r."Id", r."BusinessId", 'locations.all'
            FROM "BusinessRoles" r
            WHERE r."IsSystem" AND r."Name" IN ('Manager', 'Accountant')
              AND NOT EXISTS (SELECT 1 FROM "RolePermissions" p WHERE p."RoleId" = r."Id" AND p."Permission" = 'locations.all')
              AND NOT EXISTS (SELECT 1 FROM "AuditLogs" a WHERE a."BusinessId" = r."BusinessId" AND a."Action" = 'role.permissions_changed' AND a."EntityId" = r."Id")
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(BackfillLocationsAllSql);

            // Fails (and names the business) if some business already has two owners: fix that data by hand first.
            migrationBuilder.CreateIndex(
                name: "UX_Memberships_OneOwner",
                table: "Memberships",
                column: "BusinessId",
                unique: true,
                filter: "\"IsOwner\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The back-filled permission rows are not removed: they cannot be told apart from rows that existed before.
            migrationBuilder.DropIndex(
                name: "UX_Memberships_OneOwner",
                table: "Memberships");
        }
    }
}
