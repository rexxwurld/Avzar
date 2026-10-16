using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Tailoring.Infrastructure.Persistence;
#nullable disable
namespace Tailoring.Infrastructure.Persistence.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20261017120000_AddWorkerLeaveReview")]
public partial class AddWorkerLeaveReview : Migration
{
    protected override void Up(MigrationBuilder m)
    {
        m.AddColumn<Guid>(name: "ReviewedByUserId", table: "WorkerLeaves", type: "uuid", nullable: true);
        m.AddColumn<DateTime>(name: "ReviewedAtUtc", table: "WorkerLeaves", type: "timestamp with time zone", nullable: true);
        m.AddColumn<string>(name: "ReviewNote", table: "WorkerLeaves", type: "character varying(500)", maxLength: 500, nullable: true);
    }
    protected override void Down(MigrationBuilder m)
    {
        m.DropColumn(name: "ReviewedByUserId", table: "WorkerLeaves");
        m.DropColumn(name: "ReviewedAtUtc", table: "WorkerLeaves");
        m.DropColumn(name: "ReviewNote", table: "WorkerLeaves");
    }
}
