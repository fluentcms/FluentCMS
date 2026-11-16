using System.Globalization;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Authentication;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Configuration;
using FluentCMS.Admin.Forms;
using FluentCMS.Admin.Interop;
using FluentCMS.Admin.Localization;
using FluentCMS.Admin.Shell;
using FluentCMS.Admin.Tests.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Configuration;

public sealed class AdminServicesTests
{
    private static readonly InterfaceCulture Culture = new(CultureInfo.GetCultureInfo("en"));
    private static readonly FormatService Formats = new(CultureInfo.GetCultureInfo("en"), TimeZoneInfo.Utc);

    // With an invalid file, the error page's state, the resources and the document, the theme AdminApp holds, and nothing that
    // can call the API and nothing of the wrapped set.
    [Fact]
    public void AnInvalidFileRegistersNothingButWhatTheErrorPageNeeds()
    {
        var services = new ServiceCollection();

        services.AddAdminStartup(AdminConfigurationReader.Read("""{ "apiBaseUrl": "cms.example.com" }"""), Culture, Formats);

        var errorPage = new ServiceCollection().AddAdminLocalization(Culture, Formats).AddAdminAccessibility()
            .Select(descriptor => descriptor.ServiceType)
            .Append(typeof(AdminStartup))
            .Append(typeof(IThemeStorage))
            .Append(typeof(ShellTheme));
        services.Select(descriptor => descriptor.ServiceType).ShouldBe(errorPage, ignoreOrder: true);
        services.ShouldNotContain(descriptor => descriptor.ServiceType == typeof(AdminConfiguration));
        services.ShouldNotContain(descriptor => descriptor.ServiceType == typeof(HttpClient));
        services.ShouldNotContain(descriptor => descriptor.ServiceType == typeof(IManagementApi));
        services.ShouldNotContain(descriptor => descriptor.ServiceType == typeof(ICmsTextSource));
    }

    [Fact]
    public void AValidFileRegistersTheWrappedSetWithTheAdminsLocalizer()
    {
        var services = new ServiceCollection();

        services.AddAdminStartup(AdminConfigurationReader.Read("""{ "apiBaseUrl": "https://cms.example.com" }"""), Culture, Formats);
        services.AddLogging();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ICmsTextSource>().ShouldBeOfType<CmsTextLocalizer>();
        services.Count(descriptor => descriptor.ServiceType == typeof(ICmsTextSource)).ShouldBe(1);
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
        // The WebAssembly host gives logging, which the resources of the rate limit notice need, and the navigation and the
        // JavaScript runtime, which the sign-in needs.
        services.AddLogging();
        services.AddSingleton<NavigationManager>(new StubNavigationManager());
        services.AddSingleton<IJSRuntime>(new NoJSRuntime());
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();

        var thrown = await Should.ThrowAsync<ApiProblemException>(() =>
            scope.ServiceProvider.GetRequiredService<IManagementApi>().SendAsync(new ApiRequest(HttpMethod.Get, "me"), TestContext.Current.CancellationToken));

        thrown.Problem.Status.ShouldBe(404);
        handler.Requests.ShouldHaveSingleItem().RequestUri.ShouldBe(new Uri("https://cms.example.com/cms/api/management/v1/me"));
        scope.ServiceProvider.GetRequiredService<IRateLimitNotice>().ShouldBeOfType<RateLimitNotice>();
    }

    // The session of the sign-in is the token source of the client, and the forms' store keeps drafts in sessionStorage.
    [Fact]
    public async Task AValidFileRegistersTheSignInAsTheTokenSourceAndTheFormStore()
    {
        var services = new ServiceCollection();
        services.AddAdminStartup(AdminConfigurationReader.Read("""{ "apiBaseUrl": "https://cms.example.com" }"""), Culture, Formats);
        services.AddLogging();
        services.AddSingleton<NavigationManager>(new StubNavigationManager());
        services.AddSingleton<IJSRuntime>(new NoJSRuntime());
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var scope = provider.CreateAsyncScope();

        var session = scope.ServiceProvider.GetRequiredService<AdminSession>();
        scope.ServiceProvider.GetRequiredService<IAccessTokenSource>().ShouldBeSameAs(session);
        scope.ServiceProvider.GetRequiredService<IAdminSession>().ShouldBeSameAs(session);
        scope.ServiceProvider.GetRequiredService<IFormDraftStore>().ShouldBeOfType<FormDraftStore>();
        scope.ServiceProvider.GetRequiredService<ISessionStorage>().ShouldBeOfType<SessionStorageInterop>();
        (await session.GetAccessTokenAsync(TestContext.Current.CancellationToken)).ShouldBeNull();
    }
}
