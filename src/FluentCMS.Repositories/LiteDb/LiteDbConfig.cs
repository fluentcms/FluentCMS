namespace FluentCMS.Repositories.LiteDb;

public class LiteDbOptions
{
    public bool UseInMemory { get; set; } = false;
    public string FilePath { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
