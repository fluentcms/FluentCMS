using System.Text.Json.Nodes;
using FluentCMS.Schema;

namespace FluentCMS.Core.Schema.FieldTypes;

internal sealed class UrlFieldType()
    : StringFieldType(FieldTypeNames.Url, [typeof(string), typeof(Uri)], FilterSets.Text, sortable: true, indexable: true)
{
    public override bool TryCoerce(object? value, SchemaField field, out object? result, out string? message) =>
        base.TryCoerce(value is Uri uri ? uri.OriginalString : value, field, out result, out message);

    protected override string? Check(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Host.Length > 0
            ? null
            : "is not an absolute http or https URL";

    protected override void Describe(JsonObject schema) => schema["format"] = "uri";
}
