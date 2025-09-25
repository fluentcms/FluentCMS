using FluentCMS.Plugins.TodoManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace FluentCMS.Plugins.TodoManager.Repositories;

public class TodoDbContext(DbContextOptions<TodoDbContext> options) : DbContext(options)
{
    public DbSet<Todo> Todos { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configure the Todo entity
        modelBuilder.Entity<Todo>()
            .HasKey(t => t.Id);

        modelBuilder.Entity<Todo>()
            .Property(t => t.Title)
            .IsRequired()
            .HasMaxLength(150);

        modelBuilder.Entity<Todo>()
            .Property(t => t.Description)
            .HasMaxLength(500);
    }
}