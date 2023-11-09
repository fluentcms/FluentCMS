namespace FluentCMS.Services.ErrorModels;

public class ErrorCode
{
    public ErrorType Type { get; set; }
    public string Area { get; set; }
    public int Number { get; set; }
    public string Message { get; set; }

    public ErrorCode(ErrorType type, string area, int number, string message)
    {
        Type = type;
        Area = area;
        Number = number;
        Message = message;
    }

    public override string ToString()
    {
        return $"{Area}.{(int)Type}.{Number.ToString().PadLeft(4, '0')}";
    }
}
