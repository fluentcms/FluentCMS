namespace FluentCMS.Admin.EndToEnd;

// The violations of the sweep the admin does not fix here, each with its reason and the issue that tracks it (spec 4.18):
// the measure of the audit, which aims to be empty. A view of null covers every view; a target is axe-core's selector of
// the element as it prints it.
public static class AccessibilityExceptions
{
    public static readonly IReadOnlyList<KnownViolation> All =
    [
    ];

    public static bool Cover(string view, string rule, string target) =>
        All.Any(exception => exception.Rule == rule
            && (exception.View is null || exception.View == view)
            && target.Contains(exception.Target, StringComparison.Ordinal));
}

public sealed record KnownViolation(string Rule, string Target, string Reason, string Issue, string? View = null);
