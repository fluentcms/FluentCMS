using FluentCMS.Core;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Models;
using FluentCMS.Core.Results;
using FluentCMS.Core.Rules;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.Rules;

// RUL-04, MDL-03: a rule is compiled to proposed checks through the gateway, and without a usable answer it is advisory, not an error.
public sealed class RuleCompilerTests
{
    private static readonly UserInitiator Amir = new(Guid.NewGuid(), "Amir");

    private static readonly RuleDefinition NoSuperlatives = new(
        RuleKind.VoiceAndStyle,
        "Never claim best in class.",
        CheckSeverity.Warn,
        new RuleScope(["product-page"], ["headline"], ["en"]));

    private const string TermsCheck = """
        {"type": "terms", "description": "Product copy never claims best in class.", "settings": "{\"terms\": [\"best in class\"]}",
         "examples": [
           {"name": "plain claim", "expect": "pass", "contentType": "", "fields": "{\"headline\": \"A kettle that boils in two minutes\"}"},
           {"name": "superlative", "expect": "fail", "contentType": "product-page", "fields": "{\"headline\": \"The best in class kettle\"}"}]}
        """;

    // The rule's text and scope reach the model only as data; the proposal takes the rule's severity and scope.
    [Fact]
    public async Task A_voice_and_style_rule_is_compiled_to_a_terms_check_with_its_examples()
    {
        var gateway = new FakeGateway($$"""{"checks": [{{TermsCheck}}]}""");

        var outcome = await CompileAsync(gateway, NoSuperlatives);

        Assert.Equal((RuleCompileStatus.Proposed, (string?)null, "gpt-fake", "hash-1"), (outcome.Status, outcome.Message, outcome.Model, outcome.PromptHash));
        var proposal = Assert.Single(outcome.Proposals);
        Assert.Equal(("terms", "Product copy never claims best in class.", CheckSeverity.Warn), (proposal.Type, proposal.Description, proposal.Severity));
        Assert.Equal(["product-page"], proposal.Scope.ContentTypes!);
        Assert.Equal(["headline"], proposal.Scope.Fields!);
        Assert.Equal("""{"terms": ["best in class"]}""", proposal.Settings.GetRawText());
        Assert.Equal(
            [("plain claim", CheckVerdict.Pass, "product-page"), ("superlative", CheckVerdict.Fail, "product-page")],
            proposal.Examples.Select(example => (example.Name, example.Expected, example.ContentType)));

        var request = Assert.Single(gateway.Requests);
        Assert.Equal((RuleCompiler.Profile, "rule:compile", (Initiator?)Amir), (request.Profile, request.Purpose, request.OnBehalfOf));
        Assert.Equal(
            [("rule", "Never claim best in class."), ("scope", """{"kind":"voice_and_style","contentTypes":["product-page"],"fields":["headline"]}""")],
            request.Data.Select(section => (section.Name, section.Text)));
        Assert.DoesNotContain("best in class", request.Instructions, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(request.AnswerSchema);
    }

    [Fact]
    public async Task Without_a_model_the_rule_is_advisory_and_no_call_is_made()
    {
        var gateway = new FakeGateway("{}") { Available = false };

        var outcome = await CompileAsync(gateway, NoSuperlatives);

        Assert.Equal((RuleCompileStatus.Advisory, RuleCompiler.NoModel), (outcome.Status, outcome.Message));
        Assert.Empty(outcome.Proposals);
        Assert.Empty(gateway.Requests);
    }

    [Fact]
    public async Task A_failed_call_leaves_the_rule_advisory_with_the_reason()
    {
        var gateway = new FakeGateway(null);

        var outcome = await CompileAsync(gateway, NoSuperlatives);

        Assert.Equal(RuleCompileStatus.Advisory, outcome.Status);
        Assert.Equal("The model could not propose checks, so the rule is advisory until checks are proposed again: The provider did not answer.", outcome.Message);
    }

    [Theory]
    [InlineData("Here are the checks.")]
    [InlineData("""{"checks": "terms"}""")]
    [InlineData("""{"checks": [], "advice": "none"}""")]
    [InlineData("""{"checks": [], "checks": []}""")]
    public async Task An_answer_that_cannot_be_read_leaves_the_rule_advisory(string answer)
    {
        var outcome = await CompileAsync(new FakeGateway(answer), NoSuperlatives);

        Assert.Equal(
            (RuleCompileStatus.Advisory, "The model's answer could not be read, so the rule is advisory until checks are proposed again."),
            (outcome.Status, outcome.Message));
    }

    [Fact]
    public async Task An_answer_in_a_code_fence_is_read()
    {
        var outcome = await CompileAsync(new FakeGateway($$"""
            ```json
            {"checks": [{{TermsCheck}}]}
            ```
            """), NoSuperlatives);

        Assert.Equal("terms", Assert.Single(outcome.Proposals).Type);
    }

    // A proposal of a type that is not installed or not offered, without a failing example, or with settings that are not an object is
    // left out.
    [Fact]
    public async Task A_proposal_the_installed_check_types_cannot_use_is_left_out()
    {
        const string unknownType = """{"type": "sentiment", "description": "Upbeat.", "settings": "{}", "examples": []}""";
        const string notOffered = """
            {"type": "links", "description": "Links work.", "settings": "{}",
             "examples": [
               {"name": "no link", "expect": "pass", "contentType": "", "fields": "{\"headline\": \"A kettle\"}"},
               {"name": "bad link", "expect": "fail", "contentType": "", "fields": "{\"headline\": \"http://\"}"}]}
            """;
        const string onlyPasses = """
            {"type": "terms", "description": "No hype.", "settings": "{\"terms\": [\"hype\"]}",
             "examples": [{"name": "calm", "expect": "pass", "contentType": "", "fields": "{\"headline\": \"A kettle\"}"}]}
            """;
        const string settingsList = """{"type": "terms", "description": "No hype.", "settings": "[\"hype\"]", "examples": []}""";

        var some = await CompileAsync(new FakeGateway($$"""{"checks": [{{unknownType}}, {{TermsCheck}}, {{onlyPasses}}]}"""), NoSuperlatives);
        var none = await CompileAsync(new FakeGateway($$"""{"checks": [{{unknownType}}, {{settingsList}}, {{notOffered}}]}"""), NoSuperlatives);

        Assert.Equal((RuleCompileStatus.Proposed, "2 checks more that the model proposed could not be used and were left out."), (some.Status, some.Message));
        Assert.Equal("terms", Assert.Single(some.Proposals).Type);
        Assert.Equal(
            (RuleCompileStatus.Advisory, "The model proposed 3 checks that could not be used, so the rule is advisory until checks are proposed again."),
            (none.Status, none.Message));
    }

    [Fact]
    public async Task A_rule_no_check_can_enforce_is_advisory()
    {
        var rule = new RuleDefinition(RuleKind.PermissionAndRouting, "Legal reviews every claim about safety.");

        var outcome = await CompileAsync(new FakeGateway("""{"checks": []}"""), rule);

        Assert.Equal(
            (RuleCompileStatus.Advisory, "The model found no check that can enforce this rule, so it is advisory: agents read it as guidance and it blocks nothing."),
            (outcome.Status, outcome.Message));
    }

    [Fact]
    public async Task At_most_three_checks_are_proposed()
    {
        var outcome = await CompileAsync(new FakeGateway($$"""{"checks": [{{TermsCheck}}, {{TermsCheck}}, {{TermsCheck}}, {{TermsCheck}}]}"""), NoSuperlatives);

        Assert.Equal((RuleCompiler.MaxProposals, (string?)null), (outcome.Proposals.Count, outcome.Message));
    }

    private static async Task<RuleCompileOutcome> CompileAsync(FakeGateway gateway, RuleDefinition rule)
    {
        var services = new ServiceCollection().AddFluentCmsCore();
        services.AddScoped<IModelGateway>(_ => gateway);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<RuleCompiler>().ProposeAsync(rule, Amir);
    }

    // Answers every request with the text it was given, or fails when given none.
    private sealed class FakeGateway(string? answer) : IModelGateway
    {
        public bool Available { get; init; } = true;

        public List<ModelRequest> Requests { get; } = [];

        public bool IsAvailable(string profile)
        {
            return Available && profile == RuleCompiler.Profile;
        }

        public Task<Result<ModelAnswer>> AskAsync(ModelRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(answer is null
                ? Result<ModelAnswer>.Failure(ModelErrorCodes.CallFailed, "The provider did not answer.")
                : Result<ModelAnswer>.Success(new ModelAnswer(Guid.NewGuid(), "openai", "gpt-fake", answer, "hash-1", 0.01m)));
        }
    }
}
