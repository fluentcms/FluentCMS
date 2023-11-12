namespace FluentCMS.Services.Exceptions;

public class Error
{
    public ErrorType Type { get; set; }
    public ErrorArea Area { get; set; }
    public int Number { get; set; }
    public string Message { get; set; }

    public Error(ErrorArea area, ErrorType type, int number, string message)
    {
        Area = area;
        Type = type;
        Number = number;
        Message = message;
    }

    public override string ToString()
    {
        return $"{Area.ToString().ToLower()}.{(int)Type}.{Number.ToString().PadLeft(3, '0')}";
    }
}
