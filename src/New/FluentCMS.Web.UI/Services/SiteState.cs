using Microsoft.AspNetCore.Components;
using System.Collections.Specialized;
using System.Web;

namespace FluentCMS.Web.UI.Services;

public class SiteState
{
    public bool Initialized { get; }
    public Uri Uri { get; }
    public NameValueCollection QueryString { get; } = [];

    private readonly SetupManager _setupManager;
    private readonly NavigationManager _navigator;
    private readonly PageClient _pageClient;

    public SiteState(NavigationManager navigator, PageClient pageClient, SetupManager setupManager)
    {
        _setupManager = setupManager;
        _navigator = navigator;
        _pageClient = pageClient;
        var taskInit = Task.Run(setupManager.IsInitialized);
        Initialized = taskInit.Result;
        Uri = new Uri(navigator.Uri);
        QueryString = HttpUtility.ParseQueryString(Uri.Query);
    }

    public async Task<PageFullDetailResponse?> GetCurrentPage(CancellationToken cancellationToken = default)
    {
        if (!Initialized)
            throw new AppException(ExceptionCodes.SetupSettingsNotInitialized);

        try
        {
            var pageResponse = await _pageClient.GetByPathAsync(Uri.Authority, Uri.LocalPath, cancellationToken);
            if (pageResponse.Data != null)
                return pageResponse.Data;

            return null;
        }
        catch
        {
            throw new AppException(ExceptionCodes.PageNotFound);
        }
    }
}
