using FluentCMS.Admin.Api;
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
        using var provider = services.BuildServiceProvider();
        provider.GetService<IManagementApi>().ShouldBeNull();
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

    [Fact]
    public async Task WithAValidFileTheManagementApiCallsUnderTheApiBaseUrl()
    {
        var handler = new FakeHandler(null);
        var services = new ServiceCollection();
        services.AddAdminStartup(AdminConfigurationReader.Read("""{ "apiBaseUrl": "https://cms.example.com/cms" }"""));
        services.Configure<ManagementApiOptions>(options => options.PrimaryHandler = () => handler);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();

        var thrown = await Should.ThrowAsync<ApiProblemException>(() =>
            scope.ServiceProvider.GetRequiredService<IManagementApi>().SendAsync(new ApiRequest(HttpMethod.Get, "me"), TestContext.Current.CancellationToken));

        thrown.Problem.Status.ShouldBe(404);
        handler.Requests.ShouldHaveSingleItem().RequestUri.ShouldBe(new Uri("https://cms.example.com/cms/api/management/v1/me"));
    }
}
