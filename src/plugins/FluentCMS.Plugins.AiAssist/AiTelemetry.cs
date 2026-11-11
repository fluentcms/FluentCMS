using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluentCMS.Sites;

namespace FluentCMS.Plugins.AiAssist;

// The names of core's one activity source and one meter, so that a host that subscribes to them hears the AI calls too.
// A span and a count name the action, the provider and the outcome, and never the text or the suggestion.
internal static class AiTelemetry
{
    public const string Name = "FluentCMS";
    public const string CallsCounter = "fluentcms.ai.calls";
    public const string OperationTag = "fluentcms.operation";
    public const string ProviderTag = "fluentcms.provider";
    public const string OutcomeTag = "fluentcms.outcome";
    public const string CodeTag = "fluentcms.code";

    private static readonly string Version = typeof(AiTelemetry).Assembly.GetName().Version?.ToString() ?? "1.0.0";
    private static readonly ActivitySource Source = new(Name, Version);
    private static readonly Meter Meter = new(Name, Version);

    private static readonly Counter<long> Calls = Meter.CreateCounter<long>(CallsCounter, unit: "{call}", description: "AI assist calls sent to a provider, by action, provider and outcome.");

    public static System.Diagnostics.Activity? Start(string action, SiteId site, string type, string provider)
    {
        var activity = Source.StartActivity($"ai {action}");
        activity?.SetTag(OperationTag, action);
        activity?.SetTag("fluentcms.site", site.Value.ToString("D"));
        activity?.SetTag("fluentcms.type", type);
        activity?.SetTag(ProviderTag, provider);
        return activity;
    }

    public static void End(System.Diagnostics.Activity? activity, string action, string provider, string outcome, string? code)
    {
        activity?.SetTag(OutcomeTag, outcome);
        if (code is not null)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.SetTag(CodeTag, code);
        }

        Calls.Add(
            1,
            new KeyValuePair<string, object?>(OperationTag, action),
            new KeyValuePair<string, object?>(ProviderTag, provider),
            new KeyValuePair<string, object?>(OutcomeTag, outcome));
    }
}
