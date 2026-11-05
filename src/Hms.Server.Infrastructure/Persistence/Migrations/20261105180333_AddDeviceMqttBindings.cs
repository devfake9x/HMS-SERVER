using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Hms.Server.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceMqttBindings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "device_mqtt_bindings",
                columns: table => new
                {
                    DeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    TopicPrefix = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MqttDeviceId = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    KeyVersion = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_mqtt_bindings", x => x.DeviceId);
                    table.ForeignKey(
                        name: "FK_device_mqtt_bindings_devices_DeviceId",
                        column: x => x.DeviceId,
                        principalTable: "devices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_device_mqtt_bindings_TopicPrefix_MqttDeviceId",
                table: "device_mqtt_bindings",
                columns: new[] { "TopicPrefix", "MqttDeviceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_mqtt_bindings");
        }
    }
}
