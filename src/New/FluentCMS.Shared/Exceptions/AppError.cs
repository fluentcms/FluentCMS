namespace FluentCMS;

public class AppError
{
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public AppError()
    {
    }

    public AppError(string code)
    {
        Code = code;
    }

    public override string ToString()
    {
        return $"{Code}-{Description}";
    }
}
