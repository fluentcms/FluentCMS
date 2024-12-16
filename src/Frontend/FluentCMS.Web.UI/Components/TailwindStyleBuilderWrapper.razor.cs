using Microsoft.JSInterop;

namespace FluentCMS.Web.UI;

public partial class TailwindStyleBuilderWrapper
{
    [Inject]
    private ViewState ViewState { get; set; } = default!;

    public string Config => @"
        {
            ""darkMode"": ""class"",
            ""theme"": {
                ""extend"": {
                    ""colors"": {
                        ""primary"": {
                            ""50"": ""hsl(var(--f-primary-50, var(--f-primary), 100%, 95%))"",
                            ""100"": ""hsl(var(--f-primary-100, var(--f-primary), 80%, 90%))"",
                            ""200"": ""hsl(var(--f-primary-200, var(--f-primary), 70%, 80%))"",
                            ""300"": ""hsl(var(--f-primary-300, var(--f-primary), 60%, 70%))"",
                            ""400"": ""hsl(var(--f-primary-400, var(--f-primary), 60%, 60%))"",
                            ""500"": ""hsl(var(--f-primary-500, var(--f-primary), 60%, 50%))"",
                            ""600"": ""hsl(var(--f-primary-600, var(--f-primary), 80%, 40%))"",
                            ""700"": ""hsl(var(--f-primary-700, var(--f-primary), 80%, 30%))"",
                            ""800"": ""hsl(var(--f-primary-800, var(--f-primary), 80%, 20%))"",
                            ""900"": ""hsl(var(--f-primary-900, var(--f-primary), 90%, 15%))""
                        },
                        ""surface"": {
                            ""DEFAULT"": ""hsl(var(--f-surface))"",
                            ""muted"": ""hsl(var(--f-surface-muted))"",
                            ""accent"": ""hsl(var(--f-surface-accent))""
                        },
                        ""content"": {
                            ""DEFAULT"": ""hsl(var(--f-content))"",
                            ""muted"": ""hsl(var(--f-content-muted))"",
                            ""accent"": ""hsl(var(--f-content-accent))""
                        },
                        ""border"": {
                            ""DEFAULT"": ""hsl(var(--f-border))"",
                            ""muted"": ""hsl(var(--f-border-muted))"",
                            ""accent"": ""hsl(var(--f-border-accent))""
                        }
                    }
                }
            }
        }";

    private async Task OnCssGenerated(string css)
    {
        var cssFilePath = Path.Combine("wwwroot", "tailwind", ViewState.Site.Id.ToString(), $"{ViewState.Page.Id}.css");

        var directoryPath = Path.GetDirectoryName(cssFilePath);
        if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        await File.WriteAllTextAsync(cssFilePath, css);
    }
}
