using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using IdentityExample.Models;

namespace IdentityExample;

public class AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options) :
    IdentityDbContext<
        User,              // TUser
        Role,              // TRole
        Guid,              // TKey
        UserClaim,         // TUserClaim
        UserRole,          // TUserRole
        UserLogin,         // TUserLogin
        RoleClaim,         // TRoleClaim
        UserToken          // TUserToken
    >(options)

{
    public DbSet<JwtToken> JwtTokens { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Configure JwtToken entity
        builder.Entity<JwtToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DeviceInfo).HasMaxLength(500);
            entity.Property(e => e.IpAddress).HasMaxLength(45); // IPv6 max length
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.ExpiresAt);
            entity.HasIndex(e => e.IsRevoked);

            // Configure relationship with User
            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
