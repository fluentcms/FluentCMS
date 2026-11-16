namespace FluentCMS.Admin.Configuration;

/// <summary>The interface cultures of the admin, from the <c>cultures</c> member of <c>config.json</c>.</summary>
public sealed record AdminCultures
{
    /// <summary>
    /// The culture names offered, such as <c>en</c> or <c>de-CH</c>; <see langword="null"/> when not set, which offers every
    /// culture the admin ships.
    /// </summary>
    public IReadOnlyList<string>? Offered { get; init; }

    /// <summary>The culture used when the user has not chosen one; <c>en</c> when not set. It is one of <see cref="Offered"/> when that is set.</summary>
    public string Default { get; init; } = "en";
}
