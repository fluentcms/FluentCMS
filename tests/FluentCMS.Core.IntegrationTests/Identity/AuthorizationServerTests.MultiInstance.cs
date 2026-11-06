using System.Net;
using FluentCMS.Core.Authorization;
using FluentCMS.Testing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement.Internal;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Core.IntegrationTests.Identity;

// Instances behind a load balancer share one database and nothing else, so each must read what another protected.
public sealed partial class AuthorizationServerTests
{
    private const string Purpose = "FluentCMS.Tests.MultiInstance";

    [Fact]
    public async Task TwoHostsOnOneDatabaseReadEachOthersPayloadsCookiesAndTokens()
    {
        await InDatabaseFileAsync(async file =>
        {
            await using var first = NewHost().UseSqlite(file);
            var editor = await first.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
            await using var second = NewHost().UseSqlite(file);
            _ = second.Services;

            var fromFirst = Protector(first).Protect("from the first instance");
            Protector(second).Unprotect(fromFirst).ShouldBe("from the first instance");
            var fromSecond = Protector(second).Protect("from the second instance");
            Protector(first).Unprotect(fromSecond).ShouldBe("from the second instance");

            (await AuthorizeAsync(second, await first.SignInCookieAsync(editor))).ShouldStartWith(FluentCmsTestHost.OAuthRedirectUri + "?code=");
            (await AuthorizeAsync(first, await second.SignInCookieAsync(editor))).ShouldStartWith(FluentCmsTestHost.OAuthRedirectUri + "?code=");

            var tokens = await first.SignInAsync(editor);
            using (var api = Bearer(tokens.AccessToken, second))
            {
                (await WhoAmIAsync(api)).GetProperty("id").GetGuid().ShouldBe(editor.Id);
            }

            using var client = second.CreateSecureClient();
            (await TokenAsync(client, Refresh(tokens.RefreshToken!), Token)).AccessToken.ShouldNotBeNullOrEmpty();

            DataProtectionKeys(first).ShouldBe(1);
        });
    }

    [Fact]
    public async Task HostsThatMakeTheirFirstDataProtectionKeyTogetherShareIt()
    {
        await InDatabaseFileAsync(async file =>
        {
            await using var first = NewHost().UseSqlite(file);
            _ = first.Services;
            await using var second = NewHost().UseSqlite(file);
            _ = second.Services;
            DataProtectionKeys(first).ShouldBe(0);

            using var start = new Barrier(2);
            var payloads = await Task.WhenAll(new[] { first, second }.Select(host => Task.Run(
                () =>
                {
                    var protector = Protector(host);
                    start.SignalAndWait(Token);
                    return protector.Protect("made together");
                },
                Token)));

            DataProtectionKeys(first).ShouldBe(1);
            DefaultKey(first).ShouldBe(DefaultKey(second));
            Protector(first).Unprotect(payloads[1]).ShouldBe("made together");
            Protector(second).Unprotect(payloads[0]).ShouldBe("made together");
        });
    }

    [Fact]
    public async Task DataProtectionKeysSurviveARestart()
    {
        await InDatabaseFileAsync(async file =>
        {
            string payload;
            string cookie;
            int keys;
            await using (var first = NewHost().UseSqlite(file))
            {
                var editor = await first.CreateUserAsync(BuiltInRoles.EditorName, cancellationToken: Token);
                payload = Protector(first).Protect("before the restart");
                cookie = await first.SignInCookieAsync(editor);
                keys = DataProtectionKeys(first);
            }

            await using var second = NewHost().UseSqlite(file);

            Protector(second).Unprotect(payload).ShouldBe("before the restart");
            (await AuthorizeAsync(second, cookie)).ShouldStartWith(FluentCmsTestHost.OAuthRedirectUri + "?code=");
            DataProtectionKeys(second).ShouldBe(keys);
        });
    }

    private static async Task InDatabaseFileAsync(Func<string, Task> test)
    {
        var directory = Directory.CreateTempSubdirectory("fluentcms-instances-");
        try
        {
            await test(Path.Combine(directory.FullName, "cms.db"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            directory.Delete(recursive: true);
        }
    }

    private static IDataProtector Protector(FluentCmsTestHost host) =>
        host.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose);

    private static Guid DefaultKey(FluentCmsTestHost host) =>
        host.Services.GetRequiredService<IKeyRingProvider>().GetCurrentKeyRing().DefaultKeyId;

    // Where the authorize endpoint sends a browser with this cookie: to the client with a code when the cookie is read,
    // to the login page when it is not.
    private static async Task<string> AuthorizeAsync(FluentCmsTestHost host, string cookie)
    {
        using var client = host.CreateSecureClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/connect/authorize?client_id={TestClientId}&response_type=code&scope=openid&redirect_uri={Uri.EscapeDataString(FluentCmsTestHost.OAuthRedirectUri)}&code_challenge=abcdefghijabcdefghijabcdefghijabcdefghijabc&code_challenge_method=S256");
        request.Headers.Add("Cookie", cookie);

        using var response = await client.SendAsync(request, Token);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        return response.Headers.Location!.ToString();
    }
}
