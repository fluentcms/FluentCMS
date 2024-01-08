using FluentCMS.Web.UI.Services;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Routing;

namespace FluentCMS.Web.UI;

public partial class Default : IDisposable
{
    public const string ATTRIBUTE = "FluentCMS";

    public PageFullDetailResponse? Page { get; set; }

    [Inject]
    public NavigationManager NavigationManager { set; get; } = default!;

    [Inject]
    public SetupManager SetupManager { set; get; } = default!;

    [Inject]
    public PageClient PageClient { set; get; } = default!;

    [Inject]
    public SiteClient SiteClient { set; get; } = default!;

    [Inject]
    public PluginDefinitionClient PluginDefinitionClient { set; get; } = default!;

    [Parameter]
    public string? Route { get; set; }

    protected override void OnInitialized()
    {
        NavigationManager.LocationChanged += LocationChanged;
    }

    void LocationChanged(object sender, LocationChangedEventArgs e)
    {
        StateHasChanged();
    }

    void IDisposable.Dispose()
    {
        NavigationManager.LocationChanged -= LocationChanged;
    }

    protected override async Task OnParametersSetAsync()
    {
        if (!await SetupManager.IsInitialized())
        {
            Page = await SetupManager.GetSetupPage();
            return;
        }

        try
        {
            var pageResponse = await PageClient.GetByUrlAsync(NavigationManager.Uri);
            if (pageResponse.Data != null)
                Page = pageResponse.Data;
        }
        catch
        {
        }
    }

    protected RenderFragment ChildComponents() => builder =>
    {
        if (Page == null)
            return;

        var _body = Page.Layout?.Body ?? string.Empty;

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
                    builder.AddComponentParameter(3, "Page", Page);
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

}
