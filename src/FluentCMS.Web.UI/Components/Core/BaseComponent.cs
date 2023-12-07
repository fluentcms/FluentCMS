using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using System.Linq.Expressions;
using static MongoDB.Driver.WriteConcern;

namespace FluentCMS.Web.UI.Components.Core;


public abstract class BaseComponent : ComponentBase
{
    // Child content
    RenderFragment ChildContent { get; set; } = default!;
    // Additional Attributes
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object> AdditionalAttributes { get; set; } = default!;
    // Component Name
    public string GetComponentName() => GetType().Name;
    // Get Classes
    public List<string> GetClasses()
    {
        List<string> classes = new List<string>();

        // get properties
        var properties = GetType().GetProperties().Where(p=>p.CustomAttributes.Any(x=>x.AttributeType == typeof(CssPropertyAttribute)));
        foreach (var property in properties)
        {
            if (property.GetValue(this, null) is var value)
            {
                // get value Type
                var valueType = value.GetType();
                if(valueType == typeof(bool))
                {
                    // check if value is true
                    if ((bool)value)
                    {
                        classes.Add(string.Join(UISettings.Seperator, [UISettings.Prefix, GetComponentName(), property.Name]));
                    }
                }
                else
                {
                    // add class
                    classes.Add(string.Join(UISettings.Seperator, [UISettings.Prefix, GetComponentName(), property.Name, value.ToString()]));
                }
                
            }
        }

        return classes;
    }


}

public abstract class InputBaseComponent<T> : BaseComponent
{
    [Parameter]
    public T Value { get; set; } = default!;
    [Parameter]
    public EventCallback<T> ValueChanged { get; set; }
    [Parameter]
    public Expression<Func<T>>? ValueExpression { get; set; }
    protected EditContext EditContext { get; set; } = default!;
    protected T? CurrentValue
    {
        get => Value;
        set
        {
            _ = ValueChanged.InvokeAsync(Value);
            //EditContext?.NotifyFieldChanged(FieldIdentifier);
        }
    }

}
