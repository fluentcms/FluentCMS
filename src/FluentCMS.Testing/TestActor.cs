using FluentCMS.Content;
using FluentCMS.Schema;

namespace FluentCMS.Testing;

// Who a call runs as. Every call sets the operation context for that identity and reaches the same services a request would.
public sealed class TestActor
{
    private readonly OperationRunner _runner;

    internal TestActor(IServiceProvider services, TimeProvider time, Principal principal, Origin origin, string? clientId, string locale)
    {
        Principal = principal;
        Origin = origin;
        _runner = new OperationRunner(services, time, principal, origin, clientId, locale);
    }

    public Principal Principal { get; }

    public Origin Origin { get; }

    public IContentService<T> Content<T>()
        where T : ContentItem => new ScopedContentService<T>(_runner);

    public IContentService Content() => new ScopedContentService(_runner);

    public Task<TResult> RunAsync<TService, TResult>(Func<TService, Task<TResult>> call)
        where TService : notnull => _runner.RunAsync(call);
}
