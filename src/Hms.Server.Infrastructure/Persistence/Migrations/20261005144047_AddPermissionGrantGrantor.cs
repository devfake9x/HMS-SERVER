using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPermissionGrantGrantor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GrantedBy",
                table: "permission_grants",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "GrantedByMasterAdmin",
                table: "permission_grants",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GrantedBy",
                table: "permission_grants");

            migrationBuilder.DropColumn(
                name: "GrantedByMasterAdmin",
                table: "permission_grants");
        }
    }
}
