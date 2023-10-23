using FluentCMS.Core.Entities;
using LiteDB.Async;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Repository.LiteDb.Tests.TestContext
{
    // Extension of repository to expose collection for easier Testing
    internal class LiteDbTestRepository<TEntity> : LiteDbGenericRepository<TEntity> where TEntity : IEntity
    {
        public LiteDbTestRepository(ILiteDbContext liteDbContext) : base(liteDbContext)
        {
        }

        internal ILiteCollectionAsync<TEntity> GetLiteCollection() => this.Collection;
    }
}
