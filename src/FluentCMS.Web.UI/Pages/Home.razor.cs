using FluentCMS.Api.Models;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using System.Text.RegularExpressions;
using System.Web;

namespace FluentCMS.Web.UI.Pages;
public partial class Home
{
    public const string ATTRIBUTE = "FluentCMS";

    [Parameter]
    public string? Route { get; set; }

    [SupplyParameterFromQuery]
    public Guid? PluginId { get; set; }

    [SupplyParameterFromQuery]
    public string? ViewMode { get; set; }

    private AppState? AppState { get; set; }
    protected override Task OnParametersSetAsync()
    {
        if (AppState == null)
            AppState = new AppState();

        AppState.Host = Navigator.BaseUri.EndsWith("/") ? Navigator.BaseUri.Remove(Navigator.BaseUri.Length - 1) : Navigator.BaseUri;
        AppState.Uri = new Uri(Navigator.Uri);

        var siteResult = http.GetFromJsonAsync<ApiResult<SiteResponse>>($"Site/GetByUrl?url={AppState.Host}").GetAwaiter().GetResult();
        AppState.Site = siteResult?.Data;
        AppState.Layout = AppState.Site?.Layout;

        if (AppState.Site != null)
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            query["siteId"] = AppState.Site.Id.ToString();
            query["path"] = AppState.Uri.LocalPath;

            var pageResult = http.GetFromJsonAsync<ApiResult<PageResponse>>($"Page/GetByPath?{query}").GetAwaiter().GetResult();
            AppState.Page = pageResult?.Data;
            if (AppState.Page != null && AppState.Page.Layout != null)
                AppState.Layout = AppState.Page.Layout;
        }

        AppState.PluginId = PluginId;
        AppState.ViewMode = ViewMode;

        return base.OnParametersSetAsync();
    }

    RenderFragment dynamicComponent() => builder =>
    {

        var _body = AppState?.Layout?.Body ?? string.Empty;

        var doc = new HtmlDocument();
        doc.LoadHtml(_body);
        var children = GetChildren(doc.DocumentNode);
        // add children to the dom
        AddChildrenToDom(builder, children);

    };

    private static void AddChildrenToDom(RenderTreeBuilder builder, IEnumerable<HtmlNode> children)
    {
        foreach (var child in children)
        {
            // render Inner Content
            if(child.NodeType == HtmlNodeType.Text)
            {
                builder.AddContent(0,child.InnerHtml);
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
}
