using Hms.Server.Infrastructure;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Server.IIS;

var builder = WebApplication.CreateBuilder(args);

// Keep all JSON/API request bodies bounded; individual endpoints may use a
// smaller limit when their contract does not require large payloads.
const long MaxRequestBodyBytes = 1 * 1024 * 1024;
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = MaxRequestBodyBytes);
builder.Services.Configure<IISServerOptions>(options => options.MaxRequestBodySize = MaxRequestBodyBytes);

if (builder.Environment.IsDevelopment())
    builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();

// Keep API diagnostics on stdout; Windows EventLog is not available in every
// deployment environment and must not turn a normal request failure into one.
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options => options.SingleLine = true);

// Add services to the container.

// Add the MVC view-feature services used by ValidateAntiForgeryToken filters.
// The application has no server-rendered views, but AddControllers alone does
// not register the filter factory required by the cookie/CSRF API boundary.
builder.Services.AddControllersWithViews()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSignalR();
builder.Services.AddSingleton<Hms.Server.Api.Hubs.SignalRSubscriptionRegistry>();
builder.Services.AddHostedService<Hms.Server.Api.Hubs.SignalRDeviceEventDeliveryService>();
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
    Hms.Server.Infrastructure.Authorization.FeaturePermissionAuthorizationHandler>();
var mqttSection = builder.Configuration.GetSection(Hms.Server.Infrastructure.Mqtt.MqttLinkOptions.SectionName);
builder.Services.Configure<Hms.Server.Infrastructure.Mqtt.MqttLinkOptions>(mqttSection);
builder.Services.AddHmsInfrastructure(
    enableLiveMqtt: mqttSection.GetValue<bool>("Enabled"),
    databaseConnectionString: builder.Configuration.GetConnectionString("Postgres"),
    allowInMemoryDevelopment: builder.Environment.IsDevelopment());
var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapControllers();
app.MapHub<Hms.Server.Api.Hubs.DeviceEventsHub>("/hubs/device-events");
app.MapGet("/health", (Hms.Server.Application.DeviceLink.IDeviceLinkTransportStatus link) => Results.Ok(new
{
    status = "ok",
    mqtt = !link.Current.Enabled ? "deferred" : link.Current.Connected ? "connected" : "disconnected"
}));

