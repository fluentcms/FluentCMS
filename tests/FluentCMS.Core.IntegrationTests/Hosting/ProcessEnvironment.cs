namespace FluentCMS.Core.IntegrationTests.Hosting;

// A test that sets a process environment variable is seen by every host that reads the environment while it runs, so the
// classes that build such hosts share a collection and run one after the other.
internal static class ProcessEnvironment
{
    public const string Collection = "process environment";
}
