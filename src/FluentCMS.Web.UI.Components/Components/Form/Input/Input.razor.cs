namespace FluentCMS.Web.UI.Components;

public partial class Input<TValue>
{
    [Parameter]
    [CSSProperty]
    public InputSize? Size { get; set; }

    [Parameter]
    public InputType Type { get; set; } = InputType.Text;

    public string value
    {
        get
        {
            if (Value == null) return "";

            var type = Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue);

            if (type == typeof(DateTime))
            {
                return ((DateTime)(object)Value).ToString("yyyy-MM-ddTHH:mm:ss");
            }
            else if (type == typeof(DateTimeOffset))
            {
                return ((DateTimeOffset)(object)Value).ToString("yyyy-MM-dd");
            }
            else if (type == typeof(DateOnly))
            {
                return ((DateOnly)(object)Value).ToString("yyyy-MM-dd");
            }
            else if (type == typeof(TimeOnly))
            {
                return ((TimeOnly)(object)Value).ToString("HH:mm:ss");
            }
            else
            {
                return Value.ToString();
            }
        }
    }

    private void OnInput(ChangeEventArgs evt)
    {
        if (evt.Value == null) return;

        TValue val = default!;

        if (!BindConverter.TryConvertTo<TValue>(evt.Value, System.Globalization.CultureInfo.InvariantCulture, out val)) return;

        Value = val;
    }
}