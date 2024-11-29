using System.Text.RegularExpressions;

namespace FluentCMS.Web.Api.Validation;

public partial class SlugAttribute : ValidationAttribute
{
    [GeneratedRegex(@"^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled)]
    private static partial Regex MyRegex();

    private static readonly Regex _slugRegex = MyRegex();

    protected override ValidationResult IsValid(object? value, ValidationContext validationContext)
    {
        if (value is not string slug)
            return ValidationResult.Success!;

        return !_slugRegex.IsMatch(slug) ?
            new ValidationResult("Invalid slug format. Slug can only contain lowercase letters," +
                                 " numbers, and hyphens, and must start and end with a letter or number.") :
            ValidationResult.Success!;
    }
}
