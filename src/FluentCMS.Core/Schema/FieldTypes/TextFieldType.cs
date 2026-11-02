namespace FluentCMS.Core.Schema.FieldTypes;

internal sealed class TextFieldType()
    : StringFieldType(FieldTypeNames.Text, [typeof(string)], FilterSets.Text, sortable: true, indexable: true);
