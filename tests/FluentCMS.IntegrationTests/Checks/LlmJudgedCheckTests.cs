using System.Net;
using System.Text;
using System.Text.Json;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Models;
using FluentCMS.Infrastructure.Models;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.IntegrationTests.Checks.CheckScenario;

namespace FluentCMS.IntegrationTests.Checks;

// LLM-judged checks reach the model only through the gateway (ADR-0008), and their examples gate activation as any check's do.
public sealed class LlmJudgedCheckTests
{
    private static readonly Dictionary<string, string?> Model = new()
    {
        ["Models:Profiles:checks:Provider"] = "openai",
        ["Models:Profiles:checks:Model"] = "gpt-fake",
        ["Models:Profiles:checks:ApiKey"] = "sk-test",
        ["Models:Profiles:checks:InputCostPerMillionTokens"] = "1",
        ["Models:Profiles:checks:OutputCostPerMillionTokens"] = "4",
        ["Outbound:AllowedHosts:0"] = "api.openai.com",
    };

    private static readonly CheckDefinition CalmVoice = new(
        "tone",
        "Product copy is calm and factual.",
        Parse("""{"voice": "Calm and factual, no hype and no exclamation marks."}"""),
        [
            Example("calm copy", CheckVerdict.Pass, """{"headline": "A kettle that boils in two minutes"}"""),
            Example("shouting", CheckVerdict.Fail, """{"headline": "BEST KETTLE EVER!!!"}"""),
        ]);

    // MDL-03: with no model an LLM-judged check is stored, but it cannot be activated, since its examples cannot be decided.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Without_a_model_an_LLM_judged_check_cannot_be_activated(string provider)
    {
        await using var scenario = await CheckScenario.CreateAsync(provider);
        await scenario.CreateAsync("calm-voice", CalmVoice);

        var refused = await scenario.RunAsync(directory => directory.ActivateAsync("calm-voice", 1));

        Assert.Equal(
            [
                "Example 'calm copy' could not be decided: No model is configured for LLM-judged checks, so a person reviews what the 'tone' check would have judged.",
                "Example 'shouting' could not be decided: No model is configured for LLM-judged checks, so a person reviews what the 'tone' check would have judged.",
            ],
            refused.Errors.Select(error => error.Message));
        Assert.Empty(await scenario.QueryAsync(context => context.ModelCalls.ToListAsync()));
    }

    // CHK-05: each example the model judged is a recorded call, with the prompt that held the example.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task With_a_model_its_examples_are_judged_through_the_gateway_and_recorded(string provider)
    {
        var judge = new JudgingProvider();
        await using var scenario = await CheckScenario.CreateAsync(provider, Model, services => services.AddSingleton(new ModelTransport(() => judge)));
        await scenario.CreateAsync("calm-voice", CalmVoice);

        var activated = await scenario.RunAsync(directory => directory.ActivateAsync("calm-voice", 1));

        Assert.True(activated.IsSuccess, string.Join("; ", activated.Errors.Select(error => error.Message)));
        var calls = await scenario.QueryAsync(context => context.ModelCalls.AsNoTracking().OrderBy(call => call.Id).ToListAsync());
        Assert.Equal(2, calls.Count);
        Assert.All(calls, call => Assert.Equal(("checks", "check:tone", ModelCallOutcome.Succeeded, "user", (Guid?)Amir.UserId),
            (call.Profile, call.Purpose, call.Outcome, call.InitiatorKind, call.InitiatorId)));
        Assert.Contains("A kettle that boils in two minutes", calls[0].Prompt);
        Assert.Contains("BEST KETTLE EVER!!!", calls[1].Prompt);
        Assert.Equal(2, judge.Requests);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_LLM_judged_check_whose_examples_the_model_gets_wrong_is_not_activated(string provider)
    {
        var judge = new JudgingProvider();
        await using var scenario = await CheckScenario.CreateAsync(provider, Model, services => services.AddSingleton(new ModelTransport(() => judge)));
        await scenario.CreateAsync("calm-voice", new CheckDefinition(
            "tone",
            "Product copy is calm.",
            Parse("""{"voice": "Calm."}"""),
            [
                Example("looks calm", CheckVerdict.Pass, """{"headline": "Calm!!!"}"""),
                Example("looks loud", CheckVerdict.Fail, """{"headline": "Loud"}"""),
            ]));

        var refused = await scenario.RunAsync(directory => directory.ActivateAsync("calm-voice", 1));

        Assert.Equal(
            [
                "Example 'looks calm' should pass, but the check failed it: Drop the exclamation marks.",
                "Example 'looks loud' should fail, but the check passed it.",
            ],
            refused.Errors.Select(error => error.Message));
    }

    // Answers like the OpenAI chat API: content with "!!!" fails at /headline, anything else passes.
    private sealed class JudgingProvider : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            var body = JsonSerializer.Deserialize<JsonElement>(await request.Content!.ReadAsStringAsync(cancellationToken));
            var user = body.GetProperty("messages").EnumerateArray().Last().GetProperty("content").GetString()!;
            var answer = user.Contains("!!!", StringComparison.Ordinal)
                ? """{"verdict": "fail", "findings": [{"pointer": "/headline", "excerpt": "!!!", "message": "Drop the exclamation marks."}]}"""
                : """{"verdict": "pass", "findings": []}""";
            var json = JsonSerializer.Serialize(new
            {
                id = "chatcmpl-1",
                @object = "chat.completion",
                created = 1760000000,
                model = "gpt-fake-2026-01-01",
                choices = new[] { new { index = 0, message = new { role = "assistant", content = answer }, finish_reason = "stop" } },
                usage = new { prompt_tokens = 400, completion_tokens = 40, total_tokens = 440 },
            });

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
