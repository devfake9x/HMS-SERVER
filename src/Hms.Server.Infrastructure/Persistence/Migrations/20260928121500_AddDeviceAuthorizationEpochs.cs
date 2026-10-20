using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Hms.Server.Infrastructure.Persistence;

#nullable disable

namespace Hms.Server.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260928121500_AddDeviceAuthorizationEpochs")]
public partial class AddDeviceAuthorizationEpochs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "device_authorization_epochs",
            columns: table => new
            {
                DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                Epoch = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_device_authorization_epochs", x => x.DeviceId);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "device_authorization_epochs");
    }
}
