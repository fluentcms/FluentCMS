namespace FluentCMS.Tests.Jobs;

// The job handlers in Core. The base class is compiled into the integration tests too, for handlers that live in Infrastructure.
public sealed class CheckRunJobConformanceTests : JobHandlerConformanceTests
{
    protected override string JobType => "checks.run";
}

public sealed class BulkEntryUpdatesConformanceTests : JobHandlerConformanceTests
{
    protected override string JobType => "bulk.entry_updates";
}

public sealed class RetentionPurgeJobConformanceTests : JobHandlerConformanceTests
{
    protected override string JobType => "retention.purge";

    // It purges whatever is past its retention, so it reads nothing from its payload.
    protected override bool ReadsPayload => false;
}

public sealed class RuleCompileJobConformanceTests : JobHandlerConformanceTests
{
    protected override string JobType => "rules.compile";
}
