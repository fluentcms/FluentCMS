using LiteDB;
using LiteDB.Async;

namespace FluentCMS.Repository.LiteDb
{
    public interface ILiteDbContext
    {
        public LiteDatabaseAsync Database { get; }
    }
}