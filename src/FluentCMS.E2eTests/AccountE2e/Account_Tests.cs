using FluentCMS.E2eTests;
using FluentCMS.Services;
using FluentCMS.Web.UI.ApiClients;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace FluentCMS.E2eTests.AccountE2e;

public class Account_Tests : BaseE2eTest<IAccountClient, AccountClient>
{
    // POST
    // /api/Account/ChangePassword
    [Fact]
    public async Task Should_ChangePassword()
    {
        var user = await SetupDummyUser();

        var res = await Client.ChangePasswordAsync(new UserChangePasswordRequest { UserId = user.Id, OldPassword = "Passw0rd!", NewPassword = "N3wPassw0rd!" });

        res.Errors.ShouldBeEmpty();
        res.Data.ShouldBeTrue();
    }
    // POST
    // /api/Account/Login
    [Fact]
    public async Task Should_Login()
    {
        var user = await SetupDummyUser();

        var res = await Client.LoginAsync(new UserLoginRequest
        {
            Username = "dummyuser",
            Password = "Passw0rd!",
        });

        res.Errors.ShouldBeEmpty();
        res.Data.UserId.ShouldBe(user.Id);
        res.Data.Token.ShouldNotBeEmpty();
    }


    // POST
    // /api/Account/Register
    [Fact]
    public async Task Should_Register()
    {
        var request = new FluentCMS.Web.UI.ApiClients.UserRegisterRequest()
        {
            Email = "dummyuser@gmail.com",
            Username = "dummyuser",
            Password = "Passw0rd!",
        };
        var resp = await Client.RegisterAsync(request);

        resp.Errors.ShouldBeEmpty();
        resp.Data.Email.ShouldBe("dummyuser@gmail.com");
        resp.Data.Username.ShouldBe("dummyuser");
    }

    private async Task<Entities.User> SetupDummyUser()
    {
        var scope = WebUi.Services.CreateScope();
        var userService = scope.ServiceProvider.GetRequiredService<IUserService>();
        var resp = await userService.Create(new Entities.User
        {
            UserName = "dummyuser",
            Email = "dummyuser@gmail.com",
            EmailConfirmed = true,
            NormalizedEmail = "dummyuser@gmail.com".ToUpper(),
            NormalizedUserName = "dummyuser".ToUpper(),
            Enabled = true,
        }, "Passw0rd!");

        return resp;
    }
}
