using Hms.Server.Application.Commands;
using Hms.Server.Application.Management;
using Hms.Server.Application.Provisioning;
using Hms.Server.Application.Licensing;
using Hms.Server.Application.Realtime;
using Hms.Server.Application.Authorization;
using Hms.Server.Infrastructure.Identity;
using Hms.Server.Domain.Devices;
using Hms.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hms.Server.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddHmsInfrastructure(
        this IServiceCollection services,
        bool enableLiveMqtt = false,
        string? databaseConnectionString = null,
        bool allowInMemoryDevelopment = false,
        bool secretRootConfigured = false,
        IDataProtectionProvider? productionDataProtectionProvider = null,
        ISecretRootPolicy? productionSecretRootPolicy = null)
    {
        if (enableLiveMqtt)
        {
            throw new InvalidOperationException(
                "Live MQTT is deferred until the Phase 8 broker contract is proven.");
        }

        services.AddSingleton<ICommandProtocolAdapter, Commands.HmsIdfProvisionalProtocolAdapter>();
        services.AddSingleton<IDeviceCommandTransport, DeferredDeviceCommandTransport>();
        if (!string.IsNullOrWhiteSpace(databaseConnectionString))
            services.AddScoped<ICommandAuditStore, EfCommandAuditStore>();
        else
            services.AddSingleton<ICommandAuditStore, InMemoryCommandAuditStore>();
        services.AddSingleton<ICommandCatalog, Commands.PinnedCommandCatalog>();
        services.AddScoped<SendDeviceCommandHandler>();
        if (!string.IsNullOrWhiteSpace(databaseConnectionString))
        {
            services.AddScoped<IOrganizationStore, EfOrganizationStore>();
            services.AddScoped<IDeviceStore, EfDeviceStore>();
            services.AddScoped<IProjectStore, EfProjectStore>();
            services.AddScoped<IPackageStore, EfPackageStore>();
            services.AddScoped<IAreaStore, EfAreaStore>();
        }
        else
        {
            services.AddSingleton<IOrganizationStore, InMemoryOrganizationStore>();
            services.AddSingleton<IDeviceStore, InMemoryDeviceStore>();
            services.AddSingleton<IProjectStore, InMemoryProjectStore>();
            services.AddSingleton<IPackageStore, InMemoryPackageStore>();
            services.AddSingleton<IAreaStore, InMemoryAreaStore>();
        }
        services.AddScoped<CreateOrganizationHandler>();
        services.AddScoped<UpdateOrganizationHandler>();
        services.AddScoped<CreateProjectHandler>();
        services.AddScoped<UpdateProjectHandler>();
        services.AddScoped<CreatePackageHandler>();
        services.AddScoped<UpdatePackageHandler>();
        services.AddScoped<CreateAreaHandler>();
        services.AddScoped<UpdateAreaHandler>();
        services.AddScoped<CreateDeviceHandler>();
        services.AddScoped<UpdateDeviceHandler>();
        services.AddScoped<Func<DeviceId, Device?>>(sp =>
            id => sp.GetRequiredService<IDeviceStore>().Find(id));
        if (!string.IsNullOrWhiteSpace(databaseConnectionString))
            services.AddScoped<IPermissionStore, EfPermissionStore>();
        else
            services.AddSingleton<IPermissionStore, InMemoryPermissionStore>();
        if (!string.IsNullOrWhiteSpace(databaseConnectionString))
        {
            services.AddScoped<IDeviceRoleStore, EfDeviceRoleStore>();
            services.AddScoped<IDeviceMembershipStore, EfDeviceMembershipStore>();
        }
        else
        {
            // One instance behind both interfaces, so role in-use checks see the memberships.
            services.AddSingleton<InMemoryDeviceAccessStore>();
            services.AddSingleton<IDeviceRoleStore>(sp => sp.GetRequiredService<InMemoryDeviceAccessStore>());
            services.AddSingleton<IDeviceMembershipStore>(sp => sp.GetRequiredService<InMemoryDeviceAccessStore>());
        }
        services.AddScoped<GrantFeaturePermissionHandler>();
        services.AddScoped<GrantResourceScopeHandler>();
        services.AddScoped<GrantDeviceActionHandler>();
        services.AddScoped<RevokeDeviceHandler>();
        // Scoped: works over the in-memory singleton stores and the scoped EF stores alike.
        services.AddScoped<IDeviceAccessResolver, DeviceAccessResolver>();
        services.AddScoped<CreateDeviceRoleHandler>();
        services.AddScoped<UpdateDeviceRoleHandler>();
        services.AddScoped<DeleteDeviceRoleHandler>();
        services.AddScoped<AssignDeviceMembershipHandler>();
        services.AddScoped<RemoveDeviceMembershipHandler>();
        services.AddSingleton<IBrokerCredentialProvider, RandomBrokerCredentialProvider>();
        if (!string.IsNullOrWhiteSpace(databaseConnectionString))
            services.AddScoped<IProvisioningSecretGenerator, EfProvisioningSecretGenerator>();
        else
            services.AddSingleton<IProvisioningSecretGenerator, RandomProvisioningSecretGenerator>();
        if (!string.IsNullOrWhiteSpace(databaseConnectionString))
            services.AddScoped<IProvisioningBundleStore, EfProvisioningBundleStore>();
        else
            services.AddSingleton<IProvisioningBundleStore, InMemoryProvisioningBundleStore>();
        services.AddScoped<CreateProvisioningBundleHandler>();
        if (!string.IsNullOrWhiteSpace(databaseConnectionString))
        {
            services.AddScoped<ILicenseTrustStore, EfLicenseTrustStore>();
            services.AddScoped<ILicenseStore, EfLicenseStore>();
            services.AddScoped<ILicenseApplyJournalStore, EfLicenseApplyJournalStore>();
        }
        else
        {
            services.AddSingleton<ILicenseTrustStore>(_ => new InMemoryLicenseTrustStore([]));
            services.AddSingleton<ILicenseStore, InMemoryLicenseStore>();
            services.AddSingleton<ILicenseApplyJournalStore, InMemoryLicenseApplyJournalStore>();
        }
        if (!allowInMemoryDevelopment && productionSecretRootPolicy is null)
        {
            throw new InvalidOperationException("KMS_SECRET_ROOT_PROVIDER_REQUIRED");
        }

        services.AddSingleton<ISecretRootPolicy>(
            productionSecretRootPolicy ?? new FailClosedSecretRootPolicy(secretRootConfigured));
        services.AddSingleton<ILicenseSigner, ExternalLicenseSignerUnavailable>();
        services.AddSingleton<ILicenseVerifier, UnavailableLicenseVerifier>();
        services.AddScoped<ApplyLicenseHandler>();
        if (!string.IsNullOrWhiteSpace(databaseConnectionString))
            services.AddScoped<IAuthorizationEpochStore, EfAuthorizationEpochStore>();
        else
            services.AddSingleton<IAuthorizationEpochStore, InMemoryAuthorizationEpochStore>();
        services.AddSingleton<IAuthorizedEventPublisher, BoundedDeviceEventPublisher>();
        services.AddSingleton<Hms.Server.Application.Realtime.IDeviceTelemetryStore, Hms.Server.Application.Realtime.InMemoryDeviceTelemetryStore>();
        if (string.IsNullOrWhiteSpace(databaseConnectionString) && !allowInMemoryDevelopment)
        {
            throw new InvalidOperationException("POSTGRES_CONNECTION_REQUIRED");
        }
        if (!allowInMemoryDevelopment && productionDataProtectionProvider is null)
        {
            throw new InvalidOperationException("KMS_DATA_PROTECTION_PROVIDER_REQUIRED");
        }
        if (productionDataProtectionProvider is EphemeralDataProtectionProvider)
        {
            throw new InvalidOperationException("EPHEMERAL_DATA_PROTECTION_FORBIDDEN");
        }

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            if (!string.IsNullOrWhiteSpace(databaseConnectionString))
            {
                options.UseNpgsql(databaseConnectionString);
            }
            else
            {
                // Development fallback only; production configuration must supply PostgreSQL.
                options.UseInMemoryDatabase("hms-server-development");
            }
        });

        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 12;
            })
            .AddRoles<ApplicationRole>()
            .AddSignInManager()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        var dataProtection = services.AddDataProtection()
            .SetApplicationName("Hms.Server");
        if (allowInMemoryDevelopment)
        {
            // Override any host-level provider so development and test runs do
            // not read or write a user-profile key ring. Production keeps the
            // durable KMS-backed provider boundary below.
            services.AddSingleton<IDataProtectionProvider>(_ =>
                new EphemeralDataProtectionProvider(NullLoggerFactory.Instance));
        }
        else
        {
            services.RemoveAll<IDataProtectionProvider>();
            services.AddSingleton(productionDataProtectionProvider!);
        }
        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddIdentityCookies();
        var cookieSecurePolicy = allowInMemoryDevelopment
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        services.Configure<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme, options =>
        {
            options.Cookie.Name = "hms.server.session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = cookieSecurePolicy;
        });
        services.AddAuthorization(options => options.AddPolicy("hms.user.manage", policy =>
            // The persisted permission store is the authority for mutable grants.
            // Requiring a bootstrap claim here would reject users whose grant was
            // created after login; the controller re-checks the current store.
            policy.RequireAuthenticatedUser()
                .AddRequirements(new Authorization.FeaturePermissionRequirement("users.manage"))));
        services.AddAuthorization(options => options.AddPolicy("hms.permissions.manage", policy =>
            policy.RequireAuthenticatedUser()
                .AddRequirements(new Authorization.FeaturePermissionRequirement("permissions.manage"))));
        services.AddScoped<IUserManagement, IdentityUserManagement>();
        services.AddScoped<IUserScopeStore, IdentityUserManagement>();
        services.AddScoped<IRealtimePrincipalRefresher, IdentityRealtimePrincipalRefresher>();
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "hms.server.csrf";
            options.Cookie.HttpOnly = false;
            options.Cookie.SecurePolicy = cookieSecurePolicy;
        });
        if (allowInMemoryDevelopment)
        {
            // Identity may register its own provider after AddDataProtection;
            // register the ephemeral provider last so development cannot fall
            // back to a profile key ring with stale or unwritable keys.
            services.RemoveAll<IDataProtectionProvider>();
            services.AddSingleton<IDataProtectionProvider>(_ =>
                new EphemeralDataProtectionProvider(NullLoggerFactory.Instance));
        }
        return services;
    }
}
