using FluentCMS.Web.UI.Plugins.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Scriban;
using Scriban.Runtime;
using System.Text.RegularExpressions;

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

    private Type GetTypeByName(string typeName)
    {
        var type = GetType().Assembly.GetType(typeName);

        return type ??
            throw new Exception($"Component type {typeName} not found");
    }

    protected RenderFragment ChildComponents() => builder =>
    {
        if (Page == null)
            return;

        var _body = Page.Layout?.Body ?? string.Empty;
        _body = GetParsedContent(_body);

        var componentTree = GetFluentCMSAttributes(_body);

        var index = 0;
        var start_index = 0;
        for (int i = 0; i < componentTree.Count; i++)
        {
            var htmlMarkup = _body.Substring(start_index, int.Parse(componentTree[i]["start_index"]) - start_index);
            builder.AddContent(index, (MarkupString)htmlMarkup);

            index++;
            var component = componentTree[i];
            builder.OpenComponent(index, GetTypeByName(component["_type"]));

            var attributeIndex = 0;
            foreach (var attribute in component)
            {
                if (attribute.Key == "_type" || attribute.Key == "start_index" || attribute.Key == "end_index")
                    continue;
                builder.AddComponentParameter(attributeIndex, attribute.Key, attribute.Value);
                attributeIndex++;
            }

            builder.CloseComponent();

            index++;
            start_index = int.Parse(component["end_index"]) + 1;
        }

        componentTree[componentTree.Count - 1]["end_index"] = _body.Length.ToString();

        //// find [[content]] and split
        //var htmlContents = _body.Split("[[content]]");

        //if (htmlContents.Length == 1)
        //{
        //    builder.AddContent(0, (MarkupString)htmlContents[0]);
        //    return;
        //}

        //var index = 0;
        //for (int i = 0; i < htmlContents.Length - 1; i++)
        //{
        //    builder.AddContent(index, (MarkupString)htmlContents[i]);
        //    index++;
        //    builder.OpenComponent(index, typeof(Section));
        //    builder.AddComponentParameter(0, "Name", "Main");
        //    builder.AddComponentParameter(1, "Page", Page);
        //    builder.CloseComponent();
        //    index++;
        //}
        //builder.AddContent(index, (MarkupString)htmlContents[htmlContents.Length - 1]);
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
    public static List<Dictionary<string, string>> GetFluentCMSAttributes(string htmlContent)
    {
        var result = new List<Dictionary<string, string>>();

        // Regular expression to find tags with fluentcms attribute
        var tagPattern = new Regex(@"<(?<tag>\w+)(?<attributes>[^>]*?\sfluentcms\s[^>]*?)\/?>", RegexOptions.IgnoreCase);

        // Regular expression to extract attributes
        var attributePattern = new Regex(@"(?<name>\w+)=['""](?<value>[^'""]*)['""]", RegexOptions.IgnoreCase);

        var matches = tagPattern.Matches(htmlContent);

        foreach (Match match in matches)
        {
            var tagName = match.Groups["tag"].Value;
            var attributesString = match.Groups["attributes"].Value;

            var attributes = new Dictionary<string, string>
            {
                { "_type", tagName }
            };

            var attributeMatches = attributePattern.Matches(attributesString);

            foreach (Match attributeMatch in attributeMatches)
            {
                attributes[attributeMatch.Groups["name"].Value] = attributeMatch.Groups["value"].Value;
            }

            //// Adding the "fluentcms" attribute as its presence is what we are checking
            //if (!attributes.ContainsKey("fluentcms"))
            //{
            //    attributes["fluentcms"] = string.Empty;
            //}

            // Add the start and end indices
            attributes["start_index"] = match.Index.ToString();
            attributes["end_index"] = (match.Index + match.Length - 1).ToString();

            result.Add(attributes);
        }

        return result;
    }

    //public static List<Dictionary<string, string>> GetFluentCMSAttributes(string htmlContent)
    //{
    //    var result = new List<Dictionary<string, string>>();

    //    // Regular expression to find tags with fluentcms attribute
    //    var tagPattern = new Regex(@"<(?<tag>\w+)(?<attributes>[^>]*?\sfluentcms\s[^>]*?)\/?>", RegexOptions.IgnoreCase);

    //    // Regular expression to extract attributes
    //    var attributePattern = new Regex(@"(?<name>\w+)=['""](?<value>[^'""]*)['""]", RegexOptions.IgnoreCase);

    //    var matches = tagPattern.Matches(htmlContent);

    //    foreach (Match match in matches)
    //    {
    //        var attributesString = match.Groups["attributes"].Value;

    //        var attributes = new Dictionary<string, string>();
    //        var attributeMatches = attributePattern.Matches(attributesString);

    //        foreach (Match attributeMatch in attributeMatches)
    //        {
    //            attributes[attributeMatch.Groups["name"].Value] = attributeMatch.Groups["value"].Value;
    //        }

    //        //// Adding the "fluentcms" attribute as its presence is what we are checking
    //        //if (!attributes.ContainsKey("fluentcms"))
    //        //{
    //        //    attributes["fluentcms"] = string.Empty;
    //        //}

    //        result.Add(attributes);
    //    }

    //    return result;
    //}

}
