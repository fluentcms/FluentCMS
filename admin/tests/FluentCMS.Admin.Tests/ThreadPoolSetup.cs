using System.Runtime.CompilerServices;

namespace FluentCMS.Admin.Tests;

// Component tests wait on thread pool threads while they run in parallel; on a runner with few cores the pool grows too slowly
// and a wait can time out before its check has run once.
internal static class ThreadPoolSetup
{
    [ModuleInitializer]
    internal static void RaiseMinThreads()
    {
        ThreadPool.GetMinThreads(out var workers, out var io);
        ThreadPool.SetMinThreads(Math.Max(workers, Environment.ProcessorCount * 4), io);
    }
}
