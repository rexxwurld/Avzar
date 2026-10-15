using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tailoring.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLastTotpStep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LastTotpStep",
                table: "AspNetUsers",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastTotpStep",
                table: "AspNetUsers");
        }
    }
}
