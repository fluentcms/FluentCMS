namespace FluentCMS.Admin.Api;

/// <summary>One validation error of an <see cref="ApiProblem"/>.</summary>
public sealed record ApiProblemError
{
    /// <summary>The path of the value, such as <c>title</c> or <c>seo.metaTitle</c>, which a form maps to its field.</summary>
    public string? Path { get; init; }

    /// <summary>The FCMS code of the error.</summary>
    public string? Code { get; init; }

    /// <summary>What is wrong, for people.</summary>
    public string? Message { get; init; }
}
