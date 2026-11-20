using EFootwear.BLL.Abstractions;
using EFootwear.BLL.Auth;
using EFootwear.BLL.Common;
using EFootwear.BLL.Platform;
using EFootwear.BLL.Stores;
using EFootwear.BLL.Users;
using EFootwear.DAL.Auth;
using EFootwear.DAL.Persistence;
using EFootwear.DAL.Platform;
using EFootwear.DAL.Security;
using EFootwear.DAL.Stores;
using EFootwear.DAL.Users;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EFootwear.DAL;

public static class DependencyInjection
{
    /// <summary>
    /// Registers everything the Api needs. This is the one place that connects interfaces
    /// (defined in the BLL) to their real implementations (in the DAL).
    /// Add one line here for every new service and repository.
    /// </summary>
    public static IServiceCollection AddDal(this IServiceCollection services, IConfiguration config)
    {
        var connString = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not set (use dotnet user-secrets).");


        // One shared connection pool for the whole app.
        var dataSource = new NpgsqlDataSourceBuilder(connString).Build();
        services.AddSingleton(dataSource);

        // EF Core, used by the platform-admin repository (own short-lived contexts). Request-scoped work uses the
        // context that DbSession builds on its own connection and transaction.
        services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(dataSource));

        // At startup, refuse to run as a superuser (it would bypass row-level security).
        services.AddHostedService<DatabaseRoleGuard>();

        // Scoped = one instance per HTTP request. Authentication and business code share the
        // SAME RequestContext and DbSession, so they see the same user and the same transaction.
        services.AddScoped<RequestContext>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<RequestContext>());
        services.AddScoped<DbSession>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<DbSession>());

        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();

        // --- Company login and session ---
        services.AddScoped<IAuthRepository, AuthRepository>();
        services.AddScoped<AuthService>();

        // --- Platform administrators (own connection; no company scope) ---
        services.AddScoped<IPlatformAdminRepository, PlatformAdminRepository>();
        services.AddScoped<PlatformAuthService>();

        // --- Platform: companies, and stores created by the platform ---
        services.AddScoped<IPlatformRepository, PlatformRepository>();
        services.AddScoped<PlatformService>();

        // --- Stores ---
        services.AddScoped<IStoreRepository, StoreRepository>();
        services.AddScoped<StoreService>();

        // --- Users: creation and listing ---
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<UserService>();
        return services;
    }
}
