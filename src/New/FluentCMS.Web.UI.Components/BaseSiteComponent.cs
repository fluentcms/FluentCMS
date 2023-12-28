using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Components;

public class BaseSiteComponent : ComponentBase, IDisposable
{

    protected CancellationTokenSource CancellationTokenSource = new();
    protected CancellationToken CancellationToken => CancellationTokenSource.Token;

    public void Dispose()
    {
        CancellationTokenSource.Cancel();
        CancellationTokenSource.Dispose();
    }
}
