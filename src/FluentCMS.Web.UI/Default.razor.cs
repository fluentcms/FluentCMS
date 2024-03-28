﻿using FluentCMS.Web.UI.Services;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components.Sections;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Web.UI;

public partial class Default : IDisposable
{
    public const string ATTRIBUTE = "FluentCMS";
    public const string SLOT_ATTRIBUTE = "FluentCMS-Slot";

    public PageFullDetailResponse? Page { get; set; }

    [Inject] public NavigationManager NavigationManager { set; get; } = default!;

    [Inject] public SetupManager SetupManager { set; get; } = default!;

    [Inject] public PageClient PageClient { set; get; } = default!;

    [Inject] public SiteClient SiteClient { set; get; } = default!;

    [Inject] public PluginDefinitionClient PluginDefinitionClient { set; get; } = default!;

    [Inject] public ToastService ToastService { set; get; }

    [Inject] public ILogger<Default> Logger { get; set; }
    [Inject(Key = ErrorMessageExtension.ErrorMessageFactoryKey)] public required Func<Exception, string[]> ErrorMessageFactory { get; set; }

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
            if (type != null && !type.IsSubclassOf(typeof(LayoutComponentBase)))
            {
                builder.OpenComponent(1, type);
                // add attributes
                // filter out FluentCMS
                var attributes = child.Attributes.Where(x =>
                    !x.Name.Equals(ATTRIBUTE, StringComparison.InvariantCultureIgnoreCase));
                foreach (var attribute in attributes)
                {
                    builder.AddComponentParameter(2, attribute.OriginalName, attribute.Value);
                    if (type.GetProperty("Page") != null)
                    {
                        builder.AddComponentParameter(3, "Page", Page);
                    }
                }

                // add children
                // AddChildrenToDom(builder, GetChildren(child));
                // check if has children
                if (child.HasChildNodes)
                {
                    var slots = child.ChildNodes.Where(x => x.Attributes.Any(x => x.Name.Equals(SLOT_ATTRIBUTE, StringComparison.InvariantCultureIgnoreCase)));

                    foreach (var slot in slots)
                    {
                        builder.AddAttribute(2, slot.OriginalName, (RenderFragment)((b) => AddChildrenToDom(b, slot.ChildNodes)));
                    }

                    builder.AddAttribute(3, "ChildContent",
                        (RenderFragment)((b) => AddChildrenToDom(b, child.ChildNodes.Where(x => x.Attributes.All(x => !x.Name.Equals(SLOT_ATTRIBUTE, StringComparison.InvariantCultureIgnoreCase))))));
                }

                builder.CloseComponent();
            }
            else
            {
                builder.OpenComponent(1, typeof(LayoutView));
                builder.AddComponentParameter(2, "Layout", type);
                builder.AddComponentParameter(2, "ChildContent", (RenderFragment)((b) => AddChildrenToDom(b, child.ChildNodes)));
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
        // FluentCMS.Web.UI
        var uiAssembly = typeof(Section).Assembly;
        // FluentCMS.Web.UI.Components
        var componentsAssembly = typeof(BaseComponent).Assembly;

        var typeInfo = uiAssembly.DefinedTypes.Union(componentsAssembly.DefinedTypes)
            .Union([typeof(SectionContent)])
            .FirstOrDefault(x => x.Name == typeName);
        return typeInfo;
    }

    private async Task OnError(Exception ex)
    {
        if (ToastService == null || ToastService.ToastProvider == null)
        {
            Logger.LogError(ex, "Toast Not Initialized");
            return;
        }
        var message = ErrorMessageFactory.Invoke(ex);
        foreach (var error in message)
        {
            ToastService!.ToastProvider!.Show(error, ToastType.Danger);
        }
    }
}
