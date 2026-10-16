using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Tailoring.Infrastructure.Persistence;

#nullable disable
namespace Tailoring.Infrastructure.Persistence.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261017100000_AddCustomerPortalGrants")]
public partial class AddCustomerPortalGrants : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("CustomerPortalGrants", table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false), BusinessId = table.Column<Guid>(type: "uuid", nullable: false), CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
            TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false), ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
            RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true), LastUsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true), UseCount = table.Column<int>(type: "integer", nullable: false),
            CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false), UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_CustomerPortalGrants", x => x.Id);
            table.CheckConstraint("CK_CustomerPortalGrants_UseCount", "\"UseCount\" >= 0");
            table.ForeignKey("FK_CustomerPortalGrants_Businesses_BusinessId", x => x.BusinessId, "Businesses", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_CustomerPortalGrants_Customers_CustomerId_BusinessId", x => new { x.CustomerId, x.BusinessId }, "Customers", new[] { "Id", "BusinessId" }, onDelete: ReferentialAction.Cascade);
        });
        m.CreateIndex("IX_CustomerPortalGrants_BusinessId", "CustomerPortalGrants", "BusinessId");
        m.CreateIndex("IX_CustomerPortalGrants_BusinessId_CustomerId_ExpiresAtUtc", "CustomerPortalGrants", new[] { "BusinessId", "CustomerId", "ExpiresAtUtc" });
        m.CreateIndex("IX_CustomerPortalGrants_CustomerId_BusinessId", "CustomerPortalGrants", new[] { "CustomerId", "BusinessId" });
        m.CreateIndex("IX_CustomerPortalGrants_TokenHash", "CustomerPortalGrants", "TokenHash", unique: true);
    }
    protected override void Down(MigrationBuilder m) => m.DropTable("CustomerPortalGrants");
}
