using System.Text;
using EFootwear.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EFootwear.DAL.Persistence;

/// <summary>
/// EF Core model of the tables the API reads and writes directly. The database stays the authority:
/// row-level security, triggers and the sec.* / platform.* functions are untouched. EF only maps entity classes
/// (EFootwear.Domain/Entities) to existing tables; it never creates or changes the schema (db/migrations does that).
/// Columns the entity does not list (audit columns, row_ver) keep their database defaults.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<UserStoreAccess> UserStores => Set<UserStoreAccess>();
    public DbSet<Role> Roles => Set<Role>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Company>(e =>
        {
            e.ToTable("companies", "platform");
            e.HasKey(x => x.Id);
            e.Property(x => x.CurrencyCode).HasColumnType("character(3)");
            e.Property(x => x.CreatedAt).ValueGeneratedOnAdd().HasDefaultValueSql("now()");
        });

        b.Entity<Store>(e =>
        {
            e.ToTable("stores", "core");
            e.HasKey(x => new { x.CompanyId, x.Id });
            e.Property(x => x.Id).ValueGeneratedOnAdd();
        });

        b.Entity<AppUser>(e =>
        {
            e.ToTable("users", "sec");
            e.HasKey(x => new { x.CompanyId, x.Id });
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.CreatedAt).ValueGeneratedOnAdd().HasDefaultValueSql("now()");
        });

        b.Entity<UserStoreAccess>(e =>
        {
            e.ToTable("user_stores", "sec");
            e.HasKey(x => new { x.CompanyId, x.UserId, x.StoreId });
        });

        b.Entity<Role>(e =>
        {
            e.ToTable("roles", "sec");
            e.HasKey(x => x.Code);
        });

        // Column names in the database are snake_case; the classes use PascalCase.
        foreach (var entity in b.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                property.SetColumnName(ToSnake(property.Name));
    }

    private static string ToSnake(string name)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && i > 0) sb.Append('_');
            sb.Append(char.ToLowerInvariant(name[i]));
        }
        return sb.ToString();
    }
}
