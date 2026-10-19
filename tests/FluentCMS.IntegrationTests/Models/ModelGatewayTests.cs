using FluentCMS.Core.Identity;
using FluentCMS.Core.Models;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using static FluentCMS.IntegrationTests.Models.ModelScenario;

namespace FluentCMS.IntegrationTests.Models;

public sealed class ModelGatewayTests
{
    // MDL-02: two provider families and a local server, each through its real wiring, against an in-process fake of its API.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Each_provider_family_and_a_local_server_answer_and_are_recorded(string provider)
    {
        await using var scenario = await ModelScenario.CreateAsync(provider);

        var openAi = (await scenario.AskAsync(Tone)).Value;
        var local = (await scenario.AskAsync(Tone with { Profile = "local" })).Value;
        var anthropic = (await scenario.AskAsync(Tone with { Profile = "anthropic" })).Value;

        Assert.Equal(("openai", "gpt-fake-2026-01-01", """{"verdict": "pass"}""", 0.012m), (openAi.Provider, openAi.Model, openAi.Text, openAi.Cost));
        Assert.Equal(("openai", "llama-fake:8b", 0m), (local.Provider, local.Model, local.Cost));
        Assert.Equal(("anthropic", "claude-fake-20260101", """{"verdict": "fail"}""", 0.009m), (anthropic.Provider, anthropic.Model, anthropic.Text, anthropic.Cost));

        var requests = scenario.Providers.Requests.ToList();
        Assert.Equal(
            ["https://api.openai.com/v1/chat/completions", "http://localhost:11434/v1/chat/completions", "https://api.anthropic.com/v1/messages"],
            requests.Select(request => request.Uri.GetLeftPart(UriPartial.Path)));
        Assert.Equal("Bearer sk-test-openai", requests[0].Headers["authorization"]);
        Assert.Equal("sk-test-anthropic", requests[2].Headers["x-api-key"]);
        Assert.All(requests, request => Assert.Contains("The best kettle ever made.", request.Body));

        var rows = await scenario.CallRowsAsync();
        Assert.Equal(["openai", "local", "anthropic"], rows.Select(row => row.Profile));
        Assert.Equal(["gpt-fake-2026-01-01", "llama-fake:8b", "claude-fake-20260101"], rows.Select(row => row.ModelVersion));
        Assert.Equal([(1000L, 100L), (1000L, 100L), (2000L, 200L)], rows.Select(row => (row.InputTokens, row.OutputTokens)));
        Assert.All(rows, row => Assert.Equal((ModelCallOutcome.Succeeded, "user", (Guid?)Amir.UserId), (row.Outcome, row.InitiatorKind, row.InitiatorId)));
        Assert.Equal([0.012m, 0m, 0.009m], rows.Select(row => row.Cost));
    }

    // CHK-05: an audit event's promptHash leads back to the verbatim prompt and response.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_prompt_is_found_again_by_its_hash(string provider)
    {
        await using var scenario = await ModelScenario.CreateAsync(provider);
        var answer = (await scenario.AskAsync(Tone)).Value;

        var calls = await scenario.RunAsync(services => services.GetRequiredService<IModelCallLog>().FindByPromptHashAsync(answer.PromptHash));

        var call = Assert.Single(calls);
        var prompt = ModelPrompt.Build(Tone.Instructions, Tone.Data);
        Assert.Equal((answer.CallId, prompt.Hash, prompt.Text, """{"verdict": "pass"}"""), (call.Id, call.PromptHash, call.Prompt, call.Response));
        Assert.Empty(await scenario.RunAsync(services => services.GetRequiredService<IModelCallLog>().FindByPromptHashAsync("0000")));
    }

    // SEC-05: the HTTP client of a provider refuses a host that is not allowed, whatever asked it to go there.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_request_to_a_host_that_is_not_allowed_never_leaves_the_process(string provider)
    {
        await using var scenario = await ModelScenario.CreateAsync(provider, new()
        {
            ["Models:Profiles:rogue:Provider"] = "openai",
            ["Models:Profiles:rogue:Endpoint"] = "https://models.evil.test/v1",
            ["Models:Profiles:rogue:Model"] = "gpt-fake",
        });
        var gateway = scenario.Services.CreateScope().ServiceProvider.GetRequiredService<IModelGateway>();
        var rogue = scenario.Services.GetRequiredService<IModelProfiles>().Find("rogue")!;

        var refused = await scenario.AskAsync(Tone with { Profile = "rogue" });
        var direct = await Record.ExceptionAsync(() => scenario.Services.GetRequiredService<IModelClients>().Create(rogue).GetResponseAsync("Hello"));

        Assert.False(gateway.IsAvailable("rogue"));
        Assert.Equal(ModelErrorCodes.EndpointNotAllowed, Assert.Single(refused.Errors).Code);
        Assert.NotNull(direct);
        Assert.Contains("Requests to models.evil.test are not allowed", direct.ToString());
        Assert.Empty(scenario.Providers.Requests);
        Assert.Empty(await scenario.CallRowsAsync());
    }

