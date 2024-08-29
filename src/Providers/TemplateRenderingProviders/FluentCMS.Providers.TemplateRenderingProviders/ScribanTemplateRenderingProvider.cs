using FluentCMS.Providers.TemplateRenderingProviders.Abstractions;
using Scriban.Runtime;
using Scriban;

namespace FluentCMS.Providers.TemplateRenderingProviders;

public class ScribanTemplateRenderingProvider : ITemplateRenderingProvider
{
    public string Render(string? content, Dictionary<string, object> keyValues)
    {
        if(string.IsNullOrEmpty(content))
            return string.Empty;

        var scriptObject = new ScriptObject();
        foreach (var keyValue in keyValues)
            scriptObject.Add(keyValue.Key, keyValue.Value);

        var context = new TemplateContext();
        context.PushGlobal(scriptObject);

        var template = Template.Parse(content);
        return template.Render(context);
    }
}
