using FluentCMS.Content;
using FluentCMS.Sites;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Testing;

// Gives each call a scope of its own and sets IOperationContextAccessor.Current for it, the way an entry point does for a
// request. The previous value is put back, so an operation never leaks into the code that called it.
internal sealed class OperationRunner(IServiceProvider services, TimeProvider time, Principal principal, Origin origin, string? clientId, SiteId site, string locale)
{
    public async Task<TResult> RunAsync<TService, TResult>(Func<TService, Task<TResult>> call)
        where TService : notnull
    {
        await using var scope = services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>();
        var previous = accessor.Current;
        accessor.Current = new OperationContext(
            principal,
            origin,
            site,
            locale,
            clientId,
            Guid.CreateVersion7().ToString(),
            time.GetUtcNow());

        try
        {
            return await call(scope.ServiceProvider.GetRequiredService<TService>());
        }
        finally
        {
            accessor.Current = previous;
        }
    }

    public async Task RunAsync<TService>(Func<TService, Task> call)
        where TService : notnull =>
        await RunAsync<TService, bool>(async service =>
        {
            await call(service);
            return true;
        });
}
