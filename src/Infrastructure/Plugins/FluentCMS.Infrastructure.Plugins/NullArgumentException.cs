namespace FluentCMS.Infrastructure.Plugins;

public class NullArgumentException(string? paramName) : ArgumentException("Argument cannot be null or empty.", paramName)
{
    public static void ThrowIfNullOrEmpty([NotNull] object? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
    {
        if (argument is null || (argument is string str && string.IsNullOrEmpty(str)))
        {
            throw new NullArgumentException(paramName);
        }
    }
    public static string RequireNonEmptyOrNullString([NotNull] object? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
    {
        if (argument is null || (argument is string str && string.IsNullOrEmpty(str)))
        {
            throw new NullArgumentException(paramName);
        }

        return (string)argument!;
    }

    public static T RequireNonNull<T>([NotNull] T? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
    {
        if (argument is null)
        {
            throw new NullArgumentException(paramName);
        }
        return argument;
    }
}
