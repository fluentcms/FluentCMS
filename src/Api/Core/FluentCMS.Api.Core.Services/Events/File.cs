using File = FluentCMS.Api.Core.Models.File;

namespace FluentCMS.Api.Core.Services.Events;

public class FileAddedEvent(File file) : EventBase
{
    public File File { get; } = file;
}
public class FileRemovedEvent(File file) : EventBase
{
    public File File { get; } = file;
}

public class FileRenamedEvent(File file) : EventBase
{
    public File File { get; } = file;
}

public class FileMovedEvent(File file) : EventBase
{
    public File File { get; } = file;
}
