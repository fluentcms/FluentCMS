using FluentCMS.Core;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Results;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.Jobs;

// ADR-0012, EXT-03: a handler runs as an engine only if Core lists it as that engine's; naming one in RunAs is not enough.
public sealed class EngineJobsTests
{
    [Fact]
    public void A_handler_that_names_no_engine_runs_as_whoever_enqueued_its_job()
    {
        Assert.Null(Engines().RunAs(new Handler(null), new UserInitiator(Guid.NewGuid(), "Amir")).Value);
    }

    [Fact]
    public void The_check_run_runs_as_the_check_engine()
    {
        using var provider = new ServiceCollection().AddFluentCmsCore().BuildServiceProvider();
        var checkRun = provider.GetServices<IJobHandler>().Single(handler => handler.Type == "checks.run");

        Assert.Equal(SystemInitiator.Checks, provider.GetRequiredService<EngineJobs>().RunAs(checkRun, new AgentInitiator(Guid.NewGuid(), "agent")).Value);
    }

    // RUL-04: a rule's compile proposes checks as the check engine, on behalf of the person who saved the rule.
    [Fact]
    public void The_rule_compile_runs_as_the_check_engine()
    {
        using var provider = new ServiceCollection().AddFluentCmsCore().BuildServiceProvider();
        var compile = provider.GetServices<IJobHandler>().Single(handler => handler.Type == "rules.compile");

        Assert.Equal(SystemInitiator.Checks, provider.GetRequiredService<EngineJobs>().RunAs(compile, new UserInitiator(Guid.NewGuid(), "Amir")).Value);
    }

    [Fact]
    public void A_handler_Core_does_not_list_cannot_claim_an_engine()
    {
        var claimed = Engines().RunAs(new Handler(SystemInitiator.Checks), null);

        var error = Assert.Single(claimed.Errors);
        Assert.Equal(JobErrorCodes.EngineNotAllowed, error.Code);
        Assert.Contains("'checks' engine", error.Message);
    }

    // Code running inside an engine's scope, such as a check type, enqueues as that engine; the job still runs only through a
    // handler Core lists for the engine, so the engine is not passed on.
    [Fact]
    public void A_job_an_engine_enqueued_for_a_handler_Core_does_not_list_is_refused()
    {
        var passedOn = Engines().RunAs(new Handler(null), SystemInitiator.Checks);

        Assert.Equal(JobErrorCodes.EngineNotAllowed, Assert.Single(passedOn.Errors).Code);
    }

    private static EngineJobs Engines()
    {
        using var provider = new ServiceCollection().AddFluentCmsCore().BuildServiceProvider();
        return provider.GetRequiredService<EngineJobs>();
    }

    private sealed class Handler(SystemInitiator? runAs) : IJobHandler
    {
        public string Type => "sample.claims";

        public SystemInitiator? RunAs => runAs;

        public Task<Result> RunAsync(JobContext context, CancellationToken cancellationToken) => Task.FromResult(Result.Success());
    }
}
