using LiteDB.Async;

namespace FluentCMS.Repository.LiteDb.Tests.TestContext
{
    internal class LiteDbTestContext : ILiteDbContext
    {
        public LiteDatabaseAsync Database { get; }
        public LiteDbTestContext()
        {
            // using in-memory instance to avoid clean up
            // https://github.com/mbdavid/LiteDB/blob/master/LiteDB.Tests/Database/DbRef_Interface_Tests.cs#L45
            Database = new LiteDatabaseAsync(new MemoryStream());
        }
    }
}