using Microsoft.AspNetCore.Components;
using System.Collections.Specialized;
using System.Web;

namespace FluentCMS.Web.UI.Services;

public interface ISiteState
{
    public bool Initialized { get; }
    public PageFullDetailResponse? Page { get; }
    public SiteDetailResponse? Site { get; }
    public PageMode Mode { get; }
    public Uri Uri { get; }
    public NameValueCollection QueryString { get; }
}

public enum PageMode
{
    Plugin,
    Page
}

public class SiteState : ISiteState
{
    private readonly SetupManager _setupManager;

    public bool Initialized { get; private set; }
    public PageFullDetailResponse? Page { get; private set; }
    public SiteDetailResponse? Site { get; private set; }
    public PageMode Mode { get; private set; }
    public Uri Uri { get; private set; }
    public NameValueCollection QueryString { get; } = [];

    public SiteState(NavigationManager navigator, PageClient pageClient, SetupManager setupManager)
    {
        _setupManager = setupManager;
        var taskInit = Task.Run(setupManager.IsInitialized);
        Initialized = taskInit.Result;
        Uri = new Uri(navigator.Uri);

        QueryString = HttpUtility.ParseQueryString(Uri.Query);

        if (QueryString["plugin"] != null && Uri.Fragment.ToLowerInvariant().Equals("admin"))
            Mode = PageMode.Plugin;
        else
            Mode = PageMode.Page;

        var taskPage = Task.Run(() => pageClient.GetByPathAsync(Uri.Authority, Uri.LocalPath));
        try
        {
            taskPage.Wait();
            if (taskPage.Result.Data != null)
            {
                Page = taskPage.Result.Data;
                Site = Page.Site;
            }
        }
        catch (Exception)
        {
            //if (Initialized)
            //    navigator.NavigateTo("/error", true);
        }
    }
}
