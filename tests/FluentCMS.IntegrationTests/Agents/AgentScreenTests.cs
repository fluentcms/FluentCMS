using System.Net;
using System.Text.RegularExpressions;
using FluentCMS.Core.Agents;
using FluentCMS.Core.Audit;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Agents;

public sealed partial class AgentScreenTests
{
    [Theory]
    [InlineData("/agents", "/sign-in?ReturnUrl=%2Fagents")]
    [InlineData("/agents/new", "/sign-in?ReturnUrl=%2Fagents%2Fnew")]
    public async Task An_anonymous_request_is_sent_to_sign_in(string route, string signIn)
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        await host.CreateFirstAdministratorAsync();

        var response = await host.Client.GetAsync(route);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(signIn, host.LocationOf(response));
    }

    [Fact]
    public async Task An_administrator_issues_a_key_that_is_shown_once_and_works()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var ownerId = await host.CreateFirstAdministratorAsync();
        await host.SignInAsync(AdminHost.Owner);

        var form = await host.Client.GetStringAsync("/agents/new");
        Assert.Contains("href=\"agents\"", form);
        Assert.Matches("name=\"Form.Read\"[^>]*checked", form);

        var issued = await host.SubmitAsync("/agents/new", "add-agent", CatalogSync());
        var html = await issued.Content.ReadAsStringAsync();

        // The key is in the response to the post, not in a redirect, and no page shows it again.
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        var key = KeyIn(html);
        Assert.True(AgentKeys.IsWellFormed(key));
        Assert.Contains("Key for catalog-sync", html);

        var credential = (await AuthenticateAsync(host, key)).Value;
        Assert.Equal("catalog-sync", credential.DisplayName);
        Assert.Equal(["product", "category"], credential.Scope.Types);
        Assert.Equal(["product.price"], credential.Scope.Fields);
        Assert.Equal([AgentOperation.Read, AgentOperation.Update], credential.Scope.Operations);
        Assert.Equal(AgentCaps.Create(120, TimeSpan.FromHours(1), 2.5m, TimeSpan.FromDays(1), 50, 10).Value, credential.Caps);
        Assert.InRange(credential.ExpiresAt, DateTimeOffset.UtcNow.AddDays(30).AddMinutes(-1), DateTimeOffset.UtcNow.AddDays(30));

        var list = await host.Client.GetStringAsync("/agents");
        Assert.Contains("catalog-sync", list);
        Assert.Contains($"revoke-{credential.Id:N}", list);
        Assert.Contains("data-status=\"active\"", list);
        Assert.DoesNotContain(key, list);
        Assert.DoesNotContain(key, await host.Client.GetStringAsync("/agents/new"));

        var issuing = Assert.Single(await AuditRowsAsync(host), row => row.Operation == "agent_credential.issued");
        Assert.Equal(InitiatorKinds.User, issuing.InitiatorKind);
        Assert.Equal(ownerId, issuing.InitiatorId);
        Assert.Equal(credential.Id, issuing.SubjectId);
    }

    [Fact]
    public async Task Problems_are_reported_next_to_the_fields_and_nothing_is_issued()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        await host.CreateFirstAdministratorAsync();
        await host.SignInAsync(AdminHost.Owner);

        var fields = CatalogSync();
        fields["Form.DisplayName"] = " ";
        fields["Form.Types"] = "Product";
        fields["Form.Fields"] = "price";
        fields["Form.CallsPerHour"] = "0";
        fields.Remove("Form.Read");
        fields.Remove("Form.Update");

        var refused = await host.SubmitAsync("/agents/new", "add-agent", fields);
        var html = await refused.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("Enter a name.", html);
        Assert.Contains("is not a content type slug", html);
        Assert.Contains("is not a field name", html);
        Assert.Contains("Enter a whole number of 1 or more.", html);
        Assert.Contains("id=\"operations-error\"", html);
        Assert.Equal(4, Regex.Count(html, "aria-invalid=\"true\""));
        Assert.DoesNotContain("data-testid=\"agent-key\"", html);
        Assert.Empty(await QueryAsync(host, context => context.AgentCredentials.ToListAsync()));
        Assert.DoesNotContain(await AuditRowsAsync(host), row => row.Operation == "agent_credential.issued");
    }

    [Fact]
    public async Task Revoking_from_the_list_refuses_the_key_from_then_on()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        var ownerId = await host.CreateFirstAdministratorAsync();
        await host.SignInAsync(AdminHost.Owner);
        var key = KeyIn(await (await host.SubmitAsync("/agents/new", "add-agent", CatalogSync())).Content.ReadAsStringAsync());
        var id = (await AuthenticateAsync(host, key)).Value.Id;

        var revoked = await host.SubmitAsync("/agents", $"revoke-{id:N}", new Dictionary<string, string>());
        var html = await revoked.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        Assert.Contains("The key of catalog-sync is revoked.", html);
        Assert.Contains("data-status=\"revoked\"", html);
        Assert.DoesNotContain($"revoke-{id:N}", html);
        Assert.Equal(AgentCredentialErrorCodes.Revoked, Assert.Single((await AuthenticateAsync(host, key)).Errors).Code);

        var revocation = Assert.Single(await AuditRowsAsync(host), row => row.Operation == "agent_credential.revoked");
        Assert.Equal(ownerId, revocation.InitiatorId);
        Assert.Equal(id, revocation.SubjectId);
    }

    private static Dictionary<string, string> CatalogSync()
    {
        return new Dictionary<string, string>
        {
            ["Form.DisplayName"] = "catalog-sync",
            ["Form.ValidForDays"] = "30",
            ["Form.Types"] = "product, category",
            ["Form.Fields"] = "product.price",
            ["Form.Read"] = "true",
            ["Form.Update"] = "true",
            ["Form.CallsPerHour"] = "120",
            ["Form.LlmSpendPerDay"] = "2.5",
            ["Form.ItemsPerChangeSet"] = "50",
            ["Form.ChangeSetsPerDay"] = "10",
        };
    }

    private static string KeyIn(string html)
    {
        var match = KeyField().Match(html);
        Assert.True(match.Success, "The response shows no key.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static async Task<FluentCMS.Core.Results.Result<AgentCredential>> AuthenticateAsync(AdminHost host, string key)
    {
        await using var scope = host.App.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAgentCredentialDirectory>().AuthenticateAsync(key);
    }

    private static async Task<T> QueryAsync<T>(AdminHost host, Func<FluentCmsDbContext, Task<T>> query)
    {
        await using var scope = host.App.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>());
    }

    private static Task<List<Infrastructure.Audit.AuditEventRow>> AuditRowsAsync(AdminHost host)
    {
        return QueryAsync(host, context => context.AuditEvents.AsNoTracking().ToListAsync());
    }

    [GeneratedRegex("data-testid=\"agent-key\".*?value=\"([^\"]+)\"", RegexOptions.Singleline)]
    private static partial Regex KeyField();
}
