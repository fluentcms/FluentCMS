using System.Globalization;

namespace FluentCMS.Admin.Api;

/// <summary>The Management API did not answer with a success; <see cref="Problem"/> says what it answered.</summary>
public sealed class ApiProblemException : Exception
{
    /// <summary>Creates the exception with an empty problem.</summary>
    public ApiProblemException()
    {
    }

    /// <summary>Creates the exception with an empty problem.</summary>
    /// <param name="message">The message.</param>
    public ApiProblemException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with an empty problem.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public ApiProblemException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception for a problem.</summary>
    /// <param name="problem">The problem.</param>
    public ApiProblemException(ApiProblem problem)
        : base(MessageOf(problem))
    {
        Problem = problem;
    }

    /// <summary>The problem.</summary>
    public ApiProblem Problem { get; } = new();

    private static string MessageOf(ApiProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        return $"{problem.Code ?? problem.Status.ToString(CultureInfo.InvariantCulture)}: {problem.Detail ?? problem.Title ?? "The API answered with a problem."}";
    }
}
