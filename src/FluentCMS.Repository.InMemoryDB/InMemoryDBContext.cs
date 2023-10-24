using FluentCMS.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Repository.InMemoryDB;

public class InMemoryDBContext<TKey, TEntity> : DbContext
        where TKey : IEquatable<TKey>
        where TEntity : class, IEntity<TKey>
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseInMemoryDatabase(databaseName: "InMemoryDB");
    }
    public DbSet<TEntity> DbSet { get; set; }
}

public class InMemoryDBContext<TEntity> : DbContext
        where TEntity : class, IEntity
{
    public DbSet<TEntity> DbSet { get; set; }
}
