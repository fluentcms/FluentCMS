using FluentCMS.Web.UI.Services;
using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Components;

public class BaseSiteComponent : ComponentBase, IDisposable
{
    protected CancellationTokenSource CancellationTokenSource = new();
    protected CancellationToken CancellationToken => CancellationTokenSource.Token;
    protected bool Initialized { get; set; } = false;
    public PageFullDetailResponse? Page { get; set; }

    [Inject]
    public SiteState? SiteState { get; set; }

    [Inject]
    public SetupManager? SetupManager { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (SetupManager != null)
        {
            Initialized = await SetupManager.IsInitialized();
            if (!Initialized)
                SetupManager.NavigateToSetupRoute();

            if (SiteState != null)
                Page = await SiteState.GetCurrentPage(CancellationToken);
        }
        await base.OnInitializedAsync();
    }

    public void Dispose()
    {
        CancellationTokenSource.Cancel();
        CancellationTokenSource.Dispose();
    }
}
