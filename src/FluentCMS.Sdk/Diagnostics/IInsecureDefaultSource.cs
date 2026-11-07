namespace FluentCMS.Diagnostics;

/// <summary>
/// Adds a plugin's own insecure defaults to the startup report. Register an implementation in the service collection; the
/// report asks it once per start, in every environment, so it decides itself where a setting is insecure.
/// </summary>
public interface IInsecureDefaultSource
{
    /// <summary>Lists the insecure defaults that apply now.</summary>
    /// <returns>The insecure defaults, or none.</returns>
    IEnumerable<InsecureDefault> GetInsecureDefaults();
}
