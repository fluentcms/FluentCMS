using FluentCMS.Core.Identity;
using FluentCMS.Core.Identity.Oidc;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.Tests.Identity;

// The authorization server takes out Data Protection's hosted service by the full name of an internal type, which a new
// framework version may rename; then the ring would load at every start again, and this fails first.
public sealed class DataProtectionHostedServiceTests
{
    [Fact]
    public void TheHostedServiceIsFoundByItsNameAndTakenOut()
    {
        typeof(KeyManagementOptions).Assembly.GetType(AuthorizationServerRegistration.DataProtectionHostedService)
            .ShouldNotBeNull($"Data Protection has no type {AuthorizationServerRegistration.DataProtectionHostedService} any more; find what loads the ring at start now.");

        var services = new ServiceCollection();
        services.AddDataProtection();
        services.ShouldContain(service => IsDataProtectionHostedService(service));

        AuthorizationServerRegistration.Add(services, new ClientOptions());

        services.ShouldNotContain(service => IsDataProtectionHostedService(service));
    }

    private static bool IsDataProtectionHostedService(ServiceDescriptor service) =>
        service.ServiceType == typeof(IHostedService) && service.ImplementationType?.FullName == AuthorizationServerRegistration.DataProtectionHostedService;
}
