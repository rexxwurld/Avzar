using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Tailoring.Infrastructure.Persistence;

#nullable disable
namespace Tailoring.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261017090000_AddInventory")]
public partial class AddInventory : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.CreateTable("Materials", table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false), BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
            LocationId = table.Column<Guid>(type: "uuid", nullable: true), Sku = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
            Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false), Unit = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
            OnHand = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false), Reserved = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
            ReorderLevel = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false), Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
            CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false), UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_Materials", x => x.Id);
            table.UniqueConstraint("AK_Materials_Id_BusinessId", x => new { x.Id, x.BusinessId });
            table.CheckConstraint("CK_Materials_Quantities", "\"OnHand\" >= 0 AND \"Reserved\" >= 0 AND \"Reserved\" <= \"OnHand\" AND \"ReorderLevel\" >= 0");
            table.ForeignKey("FK_Materials_Businesses_BusinessId", x => x.BusinessId, "Businesses", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_Materials_Locations_LocationId_BusinessId", x => new { x.LocationId, x.BusinessId }, "Locations", new[] { "Id", "BusinessId" }, onDelete: ReferentialAction.Restrict);
        });
        m.CreateTable("StockMovements", table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false), BusinessId = table.Column<Guid>(type: "uuid", nullable: false), MaterialId = table.Column<Guid>(type: "uuid", nullable: false),
            Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false), Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
            OnHandAfter = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false), ReservedAfter = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
            OrderId = table.Column<Guid>(type: "uuid", nullable: true), TaskId = table.Column<Guid>(type: "uuid", nullable: true), ActorUserId = table.Column<Guid>(type: "uuid", nullable: true), Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
            CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false), UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_StockMovements", x => x.Id);
            table.ForeignKey("FK_StockMovements_Businesses_BusinessId", x => x.BusinessId, "Businesses", "Id", onDelete: ReferentialAction.Restrict);
            table.ForeignKey("FK_StockMovements_Materials_MaterialId_BusinessId", x => new { x.MaterialId, x.BusinessId }, "Materials", new[] { "Id", "BusinessId" }, onDelete: ReferentialAction.Restrict);
        });
        m.CreateIndex("IX_Materials_BusinessId", "Materials", "BusinessId");
        m.CreateIndex("IX_Materials_BusinessId_Sku", "Materials", new[] { "BusinessId", "Sku" }, unique: true);
        m.CreateIndex("IX_Materials_LocationId_BusinessId", "Materials", new[] { "LocationId", "BusinessId" });
        m.CreateIndex("IX_StockMovements_BusinessId", "StockMovements", "BusinessId");
        m.CreateIndex("IX_StockMovements_BusinessId_MaterialId_CreatedAtUtc", "StockMovements", new[] { "BusinessId", "MaterialId", "CreatedAtUtc" });
        m.CreateIndex("IX_StockMovements_MaterialId_BusinessId", "StockMovements", new[] { "MaterialId", "BusinessId" });
    }
    protected override void Down(MigrationBuilder m) { m.DropTable("StockMovements"); m.DropTable("Materials"); }
}
