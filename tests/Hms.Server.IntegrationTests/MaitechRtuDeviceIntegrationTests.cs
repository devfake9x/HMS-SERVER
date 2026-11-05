using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hms.Server.Application.Commands;
using Hms.Server.Application.DeviceLink;
using Hms.Server.Application.Provisioning;
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
    public async Task ProtocolAdapter_PublishesUnderTheBoundTopicBase()
    {
        var deviceId = new DeviceId(MaitechRtuGuid);
        var bindings = new InMemoryDeviceMqttBindingStore();
        bindings.Upsert(DeviceMqttBinding.Create(deviceId, "hms_toan_lab", "gateway_870c", 11));
        var secrets = new RandomProvisioningSecretGenerator();
        secrets.Import(deviceId, 1, new byte[32]);
        var adapter = new HmsIdfCommandProtocolAdapter(bindings, secrets, new InMemoryDeviceLinkStateStore());

        var prepared = await adapter.PrepareAsync(Plan(deviceId, "system", "get_info"));

        Assert.True(prepared.CanPublish);
        Assert.Equal("hms_toan_lab/gateway_870c/cmd/system", prepared.Topic);
        Assert.Contains("\"key_ver\":11", System.Text.Encoding.UTF8.GetString(prepared.Payload.Span));
    }

    [Fact]
    public async Task ProtocolAdapter_NeverGuessesATopicForAnUnboundDevice()
    {
        var deviceId = new DeviceId(Guid.NewGuid());
        var secrets = new RandomProvisioningSecretGenerator();
        secrets.Import(deviceId, 1, new byte[32]);
        var adapter = new HmsIdfCommandProtocolAdapter(
            new InMemoryDeviceMqttBindingStore(), secrets, new InMemoryDeviceLinkStateStore());

        var prepared = await adapter.PrepareAsync(Plan(deviceId, "system", "ping"));

        Assert.False(prepared.CanPublish);
        Assert.Null(prepared.Topic);
        Assert.Equal("DEVICE_MQTT_BINDING_MISSING", prepared.FailureCode);
    }

    private static CommandExecutionPlan Plan(DeviceId deviceId, string type, string action)
    {
        var now = DateTimeOffset.UtcNow;
        var request = new DeviceCommandRequest(
            deviceId,
            new Hms.Server.Domain.Authorization.UserId(Guid.NewGuid()),
            type,
            action,
            new Dictionary<string, object?>(),
            Guid.NewGuid().ToString("N"),
            now);
        var definition = new CommandDefinition(type, action, "sys.info_view", CommandAvailability.Enabled, false, true, 120_000);
        return new CommandExecutionPlan(request, definition, 1, now.AddMinutes(2));
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
