using FluentCMS.Core.DependencyInjection;

namespace FluentCMS.Core.ProseMirror;

// FluentCms:RichText.
public sealed class RichTextOptions
{
    public const string Section = "RichText";

    private SafeUrl? _links;

    // The URL schemes a link in rich text may have, in place of http, https, mailto and tel; a URL with no scheme is
    // always allowed. A write refuses a link with any other, and a read and the renderers leave a stored one out.
    public IList<string> LinkSchemes { get; } = [];

    internal SafeUrl Links => _links ??= LinkSchemes.Count == 0 ? SafeUrl.Default : new SafeUrl(LinkSchemes);

    internal void Validate()
    {
        foreach (var scheme in LinkSchemes)
        {
            if (string.IsNullOrEmpty(scheme) || !char.IsAsciiLetter(scheme[0]) || !scheme.All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '-' or '.'))
            {
                throw new FluentCmsConfigurationException(HostErrorCodes.InvalidOption, $"{nameof(LinkSchemes)} has '{scheme}', which is not a URL scheme; give it without the colon, as 'sms'.");
            }

            if (SafeUrl.ForbiddenSchemes.Contains(scheme, StringComparer.OrdinalIgnoreCase))
            {
                throw new FluentCmsConfigurationException(HostErrorCodes.InvalidOption, $"{nameof(LinkSchemes)} has '{scheme}', which can run script or reach local content and is never allowed.");
            }
        }
    }
}
