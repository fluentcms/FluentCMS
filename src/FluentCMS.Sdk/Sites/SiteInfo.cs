namespace FluentCMS.Sites;

/// <summary>A site, as the Management API answers it.</summary>
/// <param name="Id">The id of the site.</param>
/// <param name="Name">Its name.</param>
/// <param name="Slug">Its slug.</param>
/// <param name="DefaultLocale">Its default locale.</param>
/// <param name="IsDefault">Whether it is the default site of the instance.</param>
/// <param name="CreatedAt">When it was created.</param>
public sealed record SiteInfo(Guid Id, string Name, string Slug, string DefaultLocale, bool IsDefault, DateTimeOffset CreatedAt);
