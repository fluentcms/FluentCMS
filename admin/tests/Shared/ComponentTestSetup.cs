using System.Runtime.CompilerServices;
using Bunit;

namespace FluentCMS.Admin.Testing;

// Compiled into every admin test project that references bUnit (admin/tests/Directory.Build.targets). The component tests
// of a project run in parallel, and a runner has two cores: the thread pool grows too slowly for the waits, which then time
// out before their check has run once, and bUnit's own default of one second is shorter than a render can take there.
internal static class ComponentTestSetup
{
    public static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(10);

    [ModuleInitializer]
    internal static void Initialize()
    {
        ThreadPool.GetMinThreads(out var workers, out var io);
        ThreadPool.SetMinThreads(Math.Max(workers, Environment.ProcessorCount * 4), io);
        BunitContext.DefaultWaitTimeout = WaitTimeout;
    }
}
