namespace FluentCMS.Admin.Components.Ui;

// shadcn's cva(): base classes, the classes for each value of each variant, and the values used when none is chosen.
// Classes() merges them with a Class parameter last, so the caller's classes win. Compound variants come when a component needs them.
//
//   cva("base", { variants: { size: { sm: "h-8" } }, defaultVariants: { size: "sm" } })
//   new Variants("base", new() { ["size"] = new() { ["sm"] = "h-8" } }, new() { ["size"] = "sm" })
public sealed class Variants(string baseClasses, Dictionary<string, Dictionary<string, string>> variants, Dictionary<string, string>? defaults = null)
{
    public string Classes(Dictionary<string, string?>? chosen = null, string? className = null)
    {
        var classes = new List<string?> { baseClasses };
        foreach (var (name, values) in variants)
        {
            // An unknown value adds nothing, as in cva.
            var value = chosen?.GetValueOrDefault(name) ?? defaults?.GetValueOrDefault(name);
            if (value is not null && values.TryGetValue(value, out var valueClasses))
            {
                classes.Add(valueClasses);
            }
        }
        classes.Add(className);
        return ClassNames.Cn([.. classes]);
    }
}
