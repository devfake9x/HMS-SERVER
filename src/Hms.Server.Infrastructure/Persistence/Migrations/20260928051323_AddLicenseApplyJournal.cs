using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLicenseApplyJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "license_apply_journals",
                columns: table => new
                {
                    JournalId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    PriorGeneration = table.Column<int>(type: "integer", nullable: false),
                    PriorRawSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    TargetGeneration = table.Column<int>(type: "integer", nullable: false),
                    TargetRawSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_license_apply_journals", x => x.JournalId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_license_apply_journals_DeviceId_State_UpdatedAt",
                table: "license_apply_journals",
                columns: new[] { "DeviceId", "State", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "license_apply_journals");
        }
    }
}
