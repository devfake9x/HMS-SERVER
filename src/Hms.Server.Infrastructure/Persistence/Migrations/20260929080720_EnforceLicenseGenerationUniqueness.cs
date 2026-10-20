using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceLicenseGenerationUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_licenses_DeviceId_Generation",
                table: "licenses");

            migrationBuilder.CreateIndex(
                name: "IX_licenses_DeviceId_Generation",
                table: "licenses",
                columns: new[] { "DeviceId", "Generation" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_licenses_DeviceId_Generation",
                table: "licenses");

            migrationBuilder.CreateIndex(
                name: "IX_licenses_DeviceId_Generation",
                table: "licenses",
                columns: new[] { "DeviceId", "Generation" });
        }
    }
}
