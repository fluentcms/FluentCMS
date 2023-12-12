using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Components.Application;

public class BaseAppComponent : ComponentBase, IDisposable
{

    protected CancellationTokenSource CancellationTokenSource = new();
    protected CancellationToken CancellationToken => CancellationTokenSource.Token;

    [Inject]
    public AppState AppState { get; set; } = default!;

    public void Dispose()
    {
        CancellationTokenSource.Cancel();
        CancellationTokenSource.Dispose();
    }
}
