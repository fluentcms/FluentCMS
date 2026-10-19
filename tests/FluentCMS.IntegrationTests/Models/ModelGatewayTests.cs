using System.Text;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Models;
using FluentCMS.Core.Persistence;
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

    // A provider that can hold an answer to its schema is asked to, so a check's answer comes back in the shape it asked for.
    [Fact]
    public async Task An_answer_schema_reaches_the_provider_as_a_strict_json_schema()
    {
        await using var scenario = await ModelScenario.CreateAsync(DatabaseProviders.Sqlite);
        var schema = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(
            """{"type": "object", "properties": {"verdict": {"type": "string"}}, "required": ["verdict"], "additionalProperties": false}""");

        await scenario.AskAsync(Tone with { AnswerSchema = schema });

        var body = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(Assert.Single(scenario.Providers.Requests).Body);
        var format = body.GetProperty("response_format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.True(format.GetProperty("json_schema").GetProperty("strict").GetBoolean());
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
            ["Models:Profiles:rogue:Free"] = "true",
        });
        var gateway = scenario.Services.CreateScope().ServiceProvider.GetRequiredService<IModelGateway>();
        var rogue = scenario.Services.GetRequiredService<IModelProfiles>().Find("rogue")!;

        var refused = await scenario.AskAsync(Tone with { Profile = "rogue" });
        var direct = await Record.ExceptionAsync(() => scenario.Services.GetRequiredService<IModelClients>().Create(rogue).GetResponseAsync("Hello"));

        Assert.False(gateway.IsAvailable("rogue"));
        Assert.Equal(ModelErrorCodes.EndpointNotAllowed, Assert.Single(refused.Errors).Code);
        Assert.NotNull(direct);
        Assert.Contains("Requests to https://models.evil.test are not allowed", direct.ToString());
        Assert.Empty(scenario.Providers.Requests);
        Assert.Empty(await scenario.CallRowsAsync());
    }

    // SEC-04: the spend cap counts the calls on record for the period, and fails closed once it is reached.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_is_refused_once_its_calls_in_the_period_leave_no_room_for_another(string provider)
    {
        await using var scenario = await ModelScenario.CreateAsync(provider);
        var profile = scenario.Services.GetRequiredService<IModelProfiles>().Find("openai")!;
        var reserved = profile.Cost(Encoding.UTF8.GetByteCount(ModelPrompt.Build(Tone.Instructions, Tone.Data).Text), profile.MaxOutputTokens);

        // Each call costs 0.012 and reserves more, so two fit and a third does not.
        var agent = await scenario.IssueAgentAsync(spendCap: 0.018m + reserved);
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

    // SEC-04: without prices every call costs nothing and no spend cap refuses one, so an endpoint off this machine needs them unless
    // it charges nothing. A prompt is never purged while its call waits for the answer (AUD-04).
    [Fact]
    public void A_profile_off_this_machine_needs_prices_unless_it_is_free_and_retention_outlasts_every_timeout()
    {
        using var services = TestServices.Build(new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] = "Data Source=unused",
            ["Models:PromptRetention"] = "00:00:30",
            ["Models:Profiles:checks:Provider"] = "openai",
            ["Models:Profiles:checks:Model"] = "gpt-fake",
            ["Models:Profiles:checks:InputCostPerMillionTokens"] = "1",
            ["Models:Profiles:free:Provider"] = "openai",
            ["Models:Profiles:free:Model"] = "gpt-fake",
            ["Models:Profiles:free:Endpoint"] = "https://models.example.com/v1",
            ["Models:Profiles:free:Free"] = "true",
            ["Models:Profiles:local:Provider"] = "openai",
            ["Models:Profiles:local:Model"] = "llama-fake",
            ["Models:Profiles:local:Endpoint"] = "http://localhost:11434/v1",
            ["Models:Profiles:local:Timeout"] = "00:02:00",
        });

        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IModelProfiles>());

        Assert.Equal(
            [
                "Models:Profiles:checks: set InputCostPerMillionTokens and OutputCostPerMillionTokens in US dollars, so spend caps count "
                + "what its calls cost; set Free to true only if the endpoint charges nothing.",
                "Models:PromptRetention must be longer than the longest profile Timeout, 00:02:00, so no call is purged while it waits for its answer.",
            ],
            exception.Failures);
    }

    // AUD-04: a call purged while it waits for its answer stays purged when the answer comes; its outcome and cost still count.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_call_purged_while_it_waits_stays_purged_when_it_is_answered(string provider)
    {
        await using var scenario = await ModelScenario.CreateAsync(provider, new() { ["Models:PromptRetention"] = "30.00:00:00" });
        scenario.Providers.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var asking = scenario.AskAsync(Tone);
        while ((await scenario.CallRowsAsync()).Count == 0)
        {
            await Task.Delay(20);
        }

        scenario.Clock.Now = scenario.Clock.Now.AddDays(31);
        var purged = await scenario.RunAsync(services => services.GetRequiredService<IModelCallLog>().PurgeAsync());
        scenario.Providers.Gate.SetResult();
        await asking;

        var row = Assert.Single(await scenario.CallRowsAsync());
        Assert.Equal(1, purged.Value);
        Assert.Equal((ModelCallOutcome.Succeeded, (string?)null, (string?)null, 0.012m), (row.Outcome, row.Prompt, row.Response, row.Cost));
        Assert.NotNull(row.PurgedAt);
    }

    // A provider can take a minute to answer, which no unit of work may hold the database for; the call is refused before it is sent.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_call_inside_a_unit_of_work_is_refused_before_it_is_sent(string provider)
    {
        await using var scenario = await ModelScenario.CreateAsync(provider);

        var exception = await scenario.RunAsync(async services =>
        {
            await using var work = await services.GetRequiredService<IUnitOfWork>().BeginAsync();
            return await Record.ExceptionAsync(() => services.GetRequiredService<IModelGateway>().AskAsync(Tone));
        });

        Assert.Contains("outside a unit of work", Assert.IsType<InvalidOperationException>(exception).Message);
        Assert.Empty(scenario.Providers.Requests);
        Assert.Empty(await scenario.CallRowsAsync());
    }

    // Each attempt the SDK made on its own would be a call the record and the spend cap never saw, so there is one request per call.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_provider_error_is_one_request_on_record_at_its_reserved_cost_and_its_detail_stays_there(string provider)
    {
        await using var scenario = await ModelScenario.CreateAsync(provider);

        var failed = await scenario.AskAsync(Tone with { Profile = "broken" });

        var row = Assert.Single(await scenario.CallRowsAsync());
        var error = Assert.Single(failed.Errors);
        Assert.Equal(ModelErrorCodes.CallFailed, error.Code);
        Assert.Equal($"The call to the 'broken' model failed; model call {row.Id} records the provider's error.", error.Message);
        Assert.Single(scenario.Providers.Requests);
        Assert.Equal(ModelCallOutcome.Failed, row.Outcome);
        Assert.Contains("secret-123", row.Error);
        Assert.True(row.Cost >= 1024 * 20 / 1_000_000m, $"{row.Cost} is less than the output the call could have used.");
    }

    // The profile's key is the only credential Anthropic receives, whatever the host's environment holds.
    [Fact]
    public async Task The_Anthropic_wiring_ignores_a_token_in_the_environment()
    {
        var previous = Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN");
        Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", "token-from-the-environment");
        try
        {
            await using var scenario = await ModelScenario.CreateAsync(DatabaseProviders.Sqlite);

            Assert.True((await scenario.AskAsync(Tone with { Profile = "anthropic" })).IsSuccess);

            var request = Assert.Single(scenario.Providers.Requests);
            Assert.Equal("sk-test-anthropic", request.Headers["x-api-key"]);
            Assert.DoesNotContain(request.Headers, header => header.Value.Contains("token-from-the-environment", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", previous);
        }
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
            ["Models:Profiles:Risk_Scoring:Provider"] = "anthropic",
            ["Models:Profiles:Risk_Scoring:Model"] = "claude-fake",
            ["Models:Profiles:Risk_Scoring:Endpoint"] = "http://models.example.com",
            ["Models:Profiles:Risk_Scoring:MaxOutputTokens"] = "0",
            ["Models:Profiles:Risk_Scoring:Temperature"] = "3",
            ["Models:Profiles:Risk_Scoring:Timeout"] = "2.00:00:00",
        });

        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IModelProfiles>());

        Assert.Equal(
            [
                "Models:Profiles:checks:Provider must be one of openai, anthropic.",
                "Models:Profiles:checks:Model must name the model as the provider lists it, in at most 128 characters.",
                "Models:Profiles:checks:Endpoint must be an absolute https URL, or http to this machine, such as http://localhost:11434/v1.",
                "Models:Profiles:checks: token prices cannot be negative.",
                "Models:Profiles:Risk_Scoring: a profile is named in lowercase words joined by hyphens, such as risk-scoring.",
                "Models:Profiles:Risk_Scoring:ApiKey is required for Anthropic, so the SDK never falls back to credentials it finds on the machine.",
                "Models:Profiles:Risk_Scoring:Endpoint must be an absolute https URL, or http to this machine, such as http://localhost:11434/v1.",
                "Models:Profiles:Risk_Scoring: set InputCostPerMillionTokens and OutputCostPerMillionTokens in US dollars, so spend caps count "
                + "what its calls cost; set Free to true only if the endpoint charges nothing.",
                "Models:Profiles:Risk_Scoring:MaxOutputTokens must be from 1 to 200000.",
                "Models:Profiles:Risk_Scoring:Temperature must be from 0 to 2, or left out for the provider's default.",
                "Models:Profiles:Risk_Scoring:Timeout must be from 1 second to 1 hour, such as 00:01:00.",
            ],
            exception.Failures);
    }
}
