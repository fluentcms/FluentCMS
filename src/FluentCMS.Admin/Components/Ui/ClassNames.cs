namespace FluentCMS.Admin.Components.Ui;

// Joins class fragments the way shadcn's cn() does. Conflict resolution (tailwind-merge) comes with the component set.
public static class ClassNames
{
    public static string Cn(params string?[] classes)
    {
        return string.Join(' ', classes.Where(c => !string.IsNullOrWhiteSpace(c)));
    }
}

public sealed class DialogContext
{
    public string Id { get; } = "dialog-" + Guid.NewGuid().ToString("N")[..8];
    public string TitleId => Id + "-title";
    public string DescriptionId => Id + "-description";
}

public sealed class DropdownMenuContext
{
    public string Id { get; } = "menu-" + Guid.NewGuid().ToString("N")[..8];
    public string TriggerId => Id + "-trigger";
}

public sealed class CommandContext
{
    public string Id { get; } = "command-" + Guid.NewGuid().ToString("N")[..8];
    public string ListId => Id + "-list";
    public string InputId => Id + "-input";
}
