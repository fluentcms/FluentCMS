using FluentCMS.Web.UI.ApiClients;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using System.Text.RegularExpressions;

namespace FluentCMS.Web.UI.Pages;

public class BasePage : ComponentBase, IDisposable
{
    public const string ATTRIBUTE = "FluentCMS";
    protected CancellationTokenSource CancellationTokenSource = new();
    protected CancellationToken CancellationToken => CancellationTokenSource.Token;
    public AppState? AppState { get; set; }

    [Inject]
    protected IServiceProvider ServiceProvider { get; set; } = default!;

    [Inject]
    protected NavigationManager Navigator { get; set; } = default!;

    [Parameter]
    public string? Route { get; set; }

    [SupplyParameterFromQuery]
    public Guid? PluginId { get; set; }

    [SupplyParameterFromQuery]
    public string? ViewMode { get; set; }

    protected TApiClient GetClient<TApiClient>() where TApiClient : BaseClient
    {
        return (TApiClient)ServiceProvider.GetRequiredService(typeof(TApiClient));
    }

    protected override async Task OnParametersSetAsync()
    {
        AppState ??= new AppState();
        AppState.Host = Navigator.BaseUri.EndsWith("/") ? Navigator.BaseUri.Remove(Navigator.BaseUri.Length - 1) : Navigator.BaseUri;
        AppState.Uri = new Uri(Navigator.Uri);
        AppState.Site = await GetClient<SiteClient>().GetByUrl(AppState.Host, CancellationToken);
        AppState.Layout = AppState.Site?.Layout;

        if (AppState.Site != null)
            AppState.Page = await GetClient<PageClient>().GetByPath(AppState.Site.Id, AppState.Uri.LocalPath, CancellationToken);

        if (AppState.Page != null && AppState.Page.Layout != null)
            AppState.Layout = AppState.Page.Layout;

        AppState.PluginId = PluginId;
        AppState.ViewMode = ViewMode;

        await base.OnParametersSetAsync();
    }

    protected RenderFragment dynamicComponent() => builder =>
    {

        var _body = AppState?.Layout?.Body ?? string.Empty;

        var doc = new HtmlDocument();
        doc.LoadHtml(_body);
        var children = GetChildren(doc.DocumentNode);
        // add children to the dom
        AddChildrenToDom(builder, children);

    };

    private void AddChildrenToDom(RenderTreeBuilder builder, IEnumerable<HtmlNode> children)
    {
        foreach (var child in children)
        {
            // render Inner Content
            if (child.NodeType == HtmlNodeType.Text)
            {
                builder.AddContent(0, child.InnerHtml);
                continue;
            }
            var isDynamicNode = child.Attributes.Any(x => x.Name.Equals(ATTRIBUTE, StringComparison.InvariantCultureIgnoreCase));
            // render static node
            if (child.NodeType == HtmlNodeType.Element && !isDynamicNode)
            {
                builder.OpenRegion(0);
                builder.OpenElement(1, child.Name);
                // add attributes
                foreach (var attribute in child.Attributes)
                {
                    builder.AddAttribute(2, attribute.Name, attribute.Value);
                }
                // add children
                if (child.HasChildNodes)
                    AddChildrenToDom(builder, child.ChildNodes);
                // if does not have child but have content

                builder.CloseElement();
                builder.CloseRegion();
                continue;
            }
            // render dynamic node
            builder.OpenRegion(0);
            // get component Type from Node tag name
            var type = GetType(child.OriginalName);
            if (type != null)
            {
                builder.OpenComponent(1, type);
                // add attributes
                // filter out FluentCMS
                var attributes = child.Attributes.Where(x => !x.Name.Equals(ATTRIBUTE, StringComparison.InvariantCultureIgnoreCase));
                foreach (var attribute in attributes)
                {
                    builder.AddComponentParameter(2, attribute.Name, attribute.Value);
                }
                // add children
                AddChildrenToDom(builder, GetChildren(child));
                builder.CloseComponent();
            }
            builder.CloseRegion();

        }
    }

    private static IEnumerable<HtmlNode> GetChildren(HtmlNode doc)
    {

        // traverse through the document
        return doc.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element);
    }

    private static Type? GetType(string typeName)
    {
        var assembly = typeof(Section).Assembly;
        var typeInfo = assembly.DefinedTypes.FirstOrDefault(x => x.Name == typeName);
        return typeInfo?.AsType();
    }

    private Dictionary<string, string> ParseAttributes(string layout)
    {
        var parameterParserRegex = new Regex("(\\s(?<name>\\w+)=\\\"?(?<value>\\w+)\\\"?)+");
        return new Dictionary<string, string>(parameterParserRegex.Matches(layout).Select(x => new KeyValuePair<string, string>(x.Groups["name"].Value, x.Groups["value"].Value)));
    }

    public void Dispose()
    {
        CancellationTokenSource.Cancel();
        CancellationTokenSource.Dispose();
    }
}
