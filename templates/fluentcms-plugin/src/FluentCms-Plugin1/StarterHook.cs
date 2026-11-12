using FluentCMS.Content;
using FluentCMS.Hooks;
using Microsoft.Extensions.Logging;

namespace FluentCms_Plugin1;

// Runs after an entry of any content type is published; IAfterPublishHook<Article> would run for articles only. A before
// hook can refuse the operation with context.Cancel(code, message).
public sealed partial class StarterHook(ILogger<StarterHook> logger) : IAfterPublishHook<ContentDocument>
{
    public ValueTask AfterPublishAsync(HookContext<ContentDocument> context, CancellationToken cancellationToken)
    {
        Published(logger, context.Operation.Site.Value);
        return ValueTask.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "An entry was published in site {Site}")]
    private static partial void Published(ILogger logger, Guid site);
}
