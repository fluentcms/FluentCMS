using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FluentCMS.Tests.Admin;

// Renders a component the way a static server-rendered page does, which bUnit does not copy in every detail:
// a textarea's value becomes its text and a select's value marks its option selected.
internal static class StaticHtml
{
    public static async Task<string> RenderAsync<TComponent>(IDictionary<string, object?> parameters) where TComponent : IComponent
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters))).ToHtmlString());
    }
}
