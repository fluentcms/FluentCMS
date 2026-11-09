using Xunit;

namespace FluentCMS.Core.IntegrationTests.Hosting;

// A test that sets a process environment variable is seen by every host that reads the environment while it runs, so such a
// test runs in this collection, after the others and with nothing beside it.
[CollectionDefinition(Collection, DisableParallelization = true)]
public sealed class ProcessEnvironment
{
    public const string Collection = "process environment";
}
