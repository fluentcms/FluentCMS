using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Components.Core;

public abstract class BaseComponent : ComponentBase
{
    public const string PREFIX = "f-";

    [Parameter, EditorRequired]
    public RenderFragment ChildContent { get; set; } = default!;

    [Parameter]
    public string? CssClass { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public virtual Dictionary<string, object?> Attributes { get; set; } = [];

    protected override void OnParametersSet()
    {
        if (Attributes.TryGetValue("class", out var classValue))
        {
            var classString = classValue?.ToString();
            if (!string.IsNullOrWhiteSpace(classString))
            {
                classNames.AddRange(classString.Split(' '));
            }
        }
        base.OnParametersSet();
    }

    private string? GetClasses()
    {
        var classes = new List<string> { PREFIX + "base-component" };

        if (!string.IsNullOrWhiteSpace(CssClass))
            classes.Add(CssClass);

        foreach (var className in classNames)
        {
            classes.Add(className);
        }

        return string.Join(" ", classes);

    }
}
