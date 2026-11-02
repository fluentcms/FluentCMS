using FluentCMS.Content;
using FluentCMS.Schema;
using FluentCMS.Sites;

namespace FluentCMS.Testing;

// Who a call runs as. Every call sets the operation context for that identity and reaches the same services a request would.
public sealed class TestActor
{
    private readonly IServiceProvider _services;
    private readonly TimeProvider _time;
    private readonly string? _clientId;
    private readonly OperationRunner _runner;

    internal TestActor(IServiceProvider services, TimeProvider time, Principal principal, Origin origin, string? clientId, SiteId site, string locale)
    {
        _services = services;
        _time = time;
        _clientId = clientId;
        Principal = principal;
        Origin = origin;
        Site = site;
        Locale = locale;
        _runner = new OperationRunner(services, time, principal, origin, clientId, site, locale);
    }

    public Principal Principal { get; }

    public Origin Origin { get; }

    public SiteId Site { get; }

    public string Locale { get; }

    public TestActor In(string locale) => new(_services, _time, Principal, Origin, _clientId, Site, locale);

    public TestActor In(SiteId site) => new(_services, _time, Principal, Origin, _clientId, site, Locale);

    public IContentService<T> Content<T>()
        where T : ContentItem => new ScopedContentService<T>(_runner);

    public IContentService Content() => new ScopedContentService(_runner);

    public Task<TResult> RunAsync<TService, TResult>(Func<TService, Task<TResult>> call)
        where TService : notnull => _runner.RunAsync(call);
}
