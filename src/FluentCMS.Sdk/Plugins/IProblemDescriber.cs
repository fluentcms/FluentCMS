using FluentCMS.Content;

namespace FluentCMS.Plugins;

/// <summary>
/// Describes an exception as the REST APIs answer it, for a plugin surface that does not answer with problem details, such
/// as MCP: the stable code, the HTTP status the code stands for, its title and a message. Core implements it with the
/// codes of the host, those the plugins added with <see cref="IPluginServiceBuilder.AddErrorCode"/> included.
/// </summary>
public interface IProblemDescriber
{
    /// <summary>Describes an exception.</summary>
    /// <param name="exception">The exception.</param>
    /// <returns>
    /// The description. An exception that says nothing of its cause is <c>FCMS3199</c> with status 500 and a message that
    /// holds nothing of the exception, which the caller logs.
    /// </returns>
    ProblemDescription Describe(Exception exception);
}

/// <summary>An exception as the REST APIs answer it.</summary>
/// <param name="Code">The stable code, such as <c>FCMS3104</c>.</param>
/// <param name="Status">The HTTP status the code stands for.</param>
/// <param name="Title">A short title for people.</param>
/// <param name="Detail">A message for people.</param>
public sealed record ProblemDescription(string Code, int Status, string Title, string Detail)
{
    /// <summary>The problems found when an entry or a request did not validate; empty for any other failure.</summary>
    public IReadOnlyList<ValidationError> Errors { get; init; } = [];
}
