using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommandAuditAndProvisioningBundles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Action",
                table: "command_audits",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExpiresAt",
                table: "command_audits",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<int>(
                name: "KeyGeneration",
                table: "command_audits",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "command_audits",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "provisioning_bundles",
                columns: table => new
                {
                    BundleId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Used = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProtectedPayload = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provisioning_bundles", x => x.BundleId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_provisioning_bundles_DeviceId_Used_ExpiresAt",
                table: "provisioning_bundles",
                columns: new[] { "DeviceId", "Used", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "provisioning_bundles");

            migrationBuilder.DropColumn(
                name: "Action",
                table: "command_audits");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "command_audits");

            migrationBuilder.DropColumn(
                name: "KeyGeneration",
                table: "command_audits");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "command_audits");
        }
    }
}
