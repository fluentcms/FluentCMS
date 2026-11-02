using FluentCMS.Content;

namespace FluentCMS.Core.Schema.FieldTypes;

internal sealed class LongTextFieldType()
    : StringFieldType(FieldTypeNames.LongText, [typeof(string)], [FilterOperator.Contains, FilterOperator.StartsWith, .. FilterSets.NullChecks], sortable: false, indexable: false);
