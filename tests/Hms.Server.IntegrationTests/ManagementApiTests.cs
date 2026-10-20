using System.Collections.Frozen;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Hms.Server.Application.Management;
using Hms.Server.Application.Authorization;
using Hms.Server.Application.Commands;
using Hms.Server.Application.Provisioning;
using Hms.Server.Application.Licensing;
using Hms.Server.Infrastructure;
using Hms.Server.Infrastructure.Commands;
using Hms.Server.Infrastructure.Persistence;
using Hms.Server.Domain.Commands;
using Hms.Server.Api.Controllers;
using Hms.Server.Domain.Devices;
using Hms.Server.Domain.Organizations;
using Hms.Server.Domain.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Identity;

namespace Hms.Server.IntegrationTests;

public sealed class ManagementApiTests : IClassFixture<ManagementApiFactory>
{
    private readonly ManagementApiFactory _factory;

    public ManagementApiTests(ManagementApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Unauthenticated_management_requests_are_rejected()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/devices");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Organization_scope_filters_device_list_to_descendants()
    {
        using var client = _factory.CreateClientForUser(_factory.ScopedUserId,
            new Claim("org-scope", _factory.ParentOrganizationId.ToString()),
            new Claim("perm", "devices.manage"));

        var response = await client.GetAsync("/api/v1/devices");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        var ids = json.RootElement.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToArray();
        Assert.Contains(_factory.ChildDeviceId, ids);
        Assert.DoesNotContain(_factory.UnrelatedDeviceId, ids);
    }

    [Fact]
    public async Task Realtime_authorization_snapshot_is_scoped_nonrevoked_and_no_store()
    {
        using var client = _factory.CreateClientForUser(_factory.ScopedUserId,
            new Claim("org-scope", _factory.ParentOrganizationId.ToString()));
        var response = await client.GetAsync($"/api/v1/devices/{_factory.ChildDeviceId}/realtime-authorization");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(_factory.ChildDeviceId, body.RootElement.GetProperty("deviceId").GetGuid());
        Assert.True(body.RootElement.GetProperty("authorizationEpoch").GetInt64() >= 0);
        Assert.True(body.RootElement.GetProperty("policyVersion").GetInt64() >= 0);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(
            $"/api/v1/devices/{_factory.UnrelatedDeviceId}/realtime-authorization")).StatusCode);

