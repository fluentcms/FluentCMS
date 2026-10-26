using System.Text.Json;
using FluentCMS.Core;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Models;
using FluentCMS.Core.Results;
using FluentCMS.Core.Rules;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.Rules;

// RUL-04: the compile job reads only the version its payload names, and its last attempt never leaves the version compiling.
public sealed class RuleCompileJobTests
{
    private static readonly Guid RuleId = Guid.NewGuid();

    [Theory]
    [InlineData("\"1\"")]
    [InlineData("1.5")]
    [InlineData("null")]
    [InlineData(null)]
    public async Task A_version_that_is_not_a_whole_number_is_a_payload_failure(string? version)
    {
        var json = version is null ? $$"""{"ruleId": "{{RuleId}}"}""" : $$"""{"ruleId": "{{RuleId}}", "version": {{version}}}""";

        var result = await RunAsync(new RecordingRuleChecks(), attempt: 1, JsonDocument.Parse(json).RootElement.Clone());

        Assert.Equal("payload_invalid", Assert.Single(result.Errors).Code);
    }

    // An attempt that throws is retried; the last one records the version advisory, so proposing again is what starts it over.
    [Fact]
    public async Task The_last_attempt_of_a_compile_that_throws_leaves_the_version_advisory()
    {
        var rules = new RecordingRuleChecks();
        var payload = JsonSerializer.SerializeToElement(new { ruleId = RuleId, version = 2 });

        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(rules, attempt: 1, payload));
        var abandoned = await RunAsync(rules, attempt: NewJob.DefaultMaxAttempts, payload);

        Assert.Equal(("rule_compile_abandoned", "The model client broke."), (Assert.Single(abandoned.Errors).Code, abandoned.Errors[0].Message));
        var recorded = Assert.Single(rules.Recorded);
        Assert.Equal((RuleId, 2, RuleCompileStatus.Advisory), (recorded.RuleId, recorded.Version, recorded.Outcome.Status));
        Assert.Equal(
            "Checks could not be proposed after every attempt failed, so the rule is advisory until checks are proposed again.",
            recorded.Outcome.Message);
    }

    private static async Task<Result> RunAsync(RecordingRuleChecks rules, int attempt, JsonElement payload)
    {
        var services = new ServiceCollection().AddFluentCmsCore();
        services.AddScoped<IModelGateway, ThrowingGateway>();
        services.AddScoped<IRuleChecks>(_ => rules);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var job = new Job
        {
            Id = Guid.CreateVersion7(),
            Type = RuleCompiler.JobType,
            Payload = payload,
            State = JobState.Running,
            Attempts = attempt,
            MaxAttempts = NewJob.DefaultMaxAttempts,
            CreatedAt = DateTimeOffset.UtcNow,
            ScheduledAt = DateTimeOffset.UtcNow,
        };

        return await scope.ServiceProvider.GetServices<IJobHandler>()
            .Single(handler => handler.Type == RuleCompiler.JobType)
            .RunAsync(new JobContext(job, scope.ServiceProvider, (_, _) => Task.FromResult(true)), CancellationToken.None);
    }

    private sealed class ThrowingGateway : IModelGateway
    {
        public bool IsAvailable(string profile) => true;

        public Task<Result<ModelAnswer>> AskAsync(ModelRequest request, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("The model client broke.");
        }
    }

    // The version is always this job's compile, and what is recorded is kept.
    private sealed class RecordingRuleChecks : IRuleChecks
    {
        public List<(Guid RuleId, int Version, RuleCompileOutcome Outcome)> Recorded { get; } = [];

        public Task<Result<RuleDefinition>> CompilingAsync(Guid ruleId, int version, Guid jobId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Result<RuleDefinition>.Success(new RuleDefinition(RuleKind.VoiceAndStyle, "Never claim best in class.")));
        }

        public Task<Result> RecordAsync(Guid ruleId, int version, Guid jobId, RuleCompileOutcome outcome, CancellationToken cancellationToken = default)
        {
            Recorded.Add((ruleId, version, outcome));
            return Task.FromResult(Result.Success());
        }

        public Task<Result<RuleCompilation>> GetAsync(string slug, int version, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<RuleCompilation>> CompileAsync(string slug, int version, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<RuleCheckProposal>> ConfirmAsync(
            string slug,
            int number,
            CheckDefinition? definition = null,
            string? checkName = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<RuleCheckProposal>> RejectAsync(string slug, int number, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
