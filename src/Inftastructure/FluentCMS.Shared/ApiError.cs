namespace FluentCMS;

public class ApiError
{
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public ApiError()
    {
    }

    public ApiError(string code)
    {
        Code = code;
    }

    public ApiError(string code, string description)
    {
        Code = code;
        Description = description;
    }

    public override string ToString()
    {
        return $"{Code}-{Description}";
    }
}