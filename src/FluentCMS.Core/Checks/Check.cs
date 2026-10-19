using FluentCMS.Core.Results;

namespace FluentCMS.Core.Checks;

// A check of the site under a name, at one of its versions (CHK-02). Revising makes the next version and leaves the active one as
// it is, so what runs is always a version whose examples passed when it was activated.
public sealed class Check
{
    private Check(Guid id, Guid siteId, string name, int version, int latestVersion, int? activeVersion, CheckDefinition definition)
    {
        Id = id;
        SiteId = siteId;
        Name = name;
        Version = version;
        LatestVersion = latestVersion;
        ActiveVersion = activeVersion;
        Definition = definition;
    }

    public Guid Id { get; }

    public Guid SiteId { get; }

    public string Name { get; }

    // The version this definition belongs to.
    public int Version { get; }

    public int LatestVersion { get; }

    // The version that runs; null while the check is not active.
    public int? ActiveVersion { get; }

    public CheckDefinition Definition { get; }

    public static Result<Check> Create(Guid siteId, string name, CheckDefinition definition, CheckTypeRegistry checkTypes)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(checkTypes);

        var errors = new List<Error>();

        if (siteId == Guid.Empty)
        {
            errors.Add(new Error(CheckErrorCodes.SiteMissing, "A check belongs to a site, and the site id is empty."));
        }

        if (!CheckNames.IsName(name))
        {
            errors.Add(new Error(
                CheckErrorCodes.NameInvalid,
                $"The name '{name}' must be lowercase letters and digits in words joined by single hyphens, such as 'no-superlatives'."));
        }

        errors.AddRange(definition.Validate(checkTypes).Errors);

        return errors.Count == 0
            ? Result<Check>.Success(new Check(Guid.CreateVersion7(), siteId, name, 1, 1, null, definition))
            : Result<Check>.Failure(errors);
    }

    // Only called on the latest version, which the next one follows.
    public Result<Check> Revise(CheckDefinition definition, CheckTypeRegistry checkTypes)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(checkTypes);

        var valid = definition.Validate(checkTypes);
        return valid.IsSuccess
            ? Result<Check>.Success(new Check(Id, SiteId, Name, LatestVersion + 1, LatestVersion + 1, ActiveVersion, definition))
            : Result<Check>.Failure(valid.Errors);
    }

    public Check WithActiveVersion(int? activeVersion)
    {
        return new Check(Id, SiteId, Name, Version, LatestVersion, activeVersion, Definition);
    }

    // Rebuilds a stored check without validating again, so it stays readable after a check type it uses is gone.
    public static Check Restore(Guid id, Guid siteId, string name, int version, int latestVersion, int? activeVersion, CheckDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);

        return new Check(id, siteId, name, version, latestVersion, activeVersion, definition);
    }
}
