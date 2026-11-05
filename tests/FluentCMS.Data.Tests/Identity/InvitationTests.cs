using System.Security.Cryptography;
using System.Text;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Identity;
using FluentCMS.Providers;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using static FluentCMS.Data.Tests.Identity.IdentityFixture;

namespace FluentCMS.Data.Tests.Identity;

public sealed class InvitationTests : IDisposable
{
    private readonly IdentityFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    private static Task<InvitationSent> InviteAsync(IdentityFixture fixture, string email, params Guid[] roles) =>
        fixture.WithAsync(services => services.GetRequiredService<InvitationService>().InviteAsync(email, roles, AcceptUrl, Token));

    private static Task<Microsoft.AspNetCore.Identity.IdentityResult> AcceptAsync(IdentityFixture fixture, string token, string userName = "ann", string password = Password) =>
        fixture.WithAsync(services => services.GetRequiredService<InvitationService>().AcceptAsync(token, userName, password, Token));

    [Fact]
    public async Task AnInvitationIsSentWithItsTokenStoresOnlyTheHashAndBecomesAUserWithItsRoles()
    {
        var sent = await InviteAsync(_fixture, "ann@example.com", BuiltInRoles.Editor, BuiltInRoles.Author);

        var message = _fixture.Email.Sent.ShouldHaveSingleItem();
        message.To.ShouldBe("ann@example.com");
        var token = TokenOf(message);
        token.Length.ShouldBe(43);

        var row = _fixture.Invitations().ShouldHaveSingleItem();
        row.Id.ShouldBe(sent.Id);
        row.TokenHash.ShouldBe(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))));
        row.TokenHash.ShouldNotContain(token);
        row.ExpiresAt.ShouldBe(Start.AddDays(7));
        (await _fixture.WithAsync(services => services.GetRequiredService<InvitationService>().FindAsync(token, Token))).ShouldNotBeNull().Email.ShouldBe("ann@example.com");

        Succeeded(await AcceptAsync(_fixture, token));

        var accepted = _fixture.Invitations().Single();
        accepted.AcceptedAt.ShouldBe(Start);
        var user = await _fixture.UsersAsync(async users => (await users.FindByEmailAsync("ann@example.com")).ShouldNotBeNull());
        accepted.UserId.ShouldBe(user.Id);
        user.UserName.ShouldBe("ann");
        user.EmailConfirmed.ShouldBeTrue();
        (await _fixture.UsersAsync(users => users.CheckPasswordAsync(user, Password))).ShouldBeTrue();
        (await _fixture.UsersAsync(users => users.GetRoleIdsAsync(user))).ShouldBe([BuiltInRoles.Editor, BuiltInRoles.Author], ignoreOrder: true);
    }

    [Fact]
    public async Task AnInvitationIsUsedOnce()
    {
        await InviteAsync(_fixture, "ann@example.com", BuiltInRoles.Editor);
        var token = TokenOf(_fixture.Email.Sent.Single());
        Succeeded(await AcceptAsync(_fixture, token));

        var again = await AcceptAsync(_fixture, token, "ann2");

        again.Errors.ShouldHaveSingleItem().Code.ShouldBe(IdentityErrorCodes.InvalidInvitation);
        (await _fixture.UsersAsync(users => users.FindByNameAsync("ann2"))).ShouldBeNull();
        (await _fixture.WithAsync(services => services.GetRequiredService<InvitationService>().FindAsync(token, Token))).ShouldBeNull();
    }

    [Fact]
    public async Task AnInvitationIsRefusedAfterItsExpiry()
    {
        await InviteAsync(_fixture, "ann@example.com", BuiltInRoles.Editor);
        var token = TokenOf(_fixture.Email.Sent.Single());

        _fixture.Clock.Advance(TimeSpan.FromDays(7));

        (await AcceptAsync(_fixture, token)).Errors.ShouldHaveSingleItem().Code.ShouldBe(IdentityErrorCodes.InvitationExpired);
        (await _fixture.WithAsync(services => services.GetRequiredService<InvitationService>().FindAsync(token, Token))).ShouldBeNull();
        (await _fixture.UsersAsync(users => users.FindByEmailAsync("ann@example.com"))).ShouldBeNull();
    }

    [Fact]
    public async Task AnUnknownTokenIsRefused()
    {
        (await AcceptAsync(_fixture, "not-a-token")).Errors.ShouldHaveSingleItem().Code.ShouldBe(IdentityErrorCodes.InvalidInvitation);
    }

    [Fact]
    public async Task AnAcceptThatFailsLeavesNoUserAndTheInvitationUsable()
    {
        await InviteAsync(_fixture, "ann@example.com", BuiltInRoles.Editor);
        var token = TokenOf(_fixture.Email.Sent.Single());

        (await AcceptAsync(_fixture, token, password: "short")).Succeeded.ShouldBeFalse();

        (await _fixture.UsersAsync(users => users.FindByEmailAsync("ann@example.com"))).ShouldBeNull();
        _fixture.Invitations().Single().AcceptedAt.ShouldBeNull();
        Succeeded(await AcceptAsync(_fixture, token));
    }

    [Fact]
    public async Task ANewInvitationToTheSameAddressEndsTheOneBefore()
    {
        await InviteAsync(_fixture, "ann@example.com", BuiltInRoles.Editor);
        await InviteAsync(_fixture, "ann@example.com", BuiltInRoles.Author);
        var (first, second) = (TokenOf(_fixture.Email.Sent.First()), TokenOf(_fixture.Email.Sent.Last()));

        (await AcceptAsync(_fixture, first)).Errors.ShouldHaveSingleItem().Code.ShouldBe(IdentityErrorCodes.InvalidInvitation);
        Succeeded(await AcceptAsync(_fixture, second));
    }

    [Fact]
    public async Task AnInvitationToAUserOrAnUnknownRoleOrABadAddressIsRefused()
    {
        await _fixture.CreateUserAsync("ann");

        (await Should.ThrowAsync<ValidationException>(() => InviteAsync(_fixture, "ann@example.com"))).Errors.ShouldHaveSingleItem().Code.ShouldBe(IdentityErrorCodes.EmailTaken);
        (await Should.ThrowAsync<ValidationException>(() => InviteAsync(_fixture, "bob@example.com", Guid.CreateVersion7()))).Errors.ShouldHaveSingleItem().Code.ShouldBe(IdentityErrorCodes.UnknownRole);
        (await Should.ThrowAsync<ValidationException>(() => InviteAsync(_fixture, "not an address"))).Errors.ShouldHaveSingleItem().Code.ShouldBe(IdentityErrorCodes.InvalidEmail);
        _fixture.Invitations().ShouldBeEmpty();
        _fixture.Email.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task InDevelopmentTheDefaultSenderWritesTheLinkToTheLog()
    {
        using var fixture = new IdentityFixture(environment: "Development");
        (await fixture.WithAsync(services => Task.FromResult(services.GetRequiredService<IEmailSender>()))).ShouldBeOfType<LogEmailSender>();

        await InviteAsync(fixture, "ann@example.com", BuiltInRoles.Editor);

        var logged = fixture.Logs.Messages.Single(message => message.Contains("ann@example.com", StringComparison.Ordinal));
        logged.ShouldContain(AcceptUrl.AbsoluteUri + "?token=");
        var token = Uri.UnescapeDataString(logged.Split("?token=")[1].Split('\n')[0]);
        Succeeded(await AcceptAsync(fixture, token));
    }

    [Fact]
    public async Task OutsideDevelopmentNothingIsSentUntilASenderIsRegisteredAndTheInvitationIsEnded()
    {
        using var fixture = new IdentityFixture(environment: "Production");

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => InviteAsync(fixture, "ann@example.com", BuiltInRoles.Editor));

        refused.Message.ShouldStartWith(IdentityErrorCodes.NoEmailSender);
        fixture.Invitations().ShouldHaveSingleItem().RevokedAt.ShouldNotBeNull();
        fixture.Logs.Messages.ShouldNotContain(message => message.Contains("?token=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheInvitationNamesWhoSentIt()
    {
        var admin = await _fixture.CreateUserAsync("admin", Password, BuiltInRoles.SuperAdmin);

        await _fixture.Database.AsAsync(PrincipalOf(admin), services => services.GetRequiredService<InvitationService>().InviteAsync("bob@example.com", [BuiltInRoles.Editor], AcceptUrl, Token));

        _fixture.Invitations().Single().CreatedBy.ShouldBe(admin.Id);
    }
}
