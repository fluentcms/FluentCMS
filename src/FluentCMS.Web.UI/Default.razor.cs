using FluentCMS.Web.UI.Services;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Sections;
using System.Reflection;

namespace FluentCMS.Web.UI;

public partial class Default : IDisposable
{
    public const string ATTRIBUTE = "FluentCMS";

    public PageFullDetailResponse? Page { get; set; }

    [Inject] public NavigationManager NavigationManager { set; get; } = default!;

    [Inject] public SetupManager SetupManager { set; get; } = default!;

    [Inject] public PageClient PageClient { set; get; } = default!;

    [Inject] public SiteClient SiteClient { set; get; } = default!;

    [Inject] public PluginDefinitionClient PluginDefinitionClient { set; get; } = default!;

    [Parameter] public string? Route { get; set; }

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

            var isDynamicNode =
                child.Attributes.Any(x => x.Name.Equals(ATTRIBUTE, StringComparison.InvariantCultureIgnoreCase));
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
                if (type.IsSubclassOf(typeof(LayoutComponentBase)))
                {
                    builder.OpenComponent(1, typeof(LayoutView));
                    // add layout attribute
                    builder.AddComponentParameter(2, "Layout", type);

                    builder.AddAttribute(4, "ChildContent",
                        (RenderFragment)((b) => AddChildrenToDom(b, child.ChildNodes)));

                    builder.CloseComponent();
                }
                else
                {
                    builder.OpenComponent(1, type);
                    // add attributes
                    // filter out FluentCMS
                    var attributes = child.Attributes.Where(x =>
                        !x.Name.Equals(ATTRIBUTE, StringComparison.InvariantCultureIgnoreCase));



                    foreach (var attribute in attributes)
                    {
                        // cast attributeTypes

                        builder.AddComponentParameter(2, attribute.OriginalName, CastToProperty(attribute.Value, attribute.OriginalName, type));
                        if (type.GetProperties().Any(x => x.Name == "Page"))
                        {
                            builder.AddComponentParameter(3, "Page", Page);
                        }
                    }

                    // add children
                    // AddChildrenToDom(builder, GetChildren(child));
                    // check if has children
                    if (child.HasChildNodes)
                    {
                        var fragments = type.GetProperties().Where(x => x.PropertyType == typeof(RenderFragment)).Select(x => x.Name).ToList();
                        if (!fragments.Any())
                        {
                            fragments = ["ChildContent"];
                        }
                        foreach (var fragment in fragments)
                        {
                            HtmlNodeCollection nodes;
                            if (child.ChildNodes.Any(x => x.OriginalName == fragment))
                            {
                                nodes = child.ChildNodes.Single(x => x.OriginalName == fragment).ChildNodes;
                            }
                            else
                            {
                                nodes = child.ChildNodes;
                            }
                            builder.AddAttribute(2, fragment, (RenderFragment)((b) => AddChildrenToDom(b, nodes)));

                        }
                    }

                    builder.CloseComponent();
                }

            }

            builder.CloseRegion();
        }
    }

    private object? CastToProperty(string value, string originalName, Type type)
    {
        var originalType = type.GetProperty(originalName)?.PropertyType?.FullName ?? "";
        return originalType switch
        {
            "" => value,
            "System.String" => value,
            "FluentCMS.Web.UI.Components.IconName" => Enum.Parse(typeof(IconName), value),
            _ => throw new NotSupportedException($"We Dont support \"{originalType}\" yet")
        };
    }

    private static IEnumerable<HtmlNode> GetChildren(HtmlNode doc)
    {
        // traverse through the document
        return doc.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element);
    }

    private static Type? GetType(string typeName)
    {
        // FluentCMS.Web.UI
        var uiAssembly = typeof(Section).Assembly;
        // FluentCMS.Web.UI.Components
        var componentsAssembly = typeof(BaseComponent).Assembly;

        var types = uiAssembly.DefinedTypes.Union(componentsAssembly.DefinedTypes).ToList();
        types.Add(typeof(SectionContent).GetTypeInfo());
        var typeInfo = types
            .FirstOrDefault(x => x.Name == typeName);
        return typeInfo?.AsType();
    }
}
