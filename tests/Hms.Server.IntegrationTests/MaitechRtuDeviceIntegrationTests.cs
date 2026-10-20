using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hms.Server.Application.Commands;
using Hms.Server.Domain.Commands;
using Hms.Server.Domain.Devices;
using Hms.Server.Infrastructure.Commands;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Hms.Server.IntegrationTests;

public sealed class MaitechRtuDeviceIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private static readonly Guid MaitechRtuGuid = new("870c870c-870c-870c-870c-870c870c870c");

    public MaitechRtuDeviceIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ProtocolAdapter_MapsMaitechRtuToHardwareIdGateway870c()
    {
        var adapter = new HmsIdfProvisionalProtocolAdapter();
        var deviceId = new DeviceId(MaitechRtuGuid);
        var userId = new Hms.Server.Domain.Authorization.UserId(Guid.NewGuid());
        var request = new DeviceCommandRequest(
            deviceId,
            userId,
            "control",
            "set_output",
            new Dictionary<string, object?> { ["channel"] = 1, ["value"] = true },
            "corr-test-870c",
            DateTimeOffset.UtcNow,
            false);

        var definition = new CommandDefinition("control", "set_output", "device.command", CommandAvailability.Enabled, false, false, 120_000);
        var plan = new CommandExecutionPlan(request, definition, 1, DateTimeOffset.UtcNow.AddMinutes(1));

        var prepared = await adapter.PrepareAsync(plan);

        Assert.Equal("hms_toan_lab/gateway_870c/cmd", prepared.Topic);
        Assert.True(prepared.Payload.Length > 0);
    }

    [Fact]
    public async Task ProtocolAdapter_UsesStandardGuid_ForOtherDevices()
    {
        var adapter = new HmsIdfProvisionalProtocolAdapter();
        var normalGuid = Guid.NewGuid();
        var deviceId = new DeviceId(normalGuid);
        var userId = new Hms.Server.Domain.Authorization.UserId(Guid.NewGuid());
        var request = new DeviceCommandRequest(
            deviceId,
            userId,
            "system",
            "ping",
            new Dictionary<string, object?> { ["nonce"] = "test" },
            "corr-test-normal",
            DateTimeOffset.UtcNow,
            false);

        var definition = new CommandDefinition("system", "ping", "device.command", CommandAvailability.Enabled, false, false, 120_000);
        var plan = new CommandExecutionPlan(request, definition, 1, DateTimeOffset.UtcNow.AddMinutes(1));

        var prepared = await adapter.PrepareAsync(plan);

        Assert.Equal($"hms_toan_lab/{normalGuid:N}/cmd", prepared.Topic);
    }

    [Fact]
    public async Task TelemetryIngestion_AcceptsAndServesRichRtuTelemetry()
    {
        var client = _factory.CreateClient();

        // 1. Authenticate as master admin
        var csrfRes = await client.GetAsync("/api/v1/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, csrfRes.StatusCode);
        var csrf = await csrfRes.Content.ReadFromJsonAsync<JsonElement>();
        var token = csrf.GetProperty("requestToken").GetString();

        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new
            {
                email = "admin@hms.local",
                password = "AdminPassword123!",
                rememberMe = false
            })
        };
        loginReq.Headers.Add("X-CSRF-TOKEN", token);
        var loginRes = await client.SendAsync(loginReq);
        Assert.True(loginRes.StatusCode == HttpStatusCode.NoContent || loginRes.StatusCode == HttpStatusCode.OK);

        // 2. Query device list to find MAITECH-RTU-870C
        var devRes = await client.GetAsync("/api/v1/devices");
        Assert.Equal(HttpStatusCode.OK, devRes.StatusCode);
        var devices = await devRes.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.NotNull(devices);

        var rtu = devices.FirstOrDefault(d => d.GetProperty("id").GetString() == MaitechRtuGuid.ToString());
        Assert.True(rtu.ValueKind != JsonValueKind.Undefined, "MAITECH-RTU-870C must be seeded in device list");
        Assert.Equal("MAITECH-RTU-870C", rtu.GetProperty("name").GetString());
        Assert.Equal("Active", rtu.GetProperty("state").GetString());

        // 3. Post telemetry payload for MAITECH-RTU
        var telemetryPayload = new Dictionary<string, object?>
        {
            ["hostname"] = "maitech-rtu",
            ["uptime_seconds"] = 443959,
            ["free_heap"] = 1464491,
            ["min_free_heap"] = 681248,
            ["internal_free"] = 91639,
            ["mqtt_connected"] = true,
            ["voltage"] = "3.62",
            ["temperature"] = "28.4",
            ["digital_outputs"] = new[]
            {
                new { channel = 1, gpioPin = 21, state = true, mode = "toggle" },
                new { channel = 2, gpioPin = 35, state = false, mode = "toggle" }
            }
        };

        var postTelRes = await client.PostAsJsonAsync($"/api/v1/devices/{MaitechRtuGuid}/telemetry", telemetryPayload);
        Assert.Equal(HttpStatusCode.OK, postTelRes.StatusCode);

        // 4. Retrieve telemetry to verify persistence
        var getTelRes = await client.GetAsync($"/api/v1/devices/{MaitechRtuGuid}/telemetry");
        Assert.Equal(HttpStatusCode.OK, getTelRes.StatusCode);
        var servedTelemetry = await getTelRes.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(MaitechRtuGuid.ToString(), servedTelemetry.GetProperty("deviceId").GetString());
        var data = servedTelemetry.GetProperty("data");
        Assert.Equal("maitech-rtu", data.GetProperty("hostname").GetString());
        Assert.Equal(443959, data.GetProperty("uptime_seconds").GetInt32());
        Assert.True(data.GetProperty("mqtt_connected").GetBoolean());
    }

    [Fact]
    public async Task TelemetryIngestion_PreservesDigitalOutputs_WhenPartialMqttStatusIngested()
    {
        var client = _factory.CreateClient();

        // 1. Authenticate as master admin
        var csrfRes = await client.GetAsync("/api/v1/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, csrfRes.StatusCode);
        var csrf = await csrfRes.Content.ReadFromJsonAsync<JsonElement>();
        var token = csrf.GetProperty("requestToken").GetString();

        var loginReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new
            {
                email = "admin@hms.local",
                password = "AdminPassword123!",
                rememberMe = false
            })
        };
        loginReq.Headers.Add("X-CSRF-TOKEN", token);
        var loginRes = await client.SendAsync(loginReq);
        Assert.True(loginRes.StatusCode == HttpStatusCode.NoContent || loginRes.StatusCode == HttpStatusCode.OK);

        // 2. Post full hardware state with DO1 = ON (true)
        var fullHardwarePayload = new Dictionary<string, object?>
        {
            ["hostname"] = "maitech-rtu",
            ["uptime_seconds"] = 445000,
            ["free_heap"] = 1460000,
            ["digital_outputs"] = new[]
            {
                new { channel = 1, gpioPin = 21, state = true, mode = "toggle" },
                new { channel = 2, gpioPin = 35, state = false, mode = "toggle" }
            }
        };
        var post1 = await client.PostAsJsonAsync($"/api/v1/devices/{MaitechRtuGuid}/telemetry", fullHardwarePayload);
        Assert.Equal(HttpStatusCode.OK, post1.StatusCode);

        // 3. Post a partial MQTT status packet (only uptime & heap, NO digital_outputs)
        var partialMqttPayload = new Dictionary<string, object?>
        {
            ["uptime_seconds"] = 445003,
            ["free_heap"] = 1459800,
            ["min_free_heap"] = 681248,
            ["mqtt_connected"] = true
        };
        var post2 = await client.PostAsJsonAsync($"/api/v1/devices/{MaitechRtuGuid}/telemetry", partialMqttPayload);
        Assert.Equal(HttpStatusCode.OK, post2.StatusCode);

        // 4. Retrieve telemetry and ensure DO1 state = true is preserved
        var getRes = await client.GetAsync($"/api/v1/devices/{MaitechRtuGuid}/telemetry");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var served = await getRes.Content.ReadFromJsonAsync<JsonElement>();
        var data = served.GetProperty("data");

        // Verify uptime was updated
        Assert.Equal(445003, data.GetProperty("uptime_seconds").GetInt32());

        // Verify digital_outputs was NOT lost and DO1 is still true (ON)
        Assert.True(data.TryGetProperty("digital_outputs", out var doProp), "digital_outputs must be preserved across partial updates");
        var channels = doProp.EnumerateArray().ToArray();
        Assert.True(channels.Length >= 2);
        var ch1 = channels.First(c => c.GetProperty("channel").GetInt32() == 1);
        Assert.True(ch1.GetProperty("state").GetBoolean(), "DO1 state must remain true (ON)");
    }
}
