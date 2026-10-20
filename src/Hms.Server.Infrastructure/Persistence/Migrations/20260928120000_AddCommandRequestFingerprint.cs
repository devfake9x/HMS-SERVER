using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Hms.Server.Infrastructure.Persistence;

#nullable disable

namespace Hms.Server.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260928120000_AddCommandRequestFingerprint")]
public partial class AddCommandRequestFingerprint : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "RequestFingerprint",
            table: "command_audits",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "RequestFingerprint",
            table: "command_audits");
    }
}
