//using LiteDB.Async;
//using Microsoft.Extensions.Options;

//namespace FluentCMS.Repositories.LiteDb;

//public class LiteDbContext
//{
//    private readonly LiteDatabaseAsync _db = default!;
//    public LiteDatabaseAsync Database => _db;

//    public LiteDbContext(IOptions<LiteDbOptions> options)
//    {
//        if (options?.Value is null)
//            throw new ArgumentNullException(nameof(options));

//        if (string.IsNullOrWhiteSpace(options.Value.ConnectionString) == false)
//        {
//            _db = new LiteDatabaseAsync(options.Value.ConnectionString);
//        }
//        else
//        {
//            _db = new LiteDatabaseAsync(new MemoryStream());
//        }
//    }
//}
