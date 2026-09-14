using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Argus.Server.Features.Auth;

internal static class IdentityModel
{
    /// <summary>Gives the Identity tables short names and seeds the built-in roles.</summary>
    public static void ConfigureIdentity(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ArgusUser>(user =>
        {
            user.ToTable("users");
            user.Property(u => u.DisplayName).HasMaxLength(100);
        });

        modelBuilder.Entity<IdentityRole<Guid>>(role =>
        {
            role.ToTable("roles");
            role.HasData(
                new IdentityRole<Guid> { Id = Roles.AdminId, Name = Roles.Admin, NormalizedName = "ADMIN", ConcurrencyStamp = "role-admin" },
                new IdentityRole<Guid> { Id = Roles.UserId, Name = Roles.User, NormalizedName = "USER", ConcurrencyStamp = "role-user" });
        });

        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");
    }
}
