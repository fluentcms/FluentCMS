using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using FluentCMS.Settings;

namespace FluentCMS.Core.Api.Account;

// The look of the hosted pages, in the settings area "branding" (settings.branding.manage). The pages belong to no site, so
// they read the instance defaults. The colours go into the pages' style sheet, so they are hex colours and nothing else.
[Settings("branding")]
public sealed partial class BrandingSettings : IValidatableObject
{
    public const string DefaultPrimaryColor = "#2563eb";
    public const string DefaultBackgroundColor = "#f4f4f5";
    public const string DefaultTextColor = "#18181b";

    private const string ColorPattern = "^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$";

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = "FluentCMS";

    // An https URL, or a path on this host such as /media/logo.svg. Without one the pages show the name.
    [StringLength(2048)]
    public string? LogoUrl { get; set; }

    [Required]
    [RegularExpression(ColorPattern)]
    public string PrimaryColor { get; set; } = DefaultPrimaryColor;

    [Required]
    [RegularExpression(ColorPattern)]
    public string BackgroundColor { get; set; } = DefaultBackgroundColor;

    [Required]
    [RegularExpression(ColorPattern)]
    public string TextColor { get; set; } = DefaultTextColor;

    public static bool IsColor(string? value) => value is not null && Color().IsMatch(value);

    public static bool IsLogoUrl(string? value) =>
        value is not null
        && (value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal) && !value.Contains('\\', StringComparison.Ordinal)
            || Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrEmpty(LogoUrl) && !IsLogoUrl(LogoUrl))
        {
            yield return new ValidationResult("The logo is an https URL or a path on this host that starts with a single slash.", [nameof(LogoUrl)]);
        }
    }

    [GeneratedRegex(ColorPattern, RegexOptions.CultureInvariant)]
    private static partial Regex Color();
}
