using Hms.Server.Api.Controllers;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace Hms.Server.UnitTests;

public sealed class RequestLimitTests
{
    [Fact]
    public void Device_command_endpoint_declares_a_bounded_request_body()
    {
        var method = typeof(DeviceCommandsController).GetMethod(nameof(DeviceCommandsController.Send));

        var limit = Assert.IsType<RequestSizeLimitAttribute>(
            method?.GetCustomAttributes(typeof(RequestSizeLimitAttribute), inherit: true).SingleOrDefault());

        Assert.Equal(64 * 1024,
            ((IRequestSizeLimitMetadata)limit).MaxRequestBodySize);
    }

    [Fact]
    public void License_and_provisioning_mutations_are_bounded()
    {
        Assert.Equal(64 * 1024, RequestLimit<LicensesController>(nameof(LicensesController.Apply)));
        Assert.Equal(64 * 1024, RequestLimit<ProvisioningController>(nameof(ProvisioningController.Create)));
    }

    [Fact]
    public void License_and_provisioning_fields_have_model_limits()
    {
        var licenseProperties = typeof(LicenseApplyRequestBody).GetProperties();
        Assert.Contains(licenseProperties.Single(x => x.Name == nameof(LicenseApplyRequestBody.Kid))
            .GetCustomAttributes(typeof(StringLengthAttribute), inherit: true)
            .OfType<StringLengthAttribute>(), x => x.MaximumLength == 128);
        Assert.Contains(licenseProperties.Single(x => x.Name == nameof(LicenseApplyRequestBody.Issuer))
            .GetCustomAttributes(typeof(StringLengthAttribute), inherit: true)
            .OfType<StringLengthAttribute>(), x => x.MaximumLength == 128);
        Assert.Contains(licenseProperties.Single(x => x.Name == nameof(LicenseApplyRequestBody.RawEnvelope))
            .GetCustomAttributes(typeof(MaxLengthAttribute), inherit: true)
            .OfType<MaxLengthAttribute>(), x => x.Length == 2048);
    }

    private static long? RequestLimit<TController>(string methodName)
    {
        var method = typeof(TController).GetMethod(methodName);
        var limit = Assert.IsType<RequestSizeLimitAttribute>(
            method?.GetCustomAttributes(typeof(RequestSizeLimitAttribute), inherit: true).SingleOrDefault());
        return ((IRequestSizeLimitMetadata)limit).MaxRequestBodySize;
    }
}