        Guid revokedId;
        using (var scope = _factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IDeviceStore>();
            var revoked = Device.Create("revoked-realtime", new OrganizationId(_factory.ChildOrganizationId));
            revoked.Revoke();
            store.Add(revoked);
            revokedId = revoked.Id.Value;
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(
            $"/api/v1/devices/{revokedId}/realtime-authorization")).StatusCode);
        using var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(
            $"/api/v1/devices/{_factory.ChildDeviceId}/realtime-authorization")).StatusCode);
    }

    [Fact]
    public async Task Device_list_supports_server_side_text_filter_with_scope_enforcement()
    {
        using var client = _factory.CreateClientForUser(_factory.ScopedUserId,
            new Claim("org-scope", _factory.ParentOrganizationId.ToString()),
            new Claim("perm", "devices.manage"));

        var response = await client.GetAsync("/api/v1/devices?q=child-device");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ids = json.RootElement.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToArray();
        Assert.Equal([_factory.ChildDeviceId], ids);
    }

    [Fact]
    public async Task Device_update_renames_and_clears_nullable_classification_without_changing_owner()
    {
        Guid deviceId;
        using (var scope = _factory.Services.CreateScope())
        {
            var devices = scope.ServiceProvider.GetRequiredService<IDeviceStore>();
            var device = Device.Create("editable-device", new OrganizationId(_factory.ChildOrganizationId));
            device.SetClassification(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            devices.Add(device);
            deviceId = device.Id.Value;
        }

        using var client = _factory.CreateClientForUser(_factory.ScopedUserId,
            new Claim("org-scope", _factory.ParentOrganizationId.ToString()),
            new Claim("perm", "devices.manage"));
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/devices/{deviceId}")
        {
            Content = JsonContent.Create(new { name = "edited-device", projectId = (Guid?)null, packageId = (Guid?)null, areaId = (Guid?)null })
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf!.RequestToken);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("edited-device", body.RootElement.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("projectId").ValueKind);
        Assert.Equal(_factory.ChildOrganizationId, body.RootElement.GetProperty("ownerOrganizationId").GetGuid());
    }

    [Fact]
    public async Task Device_update_rejects_a_device_outside_the_callers_scope()
    {
        using var client = _factory.CreateClientForUser(_factory.ScopedUserId,
            new Claim("org-scope", _factory.ParentOrganizationId.ToString()),
            new Claim("perm", "devices.manage"));
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/devices/{_factory.UnrelatedDeviceId}")
        {
            Content = JsonContent.Create(new { name = "must-not-change", projectId = (Guid?)null, packageId = (Guid?)null, areaId = (Guid?)null })
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf!.RequestToken);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Persisted_resource_grants_are_used_for_scope_filtering_and_denies_win()
    {
        SeedResourceGrant(_factory.ResourceScopedUserId, _factory.ParentOrganizationId);
        SeedResourceGrant(_factory.ResourceScopedUserId, _factory.ChildOrganizationId, deny: true);

        using var client = _factory.CreateClientForUser(_factory.ResourceScopedUserId);
        var response = await client.GetAsync("/api/v1/devices");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ids = json.RootElement.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(_factory.ChildDeviceId, ids);
        Assert.DoesNotContain(_factory.UnrelatedDeviceId, ids);
    }

    [Fact]
    public async Task Me_permissions_and_catalog_are_no_store_and_include_trust_metadata()
    {
        using var client = _factory.CreateClientForUser(_factory.ScopedUserId,
            new Claim("org-scope", _factory.ParentOrganizationId.ToString()),
            new Claim("perm", "devices.manage"));

        var permissions = await client.GetAsync("/api/v1/me/permissions");
        var catalog = await client.GetAsync("/api/v1/me/command-catalog");

        Assert.Equal(HttpStatusCode.OK, permissions.StatusCode);
        Assert.Equal("no-store", permissions.Headers.CacheControl?.NoStore == true ? "no-store" : null);
        Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
        Assert.Equal("no-store", catalog.Headers.CacheControl?.NoStore == true ? "no-store" : null);
        using var catalogJson = JsonDocument.Parse(await catalog.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(catalogJson.RootElement.GetProperty("catalogVersion").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(catalogJson.RootElement.GetProperty("catalogHash").GetString()));
        Assert.True(catalogJson.RootElement.GetProperty("trusted").GetBoolean());
        var actions = catalogJson.RootElement.GetProperty("actions");
        Assert.Equal(94, actions.GetArrayLength());
        Assert.Contains(actions.EnumerateArray(), action =>
            action.GetProperty("type").GetString() == "system" &&
            action.GetProperty("action").GetString() == "get_info");
        Assert.All(actions.EnumerateArray(), action => Assert.False(action.GetProperty("available").GetBoolean()));
    }

    [Fact]
    public async Task Me_command_catalog_exposes_permission_policy_per_action()
    {
        using var client = _factory.CreateClientForUser(_factory.ScopedUserId);
        var metadata = PinnedCatalogMetadata();

        var response = await client.GetAsync("/api/v1/me/command-catalog");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(metadata.CatalogVersion, json.RootElement.GetProperty("catalogVersion").GetString());
        Assert.Equal(metadata.CatalogHash, json.RootElement.GetProperty("catalogHash").GetString());
        var actions = json.RootElement.GetProperty("actions").EnumerateArray().ToDictionary(x =>
            $"{x.GetProperty("type").GetString()}.{x.GetProperty("action").GetString()}");

        var reboot = actions["system.reboot"];
        Assert.Equal("Flash", reboot.GetProperty("class").GetString());
        Assert.Equal("sys.reboot", reboot.GetProperty("requiredPermission").GetString());
        Assert.Equal(JsonValueKind.Null, reboot.GetProperty("permissionByDomain").ValueKind);
        Assert.True(reboot.GetProperty("requiresConfirmation").GetBoolean());
        Assert.True(reboot.GetProperty("dryRunSupported").GetBoolean());
        Assert.Equal(60000, reboot.GetProperty("ttlMs").GetInt32());
        Assert.True(reboot.GetProperty("highRisk").GetBoolean());

        var info = actions["system.get_info"];
        Assert.Equal("sys.info_view", info.GetProperty("requiredPermission").GetString());
        Assert.Equal(120000, info.GetProperty("ttlMs").GetInt32());
        Assert.False(info.GetProperty("requiresConfirmation").GetBoolean());
        Assert.False(info.GetProperty("highRisk").GetBoolean());

        var configSet = actions["config.set"];
        var byDomain = configSet.GetProperty("permissionByDomain");
        Assert.Equal(JsonValueKind.Null, configSet.GetProperty("requiredPermission").ValueKind);
        Assert.Equal(JsonValueKind.Object, byDomain.ValueKind);
        Assert.Equal(6, byDomain.EnumerateObject().Count());
        Assert.All(byDomain.EnumerateObject(), x => Assert.Equal(JsonValueKind.String, x.Value.ValueKind));
        Assert.Equal("mqtt.config_edit", byDomain.GetProperty("mqtt").GetString());
        Assert.True(configSet.GetProperty("highRisk").GetBoolean());

        var configGet = actions["config.get"];
        Assert.Equal("wifi.ap_view", configGet.GetProperty("permissionByDomain").GetProperty("wifi_ap").GetString());
        Assert.False(configGet.GetProperty("highRisk").GetBoolean());

        Assert.Equal(15, actions.Values.Count(x => x.GetProperty("highRisk").GetBoolean()));
        Assert.All(actions.Values, x => Assert.Equal(
            new[]
            {
                "type", "action", "available", "class", "requiredPermission", "permissionByDomain",
                "requiresConfirmation", "dryRunSupported", "ttlMs", "highRisk"
            },
            x.EnumerateObject().Select(p => p.Name).ToArray()));
    }

    [Fact]
    public async Task Permission_management_requires_permission_policy()
    {
        using var client = _factory.CreateClientForUser(_factory.ScopedUserId);

        var response = await client.PostAsJsonAsync("/api/v1/permissions/features",
            new { userId = _factory.ScopedUserId, permission = "devices.read", deny = false });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Identity_role_mutation_advances_the_shared_policy_version()
    {
        using var scope = _factory.Services.CreateScope();
        var permissions = scope.ServiceProvider.GetRequiredService<IPermissionStore>();
        var before = permissions.PolicyVersion;

        using var client = _factory.CreateClientForUser(_factory.ScopedUserId,
            new Claim(ClaimTypes.Role, "master-admin"),
            new Claim("perm", "users.manage"));
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users/roles")
        {
            Content = JsonContent.Create(new { name = $"operator-{Guid.NewGuid():N}" })
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf!.RequestToken);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(permissions.PolicyVersion > before);
    }

    [Fact]
    public async Task Role_mutation_fails_closed_when_persisted_users_permission_is_denied()
    {
        var actorId = Guid.NewGuid();
        SeedFeatureGrant(actorId, "users.manage", deny: true);

        using var client = _factory.CreateClientForUser(actorId,
            new Claim("perm", "users.manage"));
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users/roles")
        {
            Content = JsonContent.Create(new { name = $"operator-{Guid.NewGuid():N}" })
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf!.RequestToken);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Persisted_users_permission_can_reach_management_endpoint_without_bootstrap_claim()
    {
        var actorId = Guid.NewGuid();
        SeedFeatureGrant(actorId, "users.manage");

        using var client = _factory.CreateClientForUser(actorId);
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users/roles")
        {
            Content = JsonContent.Create(new { name = $"operator-{Guid.NewGuid():N}" })
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf!.RequestToken);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Permission_grants_require_target_user_and_resource_scope()
    {
        var targetUserId = Guid.NewGuid();
        await _factory.SeedIdentityUserAsync(targetUserId, _factory.ChildOrganizationId);
        using var client = _factory.CreateClientForUser(_factory.ScopedUserId,
            new Claim("org-scope", _factory.ParentOrganizationId.ToString()),
            new Claim("perm", "permissions.manage"));

        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf");
        using var outsideRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/permissions/resource-scopes")
        {
            Content = JsonContent.Create(new
            {
                userId = targetUserId,
                organizationId = _factory.UnrelatedOrganizationId,
                deny = false
            })
        };
        outsideRequest.Headers.Add("X-CSRF-TOKEN", csrf!.RequestToken);
        var outside = await client.SendAsync(outsideRequest);

        using var insideRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/permissions/resource-scopes")
        {
            Content = JsonContent.Create(new
            {
                userId = targetUserId,
                organizationId = _factory.ChildOrganizationId,
                deny = false
            })
        };
        insideRequest.Headers.Add("X-CSRF-TOKEN", csrf.RequestToken);
        var inside = await client.SendAsync(insideRequest);

        Assert.Equal(HttpStatusCode.Forbidden, outside.StatusCode);
        Assert.Equal(HttpStatusCode.OK, inside.StatusCode);
    }

    [Fact]
    public async Task Permission_snapshot_returns_persisted_grants_for_in_scope_target()
    {
        await _factory.SeedIdentityUserAsync(_factory.ScopedUserId, _factory.ParentOrganizationId);
        await _factory.SeedIdentityUserAsync(_factory.ResourceScopedUserId, _factory.ChildOrganizationId);
        SeedFeatureGrant(_factory.ResourceScopedUserId, "device.read");

        using var client = _factory.CreateClientForUser(_factory.ScopedUserId,
            new Claim("org-scope", _factory.ParentOrganizationId.ToString()),
            new Claim("perm", "permissions.manage"));
        var response = await client.GetAsync($"/api/v1/permissions/{_factory.ResourceScopedUserId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var snapshot = await response.Content.ReadFromJsonAsync<PermissionGrantSnapshot>();
        Assert.NotNull(snapshot);
        Assert.Contains(snapshot!.FeatureGrants, x => x.Permission == "device.read" && !x.Deny);
        Assert.True(snapshot.PolicyVersion > 0);
    }

    [Fact]
    public async Task Device_revoke_rejects_a_device_outside_the_callers_scope()
    {
        using var client = _factory.CreateClientForUser(_factory.ScopedUserId,
            new Claim("org-scope", _factory.ParentOrganizationId.ToString()),
            new Claim("perm", "devices.manage"));

        var csrf = await client.GetAsync("/api/v1/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
        using var csrfJson = JsonDocument.Parse(await csrf.Content.ReadAsStringAsync());
        var requestToken = csrfJson.RootElement.GetProperty("requestToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(requestToken));

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/devices/{_factory.UnrelatedDeviceId}/revoke");
        request.Headers.Add("X-CSRF-TOKEN", requestToken);
        var response = await client.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{response.StatusCode}: {body}");
    }

    [Fact]
    public async Task Device_revoke_endpoint_changes_lifecycle_state()
    {
        using var client = _factory.CreateClientForUser(_factory.ScopedUserId,
            new Claim("org-scope", _factory.ParentOrganizationId.ToString()),
            new Claim("perm", "devices.manage"));
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/devices/{_factory.ChildDeviceId}/revoke");
        request.Headers.Add("X-CSRF-TOKEN", csrf!.RequestToken);

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, $"{response.StatusCode}: {body}");

        using var list = JsonDocument.Parse(await (await client.GetAsync("/api/v1/devices")).Content.ReadAsStringAsync());
        var device = list.RootElement.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == _factory.ChildDeviceId);
        Assert.Equal(nameof(DeviceLifecycleState.Revoked), device.GetProperty("state").GetString());
    }

    [Fact]
    public async Task Device_command_mutation_requires_csrf_token()
    {
        using var client = _factory.CreateClientForUser(_factory.ScopedUserId,
            new Claim("org-scope", _factory.ParentOrganizationId.ToString()),
            new Claim("perm", "devices.manage"));

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/devices/{_factory.ChildDeviceId}/commands",
            new { type = "provisional", action = "unknown", payload = new { } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task In_scope_device_command_reaches_the_catalog_lookup()
    {
        // A user of its own: another test writes a persisted Child deny for the shared scoped user.
        var userId = Guid.NewGuid();
        await _factory.SeedIdentityUserAsync(userId, _factory.ChildOrganizationId);
        using var client = _factory.CreateClientForUser(userId,
            new Claim("org-scope", _factory.ChildOrganizationId.ToString()),
            new Claim("perm", "devices.command"));
        var metadata = PinnedCatalogMetadata();
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/devices/{_factory.ChildDeviceId}/commands")
        {
            Content = JsonContent.Create(new
            {
                type = "provisional",
                action = "unknown",
                catalogVersion = metadata.CatalogVersion,
                catalogHash = metadata.CatalogHash
            })
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf!.RequestToken);

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {body}");
        using var json = JsonDocument.Parse(body);
        Assert.Equal("COMMAND_NOT_IN_CATALOG", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Device_command_requires_catalog_metadata_at_the_public_boundary()
    {
        // A user of its own: another test writes a persisted Child deny for the shared scoped user.
        var userId = Guid.NewGuid();
        await _factory.SeedIdentityUserAsync(userId, _factory.ChildOrganizationId);
        using var client = _factory.CreateClientForUser(userId,
            new Claim("org-scope", _factory.ChildOrganizationId.ToString()),
            new Claim("perm", "devices.command"));
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"/api/v1/devices/{_factory.ChildDeviceId}/commands")
        {
            Content = JsonContent.Create(new { type = "provisional", action = "unknown" })
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf!.RequestToken);

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("CATALOG_STALE", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Provisioning_bundle_download_is_post_only_and_csrf_protected()
    {
        var bundleId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IProvisioningBundleStore>();
            var unsigned = new ProvisioningBundle(
                bundleId,
                new DeviceId(_factory.ChildDeviceId),
                "child-device",
                "PRIMARY_ONLY",
                "broker.local",
                1883,
                "device-user",
                "device-password",
                Convert.ToBase64String(new byte[32]),
                0,
                DateTimeOffset.UtcNow.AddMinutes(5),
                ImportNonce: "nonce");
            store.Save(unsigned with { BundleHash = ProvisioningBundleIntegrity.ComputeHash(unsigned) });
        }

        using var client = _factory.CreateClientForUser(_factory.ScopedUserId,
            new Claim("org-scope", _factory.ParentOrganizationId.ToString()),
            new Claim("perm", "provisioning.manage"));
        var path = $"/api/v1/devices/{_factory.ChildDeviceId}/provisioning-bundles/{bundleId}/download";

        using var get = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, get.StatusCode);

        using var withoutCsrf = await client.PostAsync(path, content: null);
        Assert.Equal(HttpStatusCode.BadRequest, withoutCsrf.StatusCode);

        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("X-CSRF-TOKEN", csrf!.RequestToken);
        using var download = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("no-store", download.Headers.CacheControl?.NoStore == true ? "no-store" : null);
    }

    [Fact]
    public async Task Provisioning_a_revoked_device_is_rejected_at_the_http_boundary()
    {
        Guid revokedDeviceId;
        using (var scope = _factory.Services.CreateScope())
        {
            var devices = scope.ServiceProvider.GetRequiredService<IDeviceStore>();
            var revoked = Device.Create("revoked-provisioning", new OrganizationId(_factory.ChildOrganizationId));
            revoked.Revoke();
            devices.Add(revoked);
            revokedDeviceId = revoked.Id.Value;
        }

        using var client = _factory.CreateClientForUser(_factory.ScopedUserId,
            new Claim("org-scope", _factory.ParentOrganizationId.ToString()),
            new Claim("perm", "provisioning.manage"));
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/devices/{revokedDeviceId}/provisioning-bundles")
        {
            Content = JsonContent.Create(new { brokerHost = "broker.local", brokerPort = 1883 })
        };
        request.Headers.Add("X-CSRF-TOKEN", csrf!.RequestToken);

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("DEVICE_REVOKED", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Production_registration_fails_closed_without_secret_root()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddHmsInfrastructure(
                databaseConnectionString: "Host=localhost;Database=hms;Username=hms;Password=test",
                allowInMemoryDevelopment: false,
                secretRootConfigured: false));

        Assert.Equal("KMS_SECRET_ROOT_PROVIDER_REQUIRED", exception.Message);
    }

    [Fact]
    public void Production_registration_requires_a_non_ephemeral_data_protection_provider()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddHmsInfrastructure(
                databaseConnectionString: "Host=localhost;Database=hms;Username=hms;Password=test",
                allowInMemoryDevelopment: false,
                productionSecretRootPolicy: new FailClosedSecretRootPolicy(true)));

        Assert.Equal("KMS_DATA_PROTECTION_PROVIDER_REQUIRED", exception.Message);
    }

    [Fact]
    public async Task Device_permissions_matrix_returns_definitions_and_user_grants_with_datacake_parity()
    {
        using var client = _factory.CreateClientForUser(_factory.ScopedUserId,
            new Claim("org-scope", _factory.ParentOrganizationId.ToString()),
            new Claim("perm", "devices.manage"));

        var response = await client.GetAsync($"/api/v1/devices/{_factory.ChildDeviceId}/permissions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(_factory.ChildDeviceId, root.GetProperty("deviceId").GetGuid());
        
        var permissions = root.GetProperty("permissions").EnumerateArray().ToArray();
        Assert.Equal(4, permissions.Length);
        var permKeys = permissions.Select(p => p.GetProperty("key").GetString()).ToArray();
        Assert.Contains("view", permKeys);
        Assert.Contains("edit_basics", permKeys);
        Assert.Contains("edit_product", permKeys);
        Assert.Contains("record_measurements", permKeys);
    }

    [Fact]
    public async Task Device_permissions_mutation_requires_csrf_and_supports_optimistic_concurrency()
    {
        using var client = _factory.CreateClientForUser(_factory.ScopedUserId,
            new Claim("org-scope", _factory.ParentOrganizationId.ToString()),
            new Claim("perm", "devices.manage"),
            new Claim("perm", "permissions.manage"));

        var updatePayload = new
        {
            expectedPolicyVersion = 999999L,
            grants = new[]
            {
                new { userId = _factory.ScopedUserId, action = "edit_basics", granted = true }
            }
        };

        var withoutCsrf = await client.PutAsJsonAsync($"/api/v1/devices/{_factory.ChildDeviceId}/permissions", updatePayload);
        Assert.Equal(HttpStatusCode.BadRequest, withoutCsrf.StatusCode);

        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf");
        var conflictRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/devices/{_factory.ChildDeviceId}/permissions")
        {
            Content = JsonContent.Create(updatePayload)
        };
        conflictRequest.Headers.Add("X-CSRF-TOKEN", csrf!.RequestToken);
        var conflictResponse = await client.SendAsync(conflictRequest);
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);

        var getResponse = await client.GetAsync($"/api/v1/devices/{_factory.ChildDeviceId}/permissions");
        using var getJson = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        var currentVersion = getJson.RootElement.GetProperty("policyVersion").GetInt64();

        var validPayload = new
        {
            expectedPolicyVersion = currentVersion,
            grants = new[]
            {
                new { userId = _factory.ScopedUserId, action = "edit_basics", granted = true }
            }
        };
        var validRequest = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/devices/{_factory.ChildDeviceId}/permissions")
        {
            Content = JsonContent.Create(validPayload)
        };
        validRequest.Headers.Add("X-CSRF-TOKEN", csrf.RequestToken);
        var validResponse = await client.SendAsync(validRequest);
        Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);
    }

    [Fact]
    public async Task Persisted_feature_regrant_after_deny_restores_management_access()
    {
        var actorId = Guid.NewGuid();
        SeedFeatureGrant(actorId, "users.manage");
        SeedFeatureGrant(actorId, "users.manage", deny: true);
        SeedFeatureGrant(actorId, "users.manage");
        using var client = _factory.CreateClientForUser(actorId);

        var response = await PostAsync(client, "/api/v1/users/roles", new { name = $"operator-{Guid.NewGuid():N}" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Master_admin_with_persisted_users_deny_still_creates_roles()
    {
        var actorId = Guid.NewGuid();
        SeedLegacyFeatureDeny(actorId, "users.manage");
        using var client = _factory.CreateClientForUser(actorId,
            new Claim(ClaimTypes.Role, RoleAuthorizationPolicy.MasterAdminRole));

        var response = await PostAsync(client, "/api/v1/users/roles", new { name = $"operator-{Guid.NewGuid():N}" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Master_admin_device_list_ignores_persisted_resource_deny()
    {
        var actorId = Guid.NewGuid();
        SeedLegacyResourceDeny(actorId, _factory.ChildOrganizationId);
        using var client = _factory.CreateClientForUser(actorId,
            new Claim(ClaimTypes.Role, RoleAuthorizationPolicy.MasterAdminRole));

        var response = await client.GetAsync("/api/v1/devices");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ids = json.RootElement.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToArray();
        Assert.Contains(_factory.ChildDeviceId, ids);
        Assert.Contains(_factory.UnrelatedDeviceId, ids);
    }

    [Fact]
    public async Task Wildcard_allow_lifts_a_block_only_for_master_admin_over_http()
    {
        var targetId = Guid.NewGuid();
        await _factory.SeedIdentityUserAsync(targetId, _factory.ChildOrganizationId);
        using var master = _factory.CreateClientForUser(Guid.NewGuid(),
            new Claim(ClaimTypes.Role, RoleAuthorizationPolicy.MasterAdminRole));
        using var admin = _factory.CreateClientForUser(Guid.NewGuid(),
            new Claim("org-scope", _factory.ParentOrganizationId.ToString()),
            new Claim("perm", "permissions.manage"));
        var lift = new { userId = targetId, permission = "*", deny = false };

        await AssertCodeAsync(await PostAsync(admin, "/api/v1/permissions/features", lift),
            HttpStatusCode.BadRequest, "WILDCARD_GRANT_FORBIDDEN");
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(master, "/api/v1/permissions/features",
            new { userId = targetId, permission = "*", deny = true })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(master, "/api/v1/permissions/features", lift)).StatusCode);
    }

    [Fact]
    public async Task Feature_grant_rejects_wildcard_deny_from_non_master()
    {
        var targetId = Guid.NewGuid();
        await _factory.SeedIdentityUserAsync(targetId, _factory.ChildOrganizationId);
        using var admin = _factory.CreateClientForUser(Guid.NewGuid(),
            new Claim("org-scope", _factory.ParentOrganizationId.ToString()),
            new Claim("perm", "permissions.manage"));
        using var master = _factory.CreateClientForUser(Guid.NewGuid(),
            new Claim(ClaimTypes.Role, RoleAuthorizationPolicy.MasterAdminRole));
        var body = new { userId = targetId, permission = "*", deny = true };

        await AssertCodeAsync(await PostAsync(admin, "/api/v1/permissions/features", body),
            HttpStatusCode.BadRequest, "WILDCARD_GRANT_FORBIDDEN");
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(master, "/api/v1/permissions/features", body)).StatusCode);
    }

    [Fact]
    public async Task Feature_and_resource_grants_reject_self_grant_for_non_master()
    {
        var actorId = Guid.NewGuid();
        await _factory.SeedIdentityUserAsync(actorId, _factory.ChildOrganizationId);
        using var client = _factory.CreateClientForUser(actorId,
            new Claim("org-scope", _factory.ParentOrganizationId.ToString()),
            new Claim("perm", "permissions.manage"),
            new Claim("perm", "devices.manage"));

        await AssertCodeAsync(
            await PostAsync(client, "/api/v1/permissions/features",
                new { userId = actorId, permission = "devices.manage", deny = false }),
            HttpStatusCode.Forbidden, "SELF_GRANT_FORBIDDEN");
        await AssertCodeAsync(
            await PostAsync(client, "/api/v1/permissions/resource-scopes",
                new { userId = actorId, organizationId = _factory.ChildOrganizationId, deny = false }),
            HttpStatusCode.Forbidden, "SELF_GRANT_FORBIDDEN");
    }

    [Fact]
    public async Task Feature_grant_rejects_permission_the_grantor_does_not_hold()
    {
        var targetId = Guid.NewGuid();
        await _factory.SeedIdentityUserAsync(targetId, _factory.ChildOrganizationId);
        using var client = _factory.CreateClientForUser(Guid.NewGuid(),
            new Claim("org-scope", _factory.ParentOrganizationId.ToString()),
            new Claim("perm", "permissions.manage"));

        await AssertCodeAsync(
            await PostAsync(client, "/api/v1/permissions/features",
                new { userId = targetId, permission = "users.manage", deny = false }),
            HttpStatusCode.Forbidden, "GRANT_EXCEEDS_OWN_PERMISSIONS");
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, "/api/v1/permissions/features",
            new { userId = targetId, permission = "users.manage", deny = true })).StatusCode);
    }

    [Fact]
    public async Task Wildcard_feature_claim_grants_no_management_access()
    {
        using var client = _factory.CreateClientForUser(Guid.NewGuid(),
            new Claim("perm", "*"),
            new Claim("org-scope", _factory.ParentOrganizationId.ToString()));

        var users = await client.GetAsync("/api/v1/users");
        // The owner organization is in scope, so a 403 can only come from the missing devices.manage.
        var create = await PostAsync(client, "/api/v1/devices", new
        {
            name = $"wildcard-{Guid.NewGuid():N}",
            ownerOrganizationId = new { value = _factory.ParentOrganizationId }
        });

        Assert.Equal(HttpStatusCode.Forbidden, users.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task Feature_grant_rejected_when_target_scope_exceeds_grantor_scope_over_http()
    {
        var targetId = Guid.NewGuid();
        await _factory.SeedIdentityUserAsync(targetId, _factory.ChildOrganizationId);
        SeedResourceGrant(targetId, _factory.ParentOrganizationId);
        using var client = _factory.CreateClientForUser(Guid.NewGuid(),
            new Claim("org-scope", _factory.ChildOrganizationId.ToString()),
            new Claim("perm", "permissions.manage"),
            new Claim("perm", "devices.manage"));

        await AssertCodeAsync(
            await PostAsync(client, "/api/v1/permissions/features",
                new { userId = targetId, permission = "devices.manage", deny = false }),
            HttpStatusCode.Forbidden, "GRANT_EXCEEDS_OWN_PERMISSIONS");
    }

    [Fact]
    public async Task Non_master_cannot_reverse_master_admin_deny_over_http()
    {
        var targetId = Guid.NewGuid();
        await _factory.SeedIdentityUserAsync(targetId, _factory.ChildOrganizationId);
        SeedFeatureGrant(targetId, "devices.manage", deny: true);
        using var client = _factory.CreateClientForUser(Guid.NewGuid(),
            new Claim("org-scope", _factory.ParentOrganizationId.ToString()),
            new Claim("perm", "permissions.manage"),
            new Claim("perm", "devices.manage"));

        await AssertCodeAsync(
            await PostAsync(client, "/api/v1/permissions/features",
                new { userId = targetId, permission = "devices.manage", deny = false }),
            HttpStatusCode.Forbidden, "GRANT_OVERRIDES_MASTER_DECISION");
    }

    [Fact]
    public async Task Deny_on_master_admin_target_is_rejected_over_http()
    {
        var targetId = Guid.NewGuid();
        await _factory.SeedIdentityUserAsync(targetId, _factory.ChildOrganizationId);
        await _factory.AddIdentityRoleAsync(targetId, RoleAuthorizationPolicy.MasterAdminRole);
        using var client = _factory.CreateClientForUser(Guid.NewGuid(),
            new Claim(ClaimTypes.Role, RoleAuthorizationPolicy.MasterAdminRole));

        await AssertCodeAsync(
            await PostAsync(client, "/api/v1/permissions/features",
                new { userId = targetId, permission = "users.manage", deny = true }),
            HttpStatusCode.BadRequest, "GRANT_TARGET_IS_MASTER_ADMIN");
    }

    [Fact]
    public async Task User_admin_cannot_assign_master_admin_through_a_lookalike_role_name()
    {
        await EnsureMasterAdminRoleAsync();
        var attackerId = Guid.NewGuid();
        await _factory.SeedIdentityUserAsync(attackerId, _factory.ChildOrganizationId);
        SeedFeatureGrant(attackerId, "users.manage");
        SeedResourceGrant(attackerId, _factory.ChildOrganizationId);
        using var client = _factory.CreateClientForUser(attackerId);

        var response = await PutAsync(client,
            $"/api/v1/users/{attackerId:D}/roles/{Uri.EscapeDataString(LookalikeMasterAdmin)}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(await _factory.IsInRoleAsync(attackerId, RoleAuthorizationPolicy.MasterAdminRole));
    }

    [Fact]
    public async Task User_admin_cannot_create_a_lookalike_master_admin_role()
    {
        await EnsureMasterAdminRoleAsync();
        var actorId = Guid.NewGuid();
        await _factory.SeedIdentityUserAsync(actorId, _factory.ChildOrganizationId);
        SeedFeatureGrant(actorId, "users.manage");
        using var client = _factory.CreateClientForUser(actorId);

        var response = await PostAsync(client, "/api/v1/users/roles", new { name = LookalikeMasterAdmin });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Identity_role_management_rejects_a_lookalike_master_admin_name_for_non_master()
    {
        await EnsureMasterAdminRoleAsync();
        var targetId = Guid.NewGuid();
        await _factory.SeedIdentityUserAsync(targetId, _factory.ChildOrganizationId);
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserManagement>();
        var nonMaster = new UserManagementActor(false);

        Assert.Equal(["RESERVED_ROLE_FORBIDDEN"],
            await users.AssignRoleAsync(targetId.ToString("D"), LookalikeMasterAdmin, nonMaster));
        Assert.Equal(["RESERVED_ROLE_FORBIDDEN"], await users.CreateRoleAsync(LookalikeMasterAdmin, nonMaster));
        Assert.False(await _factory.IsInRoleAsync(targetId, RoleAuthorizationPolicy.MasterAdminRole));
    }

    [Fact]
    public async Task Resource_grant_rejected_when_its_subtree_leaves_the_grantor_scope_over_http()
    {
        // Scope on Parent with Child carved out, all from persisted grants.
        var adminId = Guid.NewGuid();
        await _factory.SeedIdentityUserAsync(adminId, _factory.ParentOrganizationId);
        SeedFeatureGrant(adminId, "permissions.manage");
        SeedFeatureGrant(adminId, "devices.manage");
        SeedResourceGrant(adminId, _factory.ParentOrganizationId);
        SeedResourceGrant(adminId, _factory.ChildOrganizationId, deny: true);
        var targetId = Guid.NewGuid();
        await _factory.SeedIdentityUserAsync(targetId, _factory.ParentOrganizationId);
        using var client = _factory.CreateClientForUser(adminId);

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, "/api/v1/permissions/features",
            new { userId = targetId, permission = "devices.manage", deny = false })).StatusCode);
        // A Parent grant would also open Child, which the admin was carved out of.
        await AssertCodeAsync(
            await PostAsync(client, "/api/v1/permissions/resource-scopes",
                new { userId = targetId, organizationId = _factory.ParentOrganizationId, deny = false }),
            HttpStatusCode.Forbidden, "GRANT_EXCEEDS_OWN_PERMISSIONS");
    }

    [Fact]
    public async Task Appointed_admin_cannot_act_on_its_appointer_over_http()
    {
        var appointerId = Guid.NewGuid();
        await _factory.SeedIdentityUserAsync(appointerId, _factory.ChildOrganizationId);
        SeedFeatureGrant(appointerId, "permissions.manage");
        SeedResourceGrant(appointerId, _factory.ChildOrganizationId);
        var appointedId = Guid.NewGuid();
        await _factory.SeedIdentityUserAsync(appointedId, _factory.ChildOrganizationId);
        SeedResourceGrant(appointedId, _factory.ChildOrganizationId);
        using var appointer = _factory.CreateClientForUser(appointerId);
        using var appointed = _factory.CreateClientForUser(appointedId);

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(appointer, "/api/v1/permissions/features",
            new { userId = appointedId, permission = "permissions.manage", deny = false })).StatusCode);

        await AssertCodeAsync(
            await PostAsync(appointed, "/api/v1/permissions/features",
                new { userId = appointerId, permission = "permissions.manage", deny = true }),
            HttpStatusCode.Forbidden, "GRANT_TO_DELEGATOR_FORBIDDEN");
    }

    [Fact]
    public async Task Out_of_scope_device_command_reads_like_a_missing_device()
    {
        using var client = _factory.CreateClientForUser(Guid.NewGuid(),
            new Claim("org-scope", _factory.ChildOrganizationId.ToString()),
            new Claim("perm", "devices.command"));
        var body = new { type = "adc", action = "get_status", catalogVersion = "stale", catalogHash = "stale" };

        var foreign = await PostAsync(client, $"/api/v1/devices/{_factory.UnrelatedDeviceId}/commands", body);
        var missing = await PostAsync(client, $"/api/v1/devices/{Guid.NewGuid()}/commands", body);

        Assert.Equal("DEVICE_NOT_FOUND", await CodeOfAsync(foreign));
        Assert.Equal("DEVICE_NOT_FOUND", await CodeOfAsync(missing));
    }

    [Fact]
    public async Task Device_command_passes_confirm_through_to_the_handler()
    {
        // A host whose catalog enables system.reboot, one of the rows that needs a confirm.
        var catalog = new InMemoryCommandCatalog(new PinnedCommandCatalog().List().Select(row =>
            row is { Type: "system", Action: "reboot" } ? row with { Availability = CommandAvailability.Enabled } : row));
        using var host = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<ICommandCatalog>(catalog)));
        Guid deviceId;
        using (var scope = host.Services.CreateScope())
        {
            var organization = Organization.Create("Confirm", null);
            scope.ServiceProvider.GetRequiredService<IOrganizationStore>().Add(organization);
            var device = Device.Create("confirm-device", organization.Id);
            device.Activate();
            scope.ServiceProvider.GetRequiredService<IDeviceStore>().Add(device);
            deviceId = device.Id.Value;
        }

        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test",
            TestAuthHandler.Encode(Guid.NewGuid(), [new Claim(ClaimTypes.Role, RoleAuthorizationPolicy.MasterAdminRole)]));
        object Body(string? confirm) => new
        {
            type = "system",
            action = "reboot",
            catalogVersion = InMemoryCommandCatalog.ProvisionalCatalogVersion,
            catalogHash = InMemoryCommandCatalog.ProvisionalCatalogHash,
            confirm
        };

        var withoutConfirm = await PostAsync(client, $"/api/v1/devices/{deviceId}/commands", Body(null));
        var withConfirm = await PostAsync(client, $"/api/v1/devices/{deviceId}/commands", Body("confirm-device"));

        Assert.Equal("CONFIRM_REQUIRED", await CodeOfAsync(withoutConfirm));
        Assert.NotEqual("CONFIRM_REQUIRED", await CodeOfAsync(withConfirm));
    }

    // "master-admin" with U+017F (long s) for the s: Identity normalizes it onto the real role.
    private static readonly string LookalikeMasterAdmin = $"ma{(char)0x017F}ter-admin";

    // The real role must exist for a lookalike name to reach it.
    private async Task EnsureMasterAdminRoleAsync()
    {
        var holderId = Guid.NewGuid();
        await _factory.SeedIdentityUserAsync(holderId, _factory.ChildOrganizationId);
        await _factory.AddIdentityRoleAsync(holderId, RoleAuthorizationPolicy.MasterAdminRole);
    }

    private CommandCatalogMetadata PinnedCatalogMetadata() =>
        ((ICommandCatalogMetadata)_factory.Services.GetRequiredService<ICommandCatalog>()).Metadata;

    // Grants seeded through the real handlers, written by a master-admin.
    private static readonly AccessActor MasterSeeder = new(
        new UserId(Guid.NewGuid()), true, FrozenSet<OrganizationId>.Empty, FrozenSet<string>.Empty);

    private void SeedFeatureGrant(Guid userId, string permission, bool deny = false)
    {
        using var scope = _factory.Services.CreateScope();
        var outcome = scope.ServiceProvider.GetRequiredService<GrantFeaturePermissionHandler>()
            .Handle(MasterSeeder, new UserId(userId), permission, deny);
        Assert.Equal("OK", outcome.Code);
    }

    private void SeedResourceGrant(Guid userId, Guid organizationId, bool deny = false)
    {
        using var scope = _factory.Services.CreateScope();
        var outcome = scope.ServiceProvider.GetRequiredService<GrantResourceScopeHandler>()
            .Handle(MasterSeeder, new UserId(userId), new OrganizationId(organizationId), deny);
        Assert.Equal("OK", outcome.Code);
    }

    // Deny rows written straight to the store, like data from before the grant
    // handlers rejected denies that target a master-admin.
    private void SeedLegacyFeatureDeny(Guid userId, string permission)
    {
        using var scope = _factory.Services.CreateScope();
        var permissions = scope.ServiceProvider.GetRequiredService<IPermissionStore>();
        permissions.GrantFeature(new FeaturePermissionGrant(new UserId(userId), permission, true, permissions.Advance()));
    }

    private void SeedLegacyResourceDeny(Guid userId, Guid organizationId)
    {
        using var scope = _factory.Services.CreateScope();
        var permissions = scope.ServiceProvider.GetRequiredService<IPermissionStore>();
        permissions.GrantResource(new ResourceScopeGrant(
            new UserId(userId), new OrganizationId(organizationId), true, permissions.Advance()));
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body)
    {
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf!.RequestToken);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PutAsync(HttpClient client, string path)
    {
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Put, path);
        request.Headers.Add("X-CSRF-TOKEN", csrf!.RequestToken);
        return await client.SendAsync(request);
    }

    private static async Task<string?> CodeOfAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {body}");
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("code").GetString();
    }

    private static async Task AssertCodeAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"{response.StatusCode}: {body}");
        using var json = JsonDocument.Parse(body);
        Assert.Equal(code, json.RootElement.GetProperty("code").GetString());
    }
}

