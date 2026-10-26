using FluentCMS.Core.ChangeSets;
using Microsoft.Extensions.Options;

namespace FluentCMS.Infrastructure.ChangeSets;

public sealed class ApprovalOptions
{
    public const string SectionName = "Approvals";

    // How many people approve a set in review when nothing asks for more (RTE-05).
    public int Required { get; set; } = 1;
}

internal sealed class ApprovalOptionsValidation : IValidateOptions<ApprovalOptions>
{
    public ValidateOptionsResult Validate(string? name, ApprovalOptions options)
    {
        return options.Required is >= 1 and <= ChangeSet.MaxApprovals
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"Approvals__Required is {options.Required}; it must be from 1 to {ChangeSet.MaxApprovals}.");
    }
}
