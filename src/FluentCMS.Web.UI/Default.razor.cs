using FluentCMS.Web.UI.Plugins.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Scriban;
using Scriban.Runtime;

namespace FluentCMS.Web.UI;

public partial class Default : IDisposable
{
    public const string ATTRIBUTE = "FluentCMS";
    public const string SLOT_ATTRIBUTE = "FluentCMS-Slot";

    public PageFullDetailResponse? Page { get; set; }

    [Parameter]
    public string? Route { get; set; }

    [Inject]
    public NavigationManager NavigationManager { set; get; } = default!;

    [Inject]
    public SetupManager SetupManager { set; get; } = default!;

    [Inject]
    public IHttpClientFactory HttpClientFactory { set; get; } = default!;

    [CascadingParameter]
    public Task<AuthenticationState> AuthenticationStateTask { get; set; } = default!;

    public UserLoginResponse? UserLogin { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        NavigationManager.LocationChanged += LocationChanged;
        UserLogin = await AuthenticationStateTask.GetLogin();
    }
    protected override async Task OnParametersSetAsync()
    {
        // check if setup is not done
        // if not it should be redirected to /setup route
        if (!await SetupManager.IsInitialized() && !NavigationManager.Uri.ToLower().EndsWith("/setup"))
        {
            NavigationManager.NavigateTo("/setup", true);
            return;
        }

        var pageClient = HttpClientFactory.CreateApiClient<PageClient>(UserLogin);
        var pageResponse = await pageClient.GetByUrlAsync(NavigationManager.Uri);

        if (pageResponse.Data != null)
            Page = pageResponse.Data;

        await base.OnParametersSetAsync();
    }

    void LocationChanged(object? sender, LocationChangedEventArgs e)
    {
        StateHasChanged();
    }

    void IDisposable.Dispose()
    {
        NavigationManager.LocationChanged -= LocationChanged;
    }

    protected RenderFragment ChildComponents() => builder =>
    {
        if (Page == null)
            return;

        var _body = Page.Layout?.Body ?? string.Empty;
        _body = GetParsedContent(_body);

        // find [[content]] and split
        var htmlContents = _body.Split("[[content]]");

        if (htmlContents.Length == 1)
        {
            builder.AddContent(0, (MarkupString)htmlContents[0]);
            return;
        }

        var index = 0;
        for (int i = 0; i < htmlContents.Length - 1; i++)
        {
            builder.AddContent(index, (MarkupString)htmlContents[i]);
            index++;
            builder.OpenComponent(index, typeof(Section));
            builder.AddComponentParameter(0, "Name", "Main");
            builder.AddComponentParameter(1, "Page", Page);
            builder.CloseComponent();
            index++;
        }
        builder.AddContent(index, (MarkupString)htmlContents[htmlContents.Length - 1]);
    };

    private string GetParsedContent(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        var scriptObject = new ScriptObject
        {
            ["user"] = new
            {
                username = UserLogin?.UserName ?? string.Empty,
                email = UserLogin?.Email ?? string.Empty
            }
        };

        var context = new TemplateContext();
        context.PushGlobal(scriptObject);

        var template = Template.Parse(content);
        return template.Render(context);
    }

}
