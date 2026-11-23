using FluentCMS.Diagnostics;

namespace FluentCMS.Host;

// A previous certificate belongs to a replacement that is under way, and its private key still opens the older keys of the
// ring, so the report names it by thumbprint until it is removed.
internal sealed class PreviousKeyRingCertificatesReport(IReadOnlyList<string> thumbprints, IHostEnvironment environment) : IInsecureDefaultSource
{
    public const string Id = "data-protection-previous-certificates";

    public IEnumerable<InsecureDefault> GetInsecureDefaults()
    {
        if (!environment.IsDevelopment())
        {
            yield return new InsecureDefault
            {
                Id = Id,
                Message = $"The Data Protection key ring is also read with {thumbprints.Count} previous certificates of {ReferenceHost.PreviousKeyRingCertificatesSection} (thumbprints {string.Join(", ", thumbprints)}), which never protect a new key. Remove them once a key of the current certificate is the default, 'fluentcms settings reprotect' ends with exit code 0, and nothing else protected under the older keys is still needed.",
            };
        }
    }
}
