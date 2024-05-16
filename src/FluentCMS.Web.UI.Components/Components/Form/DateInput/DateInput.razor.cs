﻿namespace FluentCMS.Web.UI.Components;

public partial class DateInput<TValue>
{
    [Parameter]
    [CSSProperty]
    public InputSize? Size { get; set; }

    public override string GetDefaultCSSName()
    {
        return "DateInput";
    }
}