using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommandAuditStatusIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_command_audits_Status",
                table: "command_audits",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_command_audits_Status",
                table: "command_audits");
        }
    }
}
