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
        Assert.Null(Engines().RunAs(new Handler(null)).Value);
    }

    [Fact]
    public void The_check_run_runs_as_the_check_engine()
    {
        using var provider = new ServiceCollection().AddFluentCmsCore().BuildServiceProvider();
        var checkRun = provider.GetServices<IJobHandler>().Single(handler => handler.Type == "checks.run");

        Assert.Equal(SystemInitiator.Checks, provider.GetRequiredService<EngineJobs>().RunAs(checkRun).Value);
    }

    [Fact]
    public void A_handler_Core_does_not_list_cannot_claim_an_engine()
    {
        var claimed = Engines().RunAs(new Handler(SystemInitiator.Checks));

        var error = Assert.Single(claimed.Errors);
        Assert.Equal(JobErrorCodes.EngineNotAllowed, error.Code);
        Assert.Contains("'checks' engine", error.Message);
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
