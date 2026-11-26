namespace FluentCMS.Core.DependencyInjection;

// How long a disposal waits for the work it stops, the job runner's loops or the callbacks on a lock's Lost, before it
// returns without them (#3544).
internal static class DisposeWait
{
    public static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);
}
