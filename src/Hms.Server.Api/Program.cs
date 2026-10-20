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
builder.Services.AddHmsInfrastructure(
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
app.MapGet("/health", () => Results.Ok(new { status = "ok", mqtt = "deferred" }));

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
}

app.Run();

public partial class Program;
