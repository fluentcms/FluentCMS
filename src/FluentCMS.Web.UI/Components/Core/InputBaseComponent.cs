using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq.Expressions;

namespace FluentCMS.Web.UI.Components.Core;

public abstract class InputBaseComponent<T> : BaseComponent
{
    [Parameter]
    public T? Value { get; set; }
    [Parameter]
    public EventCallback<T> ValueChanged { get; set; }
    [Parameter]
    public Expression<Func<T>>? Expression { get; set; }
    // current value to invoke ValueChanged
    protected T? CurrentValue
    {
        get => Value;
        set
        {
            Value = value;
            ValueChanged.InvokeAsync(value);
        }
    }
}

