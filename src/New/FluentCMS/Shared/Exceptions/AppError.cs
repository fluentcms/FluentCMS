namespace FluentCMS;

public class AppError
{
    public string Code { get; set; }
    public string Description { get; set; }

    public AppError(string code)
    {
        Code = code;
        Description = code;
    }

    public override string ToString()
    {
        return $"{Code}-{Description}";
    }
}
