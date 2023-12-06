using FluentCMS.Web.UI.ApiClients;
using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Pages;

public class BasePage : ComponentBase, IDisposable
{
    [Inject]
    protected IServiceProvider ServiceProvider { get; set; } = default!;

    protected CancellationTokenSource CancellationTokenSource = new CancellationTokenSource();
    protected CancellationToken CancellationToken => CancellationTokenSource.Token;

    protected TApiClient GetClient<TApiClient>() where TApiClient : BaseClient
    {
        return (TApiClient)ServiceProvider.GetRequiredService(typeof(TApiClient));
    }

    public void Dispose()
    {
        CancellationTokenSource.Cancel();
        CancellationTokenSource.Dispose();
    }
}
