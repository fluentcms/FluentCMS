namespace FluentCMS.Admin.Components.Ui;

// shadcn's cn(): joins class fragments, skipping empty ones, and merges them so a later class replaces an earlier one that
// sets the same property (TailwindMerge). A component's Class parameter goes last, so it overrides the component's classes.
public static class ClassNames
{
    public static string Cn(params string?[] classes)
    {
        return TailwindMerge.Merge(string.Join(' ', classes.Where(c => !string.IsNullOrWhiteSpace(c))));
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

public sealed class PopoverContext
{
    public string Id { get; } = "popover-" + Guid.NewGuid().ToString("N")[..8];
    public string TriggerId => Id + "-trigger";
}

public sealed class TooltipContext
{
    public string Id { get; } = "tooltip-" + Guid.NewGuid().ToString("N")[..8];
}

// Tab values are the page's own words, so ids are numbered per value instead of being built from them.
public sealed class TabsContext
{
    private readonly Dictionary<string, int> _numbers = [];

    public string Id { get; } = "tabs-" + Guid.NewGuid().ToString("N")[..8];
    public string? DefaultValue { get; set; }

    public string TriggerId(string value) => $"{Id}-{Number(value)}-trigger";
    public string ContentId(string value) => $"{Id}-{Number(value)}-content";

    private int Number(string value)
    {
        if (!_numbers.TryGetValue(value, out var number))
        {
            number = _numbers.Count;
            _numbers[value] = number;
        }
        return number;
    }
}

public sealed class RadioGroupContext
{
    public string GeneratedName { get; } = "radio-group-" + Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = string.Empty;
    public string? Value { get; set; }
}

public sealed class FormFieldContext
{
    public string Id { get; } = "field-" + Guid.NewGuid().ToString("N")[..8];
    public string LabelId => Id + "-label";
    public string DescriptionId => Id + "-description";
    public string ErrorId => Id + "-error";
    public bool HasDescription { get; set; }
    public bool HasError { get; set; }

    public string? DescribedBy => (HasDescription, HasError) switch
    {
        (true, true) => DescriptionId + " " + ErrorId,
        (true, false) => DescriptionId,
        (false, true) => ErrorId,
        _ => null,
    };
}