if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Development:AutoSeed", true))
{
    using var scope = app.Services.CreateScope();
    var userManager = scope.ServiceProvider.GetService<Microsoft.AspNetCore.Identity.UserManager<Hms.Server.Infrastructure.Persistence.ApplicationUser>>();
    var roleManager = scope.ServiceProvider.GetService<Microsoft.AspNetCore.Identity.RoleManager<Hms.Server.Infrastructure.Persistence.ApplicationRole>>();
    var dbContext = scope.ServiceProvider.GetService<Hms.Server.Infrastructure.Persistence.ApplicationDbContext>();
    if (userManager != null && roleManager != null && dbContext != null)
    {
        await dbContext.Database.EnsureCreatedAsync();
        if (!await roleManager.RoleExistsAsync(Hms.Server.Application.Authorization.RoleAuthorizationPolicy.MasterAdminRole))
        {
            await roleManager.CreateAsync(new Hms.Server.Infrastructure.Persistence.ApplicationRole { Name = Hms.Server.Application.Authorization.RoleAuthorizationPolicy.MasterAdminRole });
        }
        var defaultAdmin = await userManager.FindByEmailAsync("admin@hms.local");
        if (defaultAdmin == null)
        {
            defaultAdmin = new Hms.Server.Infrastructure.Persistence.ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "admin@hms.local",
                Email = "admin@hms.local",
                EmailConfirmed = true
            };
            var createResult = await userManager.CreateAsync(defaultAdmin, "AdminPassword123!");
            if (createResult.Succeeded)
            {
                await userManager.AddToRoleAsync(defaultAdmin, Hms.Server.Application.Authorization.RoleAuthorizationPolicy.MasterAdminRole);
            }
        }
    }

    var orgStore = scope.ServiceProvider.GetService<Hms.Server.Application.Management.IOrganizationStore>();
    var devStore = scope.ServiceProvider.GetService<Hms.Server.Application.Management.IDeviceStore>();
    if (orgStore != null && devStore != null)
    {
        Hms.Server.Domain.Organizations.Organization? targetOrg = null;
        if (!orgStore.List().Any())
        {
            var rootOrg = Hms.Server.Domain.Organizations.Organization.Create("HMS Industrial Systems", null);
            orgStore.Add(rootOrg);
            var subOrg = Hms.Server.Domain.Organizations.Organization.Create("Station Alpha - Solar Array", rootOrg.Id);
            orgStore.Add(subOrg);
            targetOrg = subOrg;
            var dev1 = Hms.Server.Domain.Devices.Device.Create("RTU-ESP32-S3-001", subOrg.Id);
            devStore.Add(dev1);
            var dev2 = Hms.Server.Domain.Devices.Device.Create("RTU-ESP32-S3-002", subOrg.Id);
            devStore.Add(dev2);
        }
        else
        {
            targetOrg = orgStore.List().FirstOrDefault();
        }

        var rtuId = new Guid("870c870c-870c-870c-870c-870c870c870c");
        if (targetOrg != null && !devStore.List().Any(d => d.Id.Value == rtuId || d.Name == "MAITECH-RTU-870C"))
        {
            var maitechRtu = Hms.Server.Domain.Devices.Device.Rehydrate(
                rtuId,
                "MAITECH-RTU-870C",
                targetOrg.Id,
                null, null, null,
                Hms.Server.Domain.Devices.DeviceLifecycleState.Active,
                1);
            devStore.Add(maitechRtu);
        }
    }

    // Development: bind lab devices to their topic base, and register a command key already on
    // the device when one is configured through user secrets or the environment.
    var mqttSeed = app.Configuration.GetSection(Hms.Server.Infrastructure.Mqtt.MqttLinkOptions.SectionName)
        .Get<Hms.Server.Infrastructure.Mqtt.MqttLinkOptions>();
    foreach (var seed in mqttSeed?.Devices ?? [])
    {
        var deviceId = new Hms.Server.Domain.Devices.DeviceId(seed.DeviceId);
        try
        {
            scope.ServiceProvider.GetRequiredService<Hms.Server.Application.DeviceLink.ConfigureDeviceMqttBindingHandler>()
                .Handle(deviceId, seed.TopicPrefix, seed.MqttDeviceId, seed.KeyVersion);
            if (!string.IsNullOrWhiteSpace(seed.CommandKeyHex))
                scope.ServiceProvider.GetRequiredService<Hms.Server.Application.DeviceLink.ImportDeviceCommandKeyHandler>()
                    .Handle(deviceId, seed.CommandKeyHex);
            // Left out = no output channel may be driven (fail closed).
            if (seed.AllowedOutputChannels is { } channels)
                scope.ServiceProvider.GetRequiredService<Hms.Server.Application.DeviceLink.SetDeviceOutputChannelsHandler>()
                    .Handle(deviceId, channels);
            app.Logger.LogInformation(
                "Bound device {DeviceId} to MQTT base {Prefix}/{MqttDeviceId} (command key {KeyState}, output channels [{Channels}])",
                seed.DeviceId, seed.TopicPrefix, seed.MqttDeviceId,
                string.IsNullOrWhiteSpace(seed.CommandKeyHex) ? "not configured" : "registered",
                string.Join(',', seed.AllowedOutputChannels ?? []));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Codes only: never log the seed values, which may include the key.
            app.Logger.LogWarning("MQTT seed for device {DeviceId} skipped: {Code}", seed.DeviceId, ex.Message.Split(' ')[0]);
        }
    }
}

// Commands still "Published" from a previous run can no longer get their reply; record their
// outcome as unknown so nobody reads them as pending or successful (spec R4).
using (var startupScope = app.Services.CreateScope())
{
    var unfinished = startupScope.ServiceProvider.GetRequiredService<Hms.Server.Application.Commands.ICommandAuditStore>()
        .FailUnfinished("OUTCOME_UNKNOWN_AFTER_RESTART");
    if (unfinished > 0)
        app.Logger.LogWarning("{Count} device command(s) a previous run left unfinished (published, or a reboot still being watched) marked outcome unknown", unfinished);
}

app.Run();

public partial class Program;
