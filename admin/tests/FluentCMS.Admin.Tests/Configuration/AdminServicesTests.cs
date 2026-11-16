using FluentCMS.Admin.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Configuration;

public sealed class AdminServicesTests
{
    [Fact]
    public void AnInvalidFileRegistersNothingButTheErrorPageState()
    {
        var services = new ServiceCollection();

        services.AddAdminStartup(AdminConfigurationReader.Read("""{ "apiBaseUrl": "cms.example.com" }"""));

        services.ShouldHaveSingleItem().ServiceType.ShouldBe(typeof(AdminStartup));
    }

    [Fact]
    public void AValidFileRegistersTheAdminServicesWithItsConfiguration()
    {
        var startup = AdminConfigurationReader.Read("""{ "apiBaseUrl": "https://cms.example.com" }""");
        var services = new ServiceCollection();

        services.AddAdminStartup(startup);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<AdminStartup>().ShouldBeSameAs(startup);
        provider.GetRequiredService<AdminConfiguration>().ShouldBeSameAs(startup.Configuration);
    }
}
