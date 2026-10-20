using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceRolesAndMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "device_access_audits",
                columns: table => new
                {
                    AuditId = table.Column<Guid>(type: "uuid", nullable: false),
                    Actor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Change = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreviousValue = table.Column<string>(type: "text", nullable: true),
                    NewValue = table.Column<string>(type: "text", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_access_audits", x => x.AuditId);
                });

            migrationBuilder.CreateTable(
                name: "device_roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_roles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_device_roles_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "device_memberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_memberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_device_memberships_device_roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "device_roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_device_memberships_devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "device_role_permissions",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Permission = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_role_permissions", x => new { x.RoleId, x.Permission });
                    table.ForeignKey(
                        name: "FK_device_role_permissions_device_roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "device_roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_device_access_audits_DeviceId_OccurredAt",
                table: "device_access_audits",
                columns: new[] { "DeviceId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_device_access_audits_RoleId_OccurredAt",
                table: "device_access_audits",
                columns: new[] { "RoleId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_device_memberships_DeviceId",
                table: "device_memberships",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_device_memberships_RoleId",
                table: "device_memberships",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_device_memberships_UserId_DeviceId",
                table: "device_memberships",
                columns: new[] { "UserId", "DeviceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_device_roles_OrganizationId_NormalizedName",
                table: "device_roles",
                columns: new[] { "OrganizationId", "NormalizedName" },
                unique: true);

            // Align the policy counter with every existing grant row (including
            // the device-action rows deleted next), so later Advance() values
            // sort above them and the version never moves backwards.
            migrationBuilder.Sql("""UPDATE policy_versions SET "Version" = GREATEST("Version", COALESCE((SELECT MAX("PolicyVersion") FROM permission_grants), 0)) WHERE "Id" = 1;""");

            // Device-action grants are replaced by device roles and memberships.
            migrationBuilder.Sql("""DELETE FROM permission_grants WHERE "Kind" = 'device-action';""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The deleted device-action grant rows are not restored, and the
            // policy counter is not lowered.
            migrationBuilder.DropTable(
                name: "device_access_audits");

            migrationBuilder.DropTable(
                name: "device_memberships");

            migrationBuilder.DropTable(
                name: "device_role_permissions");

            migrationBuilder.DropTable(
                name: "device_roles");
        }
    }
}
