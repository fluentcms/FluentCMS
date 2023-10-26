namespace FluentCMS.Repository.LiteDb;

public class LiteDbOptions
{
    public bool UseInMemory { get; set; } = false;
    public string FilePath { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class LiteDbOptionsBuilder
{
    private readonly LiteDbOptions _options;
    public LiteDbOptionsBuilder()
    {
        _options = new LiteDbOptions();
    }

    public LiteDbOptions Build() => _options;

    public LiteDbOptionsBuilder SetFilePath(string filePath, string password = "")
    {
        _options.FilePath = filePath;
        _options.Password = password;
        return this;
    }

    public LiteDbOptionsBuilder UseInMemory()
    {
        _options.FilePath = "";
        _options.UseInMemory = true;
        return this;
    }
}
