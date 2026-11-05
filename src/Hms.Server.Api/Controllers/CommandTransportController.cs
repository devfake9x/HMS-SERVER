using Hms.Server.Application.DeviceLink;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hms.Server.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/command-transport")]
public sealed class CommandTransportController(IDeviceLinkTransportStatus link) : ControllerBase
{
    /// <summary>The server's own broker session, as it is now.</summary>
    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        var current = link.Current;
        var (status, code) = !current.Enabled
            ? ("MQTT_DEFERRED", "MQTT_DEFERRED")
            : current.Connected
                ? ("CONNECTED", "MQTT_CONNECTED")
                : ("UNAVAILABLE", "MQTT_DISCONNECTED");
        Response.Headers.CacheControl = "no-store";
        return Ok(new
        {
            status,
            code,
            mode = current.Enabled ? "live" : "deferred",
            canPublish = current.Enabled && current.Connected,
            broker = current.Broker,
            connectedSince = current.ConnectedSince,
            lastError = current.LastError
        });
    }
}
