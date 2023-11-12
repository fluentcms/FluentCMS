namespace FluentCMS.Services.Exceptions;

public class AppError
{
    public ErrorType Type { get; set; }
    public ErrorArea Area { get; set; }
    public string Code { get; set; }
    public string? Description { get; set; }

    public AppError(ErrorType type, ErrorArea area, string code, string? description = null)
    {
        Type = type;
        Area = area;
        Code = code;
        Description = description;
    }

    public override string ToString()
    {
        return $"{Area.ToString().ToLower()}.{Code}";
    }
}