public sealed record CsrfResponse(string RequestToken);

public sealed class ManagementApiFactory : WebApplicationFactory<Program>
{
    public readonly Guid ScopedUserId = Guid.NewGuid();
    public readonly Guid ResourceScopedUserId = Guid.NewGuid();
    private Guid _parentOrganizationId;
    private Guid _childOrganizationId;
    private Guid _unrelatedOrganizationId;
    private Guid _childDeviceId;
    private Guid _unrelatedDeviceId;

    public Guid ParentOrganizationId { get { EnsureSeeded(); return _parentOrganizationId; } }
    public Guid ChildOrganizationId { get { EnsureSeeded(); return _childOrganizationId; } }
    public Guid UnrelatedOrganizationId { get { EnsureSeeded(); return _unrelatedOrganizationId; } }
    public Guid ChildDeviceId { get { EnsureSeeded(); return _childDeviceId; } }
    public Guid UnrelatedDeviceId { get { EnsureSeeded(); return _unrelatedDeviceId; } }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.Scheme;
                options.DefaultChallengeScheme = TestAuthHandler.Scheme;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.Scheme, _ => { });
        });

        builder.UseSetting("Kms:SecretRootConfigured", "true");
        builder.UseSetting("Development:AutoSeed", "false");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }

    public HttpClient CreateClientForUser(Guid userId, params Claim[] claims)
    {
        EnsureSeeded();
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        var encoded = TestAuthHandler.Encode(userId, claims);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test", encoded);
        return client;
    }

    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        EnsureSeeded();
    }

    private bool _seeded;
    private void EnsureSeeded()
    {
        if (_seeded) return;
        using var scope = Services.CreateScope();
        var organizations = scope.ServiceProvider.GetRequiredService<IOrganizationStore>();
        var devices = scope.ServiceProvider.GetRequiredService<IDeviceStore>();
        var parent = Organization.Create("Parent", null);
        organizations.Add(parent);
        var child = Organization.Create("Child", new OrganizationId(parent.Id.Value));
        organizations.Add(child);
        var unrelated = Organization.Create("Unrelated", null);
        organizations.Add(unrelated);
        _parentOrganizationId = parent.Id.Value;
        _childOrganizationId = child.Id.Value;
        _unrelatedOrganizationId = unrelated.Id.Value;
        var childDevice = Device.Create("child-device", child.Id);
        devices.Add(childDevice);
        var unrelatedDevice = Device.Create("unrelated-device", unrelated.Id);
        devices.Add(unrelatedDevice);
        _childDeviceId = childDevice.Id.Value;
        _unrelatedDeviceId = unrelatedDevice.Id.Value;
        _seeded = true;
    }

    public async Task SeedIdentityUserAsync(Guid userId, Guid organizationId)
    {
        EnsureSeeded();
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await users.FindByIdAsync(userId.ToString("D")) is not null)
            return;

        var user = new ApplicationUser
        {
            Id = userId,
            UserName = $"target-{userId:N}@example.test",
            Email = $"target-{userId:N}@example.test",
            EmailConfirmed = true,
            OrganizationId = organizationId
        };
        var result = await users.CreateAsync(user, "Strong-Test-Password-123!");
        Assert.True(result.Succeeded, string.Join(",", result.Errors.Select(x => x.Code)));
    }

    public async Task AddIdentityRoleAsync(Guid userId, string role)
    {
        using var scope = Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        if (!await roles.RoleExistsAsync(role))
            Assert.True((await roles.CreateAsync(new ApplicationRole { Id = Guid.NewGuid(), Name = role })).Succeeded);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(userId.ToString("D"));
        Assert.NotNull(user);
        var result = await users.AddToRoleAsync(user!, role);
        Assert.True(result.Succeeded, string.Join(",", result.Errors.Select(x => x.Code)));
    }

    public async Task<bool> IsInRoleAsync(Guid userId, string role)
    {
        using var scope = Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(userId.ToString("D"));
        return user is not null && await users.IsInRoleAsync(user, role);
    }
}

public sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public new const string Scheme = "Test";

    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder)
        : base(options, logger, encoder) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var value) ||
            !value.ToString().StartsWith("Test ", StringComparison.Ordinal))
            return Task.FromResult(AuthenticateResult.NoResult());

        var parts = Decode(value.ToString()[5..]);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, parts.UserId.ToString()) };
        claims.AddRange(parts.Claims.Select(x => new Claim(x.Type, x.Value)));
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme)), Scheme)));
    }

    public static string Encode(Guid userId, IEnumerable<Claim> claims) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new TestIdentity(userId, claims.Select(x => new ClaimDto(x.Type, x.Value)).ToArray())));

    private static TestIdentity Decode(string encoded) =>
        JsonSerializer.Deserialize<TestIdentity>(Convert.FromBase64String(encoded))!;

    private sealed record TestIdentity(Guid UserId, ClaimDto[] Claims);
    private sealed record ClaimDto(string Type, string Value);
}
