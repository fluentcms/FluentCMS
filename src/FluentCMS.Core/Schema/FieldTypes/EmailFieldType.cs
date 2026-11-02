using System.Net.Mail;
using System.Text.Json.Nodes;

namespace FluentCMS.Core.Schema.FieldTypes;

internal sealed class EmailFieldType()
    : StringFieldType(FieldTypeNames.Email, [typeof(string)], FilterSets.Text, sortable: true, indexable: true)
{
    private const int MaxAddressLength = 254;

    protected override string? Check(string text)
    {
        var isAddress = text.Length <= MaxAddressLength
            && !text.Any(char.IsWhiteSpace)
            && MailAddress.TryCreate(text, out var address)
            && address.DisplayName.Length == 0
            && string.Equals(address.Address, text, StringComparison.OrdinalIgnoreCase);

        return isAddress ? null : "is not a valid email address";
    }

    protected override void Describe(JsonObject schema) => schema["format"] = "email";
}
