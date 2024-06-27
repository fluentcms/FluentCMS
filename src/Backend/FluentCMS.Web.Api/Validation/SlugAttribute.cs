using System.Text.RegularExpressions;

namespace FluentCMS.Web.Api.Validation;

public class SlugAttribute : ValidationAttribute
{
    public static readonly Regex SlugRegex = new(@"^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled);

    protected override ValidationResult IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null || value is not string slug)
        {
            return ValidationResult.Success!;
        }

        if (!SlugRegex.IsMatch(slug))
        {
            return new ValidationResult("Invalid slug format. Slug can only contain lowercase letters, numbers, and hyphens, and must start and end with a letter or number.");
        }

        return ValidationResult.Success!;
    }
}
