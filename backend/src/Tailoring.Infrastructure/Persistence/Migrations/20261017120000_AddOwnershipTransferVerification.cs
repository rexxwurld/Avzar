using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Tailoring.Infrastructure.Persistence;

#nullable disable
namespace Tailoring.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261017120000_AddOwnershipTransferVerification")]
public sealed class AddOwnershipTransferVerification : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable(
            name: "OwnershipTransferChallenges",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                RequestingUserId = table.Column<Guid>(type: "uuid", nullable: false),
                NewOwnerMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OwnershipTransferChallenges", x => x.Id);
                table.ForeignKey("FK_OwnershipTransferChallenges_Businesses_BusinessId", x => x.BusinessId, "Businesses", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_OwnershipTransferChallenges_Memberships_NewOwnerMembershipId_BusinessId", x => new { x.NewOwnerMembershipId, x.BusinessId }, "Memberships", new[] { "Id", "BusinessId" }, onDelete: ReferentialAction.Restrict);
            });

        m.CreateIndex("IX_OwnershipTransferChallenges_BusinessId", "OwnershipTransferChallenges", "BusinessId");
        m.CreateIndex("IX_OwnershipTransferChallenges_TokenHash", "OwnershipTransferChallenges", "TokenHash", unique: true);
        m.CreateIndex("IX_OwnershipTransferChallenges_BusinessId_RequestingUserId_ExpiresAtUtc", "OwnershipTransferChallenges", new[] { "BusinessId", "RequestingUserId", "ExpiresAtUtc" });
        m.CreateIndex("IX_OwnershipTransferChallenges_BusinessId_ConsumedAtUtc_CancelledAtUtc", "OwnershipTransferChallenges", new[] { "BusinessId", "ConsumedAtUtc", "CancelledAtUtc" });
        m.CreateIndex("IX_OwnershipTransferChallenges_NewOwnerMembershipId_BusinessId", "OwnershipTransferChallenges", new[] { "NewOwnerMembershipId", "BusinessId" });
    }

    protected override void Down(MigrationBuilder m) => m.DropTable("OwnershipTransferChallenges");
}
