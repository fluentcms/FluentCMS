using System.Globalization;
using FluentCMS.Admin.Configuration;
using FluentCMS.Admin.Localization;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Configuration;

public sealed class AdminServicesTests
{
    private static readonly InterfaceCulture Culture = new(CultureInfo.GetCultureInfo("en"));
    private static readonly FormatService Formats = new(CultureInfo.GetCultureInfo("en"), TimeZoneInfo.Utc);

    // With an invalid file, the error page's state, the resources and the document, and nothing that can call the API.
    [Fact]
    public void AnInvalidFileRegistersNothingButWhatTheErrorPageNeeds()
    {
        var services = new ServiceCollection();

        services.AddAdminStartup(AdminConfigurationReader.Read("""{ "apiBaseUrl": "cms.example.com" }"""), Culture, Formats);

        var errorPage = new ServiceCollection().AddAdminLocalization(Culture, Formats).AddAdminAccessibility()
            .Select(descriptor => descriptor.ServiceType)
            .Append(typeof(AdminStartup));
        services.Select(descriptor => descriptor.ServiceType).ShouldBe(errorPage, ignoreOrder: true);
        services.ShouldNotContain(descriptor => descriptor.ServiceType == typeof(AdminConfiguration));
        services.ShouldNotContain(descriptor => descriptor.ServiceType == typeof(HttpClient));
    }

    [Fact]
    public void AValidFileRegistersTheAdminServicesWithItsConfiguration()
    {
        var startup = AdminConfigurationReader.Read("""{ "apiBaseUrl": "https://cms.example.com" }""");
        var services = new ServiceCollection();

        services.AddAdminStartup(startup, Culture, Formats);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<AdminStartup>().ShouldBeSameAs(startup);
        provider.GetRequiredService<AdminConfiguration>().ShouldBeSameAs(startup.Configuration);
        provider.GetRequiredService<InterfaceCulture>().ShouldBeSameAs(Culture);
    }
}