    // SEC-04: the spend cap counts the calls on record for the period, and fails closed once it is reached.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_is_refused_once_its_calls_in_the_period_reach_its_spend_cap(string provider)
    {
        await using var scenario = await ModelScenario.CreateAsync(provider);
        var agent = await scenario.IssueAgentAsync(spendCap: 0.02m);
        scenario.Initiator = new AgentInitiator(agent.Id, agent.DisplayName);

        var first = await scenario.AskAsync(Tone);
        var second = await scenario.AskAsync(Tone);
        var third = await scenario.AskAsync(Tone);
        scenario.Clock.Now = scenario.Clock.Now.AddDays(1).AddMinutes(1);
        var nextDay = await scenario.AskAsync(Tone);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(ModelErrorCodes.SpendCapExceeded, Assert.Single(third.Errors).Code);
        Assert.True(nextDay.IsSuccess);
        Assert.Equal(3, scenario.Providers.Requests.Count);
        var rows = await scenario.CallRowsAsync();
        Assert.All(rows, row => Assert.Equal((agent.Id, "agent", (Guid?)agent.Id), (row.AgentCredentialId!.Value, row.InitiatorKind, row.InitiatorId)));
        var log = (IServiceProvider services) => services.GetRequiredService<IModelCallLog>();
        Assert.Equal(0.036m, await scenario.RunAsync(services => log(services).SpendAsync(agent.Id, scenario.Clock.Now.AddDays(-2))));
        Assert.Equal(0.012m, await scenario.RunAsync(services => log(services).SpendAsync(agent.Id, scenario.Clock.Now.AddDays(-1))));
    }

    // AUD-04, AUD-N2: a purge keeps the record of each old call but not its text, and says in the audit log what it purged.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_purge_clears_prompts_past_the_retention_and_logs_each_one(string provider)
    {
        await using var scenario = await ModelScenario.CreateAsync(provider, new() { ["Models:PromptRetention"] = "30.00:00:00" });
        var old = (await scenario.AskAsync(Tone)).Value;
        scenario.Clock.Now = scenario.Clock.Now.AddDays(31);
        var recent = (await scenario.AskAsync(Tone with { Profile = "anthropic" })).Value;

        scenario.Initiator = new AgentInitiator(Guid.NewGuid(), "cleaner");
        var byAgent = await scenario.RunAsync(services => services.GetRequiredService<IModelCallLog>().PurgeAsync());
        scenario.Initiator = Amir;
        var purged = await scenario.RunAsync(services => services.GetRequiredService<IModelCallLog>().PurgeAsync());
        var again = await scenario.RunAsync(services => services.GetRequiredService<IModelCallLog>().PurgeAsync());

        Assert.Equal(ModelErrorCodes.PeopleOnly, Assert.Single(byAgent.Errors).Code);
        Assert.Equal(1, purged.Value);
        Assert.Equal(0, again.Value);
        var rows = await scenario.CallRowsAsync();
        Assert.Equal((old.CallId, (string?)null, (string?)null), (rows[0].Id, rows[0].Prompt, rows[0].Response));
        Assert.Equal(scenario.Clock.Now, rows[0].PurgedAt);
        Assert.Equal((0.012m, "gpt-fake-2026-01-01", old.PromptHash), (rows[0].Cost, rows[0].ModelVersion, rows[0].PromptHash));
        Assert.Equal(recent.CallId, rows[1].Id);
        Assert.NotNull(rows[1].Prompt);
        Assert.Null(rows[1].PurgedAt);

        var purge = Assert.Single(await scenario.PurgeEventsAsync());
        Assert.Equal(("model_call", old.CallId, old.PromptHash, "gpt-fake-2026-01-01", Amir.UserId), (purge.SubjectType, purge.SubjectId!.Value, purge.PromptHash, purge.Model, purge.InitiatorId));
    }

    [Fact]
    public void Without_profiles_there_is_no_model()
    {
        using var services = TestServices.Build(
            new Dictionary<string, string?> { ["Database:ConnectionString"] = "Data Source=unused" },
            collection => collection.AddSingleton<ICurrentInitiator>(new TestInitiator(Amir)));
        using var scope = services.CreateScope();

        Assert.Empty(services.GetRequiredService<IModelProfiles>().All);
        Assert.False(scope.ServiceProvider.GetRequiredService<IModelGateway>().IsAvailable("checks"));
    }

    [Fact]
    public void A_profile_the_host_cannot_use_stops_it_with_what_to_change()
    {
        using var services = TestServices.Build(new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] = "Data Source=unused",
            ["Models:Profiles:checks:Provider"] = "gemini",
            ["Models:Profiles:checks:Endpoint"] = "ftp://models.example.com",
            ["Models:Profiles:checks:InputCostPerMillionTokens"] = "-1",
        });

        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IModelProfiles>());

        Assert.Equal(
            [
                "Models:Profiles:checks:Provider must be one of openai, anthropic.",
                "Models:Profiles:checks:Model must name the model, such as the one the provider lists.",
                "Models:Profiles:checks:Endpoint must be an absolute http or https URL, such as http://localhost:11434/v1.",
                "Models:Profiles:checks: token prices cannot be negative.",
            ],
            exception.Failures);
    }
}
