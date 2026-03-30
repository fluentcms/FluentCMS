using Ganss.Xss;

namespace FluentCMS.Web.UI;

public partial class PageHead : IAsyncDisposable
{
    [Inject]
    private ViewState ViewState { get; set; } = default!;

    private List<string> Stylesheets { get; set; } = [];

    private static readonly HtmlSanitizer _headSanitizer = CreateHeadSanitizer();

    private static HtmlSanitizer CreateHeadSanitizer()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedTags.Add("meta");
        sanitizer.AllowedTags.Add("link");
        sanitizer.AllowedTags.Add("style");
        sanitizer.AllowedTags.Add("noscript");
        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.Add("name");
        sanitizer.AllowedAttributes.Add("content");
        sanitizer.AllowedAttributes.Add("rel");
        sanitizer.AllowedAttributes.Add("href");
        sanitizer.AllowedAttributes.Add("type");
        sanitizer.AllowedAttributes.Add("media");
        sanitizer.AllowedAttributes.Add("charset");
        sanitizer.AllowedAttributes.Add("property");
        sanitizer.AllowedAttributes.Add("http-equiv");
        sanitizer.AllowedAttributes.Add("sizes");
        sanitizer.AllowedAttributes.Add("hreflang");
        sanitizer.AllowedAttributes.Add("crossorigin");
        sanitizer.AllowedAttributes.Add("integrity");
        sanitizer.AllowedCssProperties.Clear();
        return sanitizer;
    }

    private static string SanitizeHeadContent(string content)
    {
        return _headSanitizer.Sanitize(content);
    }

    private string GetRobots()
    {
        ViewState.Page.Settings.TryGetValue("Index", out var index);
        ViewState.Page.Settings.TryGetValue("Follow", out var follow);

        var result = "";

        if (index == "true")
            result += "index, ";
        else
            result += "noindex, ";


        if (follow == "true")
            result += "follow";
        else
            result += "nofollow";

        return result;
    }

    private string GetSetting(string key)
    {
        ViewState.Page.Settings.TryGetValue(key, out var pageValue);
        ViewState.Site.Settings.TryGetValue(key, out var siteValue);

        return pageValue ?? siteValue ?? string.Empty;
    }

    private static readonly System.Text.RegularExpressions.Regex _googleTagsIdRegex =
        new(@"^[A-Za-z0-9\-]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private string GetGoogleTagsScript()
    {
        var tagId = GetSetting("GoogleTagsId");
        if (string.IsNullOrEmpty(tagId) || !_googleTagsIdRegex.IsMatch(tagId))
            return string.Empty;

        return $"<script async src=\"https://www.googletagmanager.com/gtag/js?id={tagId}\"></script>\n<script>\n\twindow.dataLayer = window.dataLayer || [];\n\tfunction gtag(){{\n\t\tdataLayer.push(arguments);\n\t}}\ngtag('js', new Date())\ngtag('config', '{tagId}');\n</script>";
    }

    private async void OnStateChanged(object? sender, EventArgs e)
    {
        await Load();
    }

    protected override async Task OnInitializedAsync()
    {
        ViewState.OnStateChanged += OnStateChanged;
        await Load();
    }

    private async Task Load()
    {
        Stylesheets = [];

        foreach (var plugin in ViewState.Plugins)
        {
            var assemblyName = plugin.Definition.Assembly.Replace(".dll", "");
            foreach (var stylesheet in plugin.Definition.Stylesheets)
            {
                var stylesheetPath = $"_content/{assemblyName}/{stylesheet.TrimStart('/')}";
                if (!Stylesheets.Contains(stylesheetPath))
                    Stylesheets.Add(stylesheetPath);
            }
        }
        StateHasChanged();
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        ViewState.OnStateChanged -= OnStateChanged;
        await Task.CompletedTask;
    }
}