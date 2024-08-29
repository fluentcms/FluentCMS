namespace FluentCMS.Providers.TemplateRenderingProviders.Abstractions;

public interface ITemplateRenderingProvider
{
    public string Render(string? content, Dictionary<string, object> keyValues);
}
