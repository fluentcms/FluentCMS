using FluentCMS.Shared.Extensions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using System.Linq;
using System.Linq.Expressions;
using static MongoDB.Driver.WriteConcern;

namespace FluentCMS.Web.UI.Components.Core;


public abstract class BaseComponent : ComponentBase
{
    // Child content
    [Parameter]
    public RenderFragment ChildContent { get; set; } = default!;
    // Additional Attributes
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object> AdditionalAttributes { get; set; } = default!;
    // Component Name
    public string GetComponentName() => GetType().Name.FromPascalCaseToKebabCase();
    // Get Classes
    public string GetClasses()
    {
        List<string> classes = new List<string>();

        // add component name
        classes.Add(string.Join(UISettings.Seperator, [UISettings.Prefix, GetComponentName()]));

        // add component Bases to ComponentBase
        var parentType = GetType().BaseType;
        while (parentType != typeof(ComponentBase))
        {
            classes.Add(string.Join(UISettings.Seperator, [UISettings.Prefix, parentType.Name.FromPascalCaseToKebabCase()]));
            parentType = parentType.BaseType;
        }

        // get properties
        var properties = GetType().GetProperties().Where(p => p.CustomAttributes.Any(x => x.AttributeType == typeof(CssPropertyAttribute)));
        foreach (var property in properties)
        {
            if (property.GetValue(this, null) is var value)
            {
                if (value == null)
                {
                    continue;
                }
                // get value Type
                var valueType = value.GetType();
                if (valueType == typeof(bool))
                {
                    // check if value is true
                    if ((bool)value)
                    {
                        classes.Add(string.Join(UISettings.Seperator, [UISettings.Prefix, GetComponentName(), property.Name.FromPascalCaseToKebabCase()]));
                    }
                }
                else
                {
                    // add class
                    classes.Add(string.Join(UISettings.Seperator, [UISettings.Prefix, GetComponentName(), property.Name.FromPascalCaseToKebabCase(), value.ToString()]));
                }

            }
        }

        // clean up backticks for generic types
        classes = classes.Select(c => c.Replace("`1", "")).ToList();

        return string.Join(" ", classes);
    }


    public string ClassName(string Name)
    {
        return string.Join(UISettings.Seperator, [UISettings.Prefix, Name.FromPascalCaseToKebabCase()]);
    }
}


