using FluentCMS.E2eTests;
using FluentCMS.Web.UI.ApiClients;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace FluentCMS.E2eTests.HostE2e;

public class Host_Tests : BaseE2eTest<IHostClient, HostClient>
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
        // TODO: Login First
        HostUpdateRequest? request = new HostUpdateRequest()
        { SuperUsers = ["dummySuperUser"] };
        var host = await Client.UpdateAsync(request);

        host.ShouldNotBeNull();
        host.Data.ShouldNotBeNull();
        host.Data.Id.ShouldNotBe(default);
        host.Data.SuperUsers.ShouldNotBeNull();
        host.Data.SuperUsers.Count.ShouldBe(1);
        host.Data.SuperUsers.ShouldContain("dummySuperUser");
    }
    private async Task Login(string userName = "superadmin", string password = "Passw0rd!")
    {
        var scope = WebUi.Services.CreateScope();
        var accountClient = scope.ServiceProvider.GetRequiredService<AccountClient>();

        var resp = await accountClient.LoginAsync(new UserLoginRequest() { Username=userName,Password = password});

        E2eHttpClientFactory<Program>.OverrideToken = resp.Data.Token;
    }
}
