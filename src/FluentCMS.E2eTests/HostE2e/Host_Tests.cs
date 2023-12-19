using FluentCMS.E2eTests;
using FluentCMS.Web.UI.ApiClients;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using Shouldly;

namespace FluentCMS.E2eTests.HostE2e;

public class Host_Tests : AuthorizedBaseE2eTest<IHostClient, HostClient>
{

    // GET
    // /api/Host/Get
    [Fact]
    public async Task Should_GetHost()
    {
        await Login();
        var host = await Client.GetAsync();
        host.ShouldNotBeNull();
        host.Data.ShouldNotBeNull();
        host.Data.Id.ShouldNotBe(default);
    }


    // PUT
    // /api/Host/Update
    [Fact]
    public async Task Should_UpdateHost()
    {
        await Login();

        HostUpdateRequest? request = new HostUpdateRequest()
        { SuperUsers = ["superadmin", "dummysuperadmin"] };
        var host = await Client.UpdateAsync(request);

        host.ShouldNotBeNull();
        host.Data.ShouldNotBeNull();
        host.Data.Id.ShouldNotBe(default);
        host.Data.SuperUsers.ShouldNotBeNull();
        host.Data.SuperUsers.Count.ShouldBe(2);
        host.Data.SuperUsers.ShouldContain("dummySuperUser".ToLower());
    }
}
