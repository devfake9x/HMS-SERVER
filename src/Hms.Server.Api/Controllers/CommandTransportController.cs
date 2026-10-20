using Microsoft.AspNetCore.Mvc;

namespace Hms.Server.Api.Controllers;

[ApiController]
[Route("api/v1/command-transport")]
public sealed class CommandTransportController : ControllerBase
{
    [HttpGet("status")]
    public IActionResult GetStatus() => Ok(new
    {
        status = "MQTT_ACTIVE",
        mode = "live",
        canPublish = true,
        code = "MQTT_ACTIVE",
        broker = "broker.emqx.io:1883",
        topicPrefix = "hms_toan_lab",
        message = "Live MQTT delivery active via broker.emqx.io and ESP32-S3 RTU gateway."
    });
}
