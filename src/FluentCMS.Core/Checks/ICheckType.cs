using System.Text.Json;
using FluentCMS.Core.Extensions;
using FluentCMS.Core.Results;

namespace FluentCMS.Core.Checks;

// A kind of check, such as a term list or a length limit (CHK-01, CHK-06). Built-ins and plugins implement it the same way
// (EXT-02, EXT-N1), and every implementation must pass CheckTypeConformanceTests in tests/FluentCMS.Tests/Checks.
[ExtensionPoint]
public interface ICheckType
{
    // Stored in every check that uses the type, so it never changes once released (CheckNames.IsTypeName).
    string Name { get; }

    CheckKind Kind { get; }

    // Checks the settings object a check gives this type and returns what is wrong, never throwing for bad input. A setting the
    // type does not know is an error with SchemaErrorCodes.SettingUnknown; CheckSettingsReader does that bookkeeping.
    IReadOnlyList<Error> ValidateSettings(JsonElement settings);

    // Only called with settings that passed ValidateSettings. Whatever the content holds, a problem is a failed outcome whose findings
    // say what to change (UX-03), never an exception; a type that cannot decide returns needs review.
    Task<CheckOutcome> EvaluateAsync(CheckInput input, CancellationToken cancellationToken);
}
