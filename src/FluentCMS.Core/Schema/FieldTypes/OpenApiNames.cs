namespace FluentCMS.Core.Schema.FieldTypes;

// The names under #/components/schemas that component and block schemas get, shared by the field types that
// point to them and by the OpenAPI document transformers that emit them.
internal static class OpenApiNames
{
    public static string ComponentRef(string component) => $"#/components/schemas/{ComponentSchema(component)}";

    public static string BlockRef(string block) => $"#/components/schemas/{BlockSchema(block)}";

    public static string ComponentSchema(string component) => $"component-{component}";

    public static string BlockSchema(string block) => $"block-{block}";
}
