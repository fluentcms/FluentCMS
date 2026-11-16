using System.Net;
using FluentCMS.Core.Authorization;
using FluentCMS.Testing;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.ApiTests;

public sealed class TestHostTests
{
    private static readonly CancellationToken Token = TestContext.Current.CancellationToken;

    // The admin signs in with the code flow and PKCE and calls the Management API with the token, as AsUserAsync does.
    [Fact]
    public async Task ASignedInAdministratorReadsTheManagementApi()
    {
        await using var host = new FluentCmsTestHost();
        var administrator = await host.CreateUserAsync(BuiltInRoles.SuperAdminName, cancellationToken: Token);
        using var api = await host.AsUserAsync(administrator);

        using var response = await api.GetAsync("/api/management/v1/me", Token);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
