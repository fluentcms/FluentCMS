using System.Globalization;
using FluentCMS.Admin.Api;
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
        services.ShouldNotContain(descriptor => descriptor.ServiceType == typeof(IManagementApi));
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

    [Fact]
    public async Task WithAValidFileTheManagementApiCallsUnderTheApiBaseUrl()
    {
        var handler = new FakeHandler(null);
        var services = new ServiceCollection();
        services.AddAdminStartup(AdminConfigurationReader.Read("""{ "apiBaseUrl": "https://cms.example.com/cms" }"""), Culture, Formats);
        services.Configure<ManagementApiOptions>(options => options.PrimaryHandler = () => handler);
        // The WebAssembly host gives logging, which the resources of the rate limit notice need.
        services.AddLogging();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();

        var thrown = await Should.ThrowAsync<ApiProblemException>(() =>
            scope.ServiceProvider.GetRequiredService<IManagementApi>().SendAsync(new ApiRequest(HttpMethod.Get, "me"), TestContext.Current.CancellationToken));

        thrown.Problem.Status.ShouldBe(404);
        handler.Requests.ShouldHaveSingleItem().RequestUri.ShouldBe(new Uri("https://cms.example.com/cms/api/management/v1/me"));
        scope.ServiceProvider.GetRequiredService<IRateLimitNotice>().ShouldBeOfType<RateLimitNotice>();
    }
}
