using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailoring.Infrastructure.Persistence.Migrations
{
    public partial class AddWorkerAvailabilityAndLeave : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkerAvailability",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    IsAvailable = table.Column<bool>(type: "boolean", nullable: false),
                    OpensAt = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    ClosesAt = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    CapacityMinutes = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkerAvailability", x => x.Id);
                    table.CheckConstraint("CK_WorkerAvailability_Day", "\"DayOfWeek\" BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_WorkerAvailability_Window", "(\"IsAvailable\" = FALSE AND \"OpensAt\" IS NULL AND \"ClosesAt\" IS NULL AND \"CapacityMinutes\" = 0) OR (\"IsAvailable\" = TRUE AND \"OpensAt\" IS NOT NULL AND \"ClosesAt\" IS NOT NULL AND \"OpensAt\" < \"ClosesAt\" AND \"CapacityMinutes\" BETWEEN 1 AND 1440)");
                    table.ForeignKey("FK_WorkerAvailability_Businesses_BusinessId", x => x.BusinessId, "Businesses", "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("FK_WorkerAvailability_Memberships_MembershipId_BusinessId", x => new { x.MembershipId, x.BusinessId }, "Memberships", new[] { "Id", "BusinessId" }, onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkerLeaves",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkerLeaves", x => x.Id);
                    table.CheckConstraint("CK_WorkerLeaves_Interval", "\"StartsAtUtc\" < \"EndsAtUtc\"");
                    table.ForeignKey("FK_WorkerLeaves_Businesses_BusinessId", x => x.BusinessId, "Businesses", "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("FK_WorkerLeaves_Memberships_MembershipId_BusinessId", x => new { x.MembershipId, x.BusinessId }, "Memberships", new[] { "Id", "BusinessId" }, onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex("IX_WorkerAvailability_BusinessId", "WorkerAvailability", "BusinessId");
            migrationBuilder.CreateIndex("IX_WorkerAvailability_BusinessId_MembershipId_DayOfWeek", "WorkerAvailability", new[] { "BusinessId", "MembershipId", "DayOfWeek" }, unique: true);
            migrationBuilder.CreateIndex("IX_WorkerAvailability_MembershipId_BusinessId", "WorkerAvailability", new[] { "MembershipId", "BusinessId" });
            migrationBuilder.CreateIndex("IX_WorkerLeaves_BusinessId", "WorkerLeaves", "BusinessId");
            migrationBuilder.CreateIndex("IX_WorkerLeaves_BusinessId_MembershipId_StartsAtUtc_EndsAtUtc", "WorkerLeaves", new[] { "BusinessId", "MembershipId", "StartsAtUtc", "EndsAtUtc" });
            migrationBuilder.CreateIndex("IX_WorkerLeaves_MembershipId_BusinessId", "WorkerLeaves", new[] { "MembershipId", "BusinessId" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "WorkerAvailability");
            migrationBuilder.DropTable(name: "WorkerLeaves");
        }
    }
}
