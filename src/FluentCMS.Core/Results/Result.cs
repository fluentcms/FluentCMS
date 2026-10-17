namespace FluentCMS.Core.Results;

// Application services return results for expected failures, so tools and screens can say what to change (ADR-0004, UX-03).
public sealed class Result
{
    private Result(IReadOnlyList<Error> errors)
    {
        Errors = errors;
    }

    public bool IsSuccess => Errors.Count == 0;

    public IReadOnlyList<Error> Errors { get; }

    public static Result Success()
    {
        return new Result([]);
    }

    public static Result Failure(IEnumerable<Error> errors)
    {
        return new Result(errors.ToList());
    }

    public static Result Failure(string code, string message)
    {
        return Failure([new Error(code, message)]);
    }
}

public sealed class Result<T>
{
    private readonly T? _value;

    private Result(T? value, IReadOnlyList<Error> errors)
    {
        _value = value;
        Errors = errors;
    }

    public bool IsSuccess => Errors.Count == 0;

    public IReadOnlyList<Error> Errors { get; }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("The result failed: " + string.Join("; ", Errors.Select(error => error.Message)));

    public static Result<T> Success(T value)
    {
        return new Result<T>(value, []);
    }

    public static Result<T> Failure(IEnumerable<Error> errors)
    {
        return new Result<T>(default, errors.ToList());
    }

    public static Result<T> Failure(string code, string message)
    {
        return Failure([new Error(code, message)]);
    }
}
