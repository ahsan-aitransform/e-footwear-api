using Dapper;
using EFootwear.Application;
using EFootwear.Application.Abstractions;
using EFootwear.Infrastructure.Persistence;
using EFootwear.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EFootwear.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString, SecurityOptions security)
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.AddTypeHandler(new DateOnlyHandler());
        SqlMapper.AddTypeHandler(new NullableDateOnlyHandler());

        services.AddSingleton(new NpgsqlDataSourceBuilder(connectionString).Build());
        services.AddSingleton<IUnitOfWorkFactory, UnitOfWorkFactory>();
        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        services.AddSingleton(security);
        services.AddApplication();
        return services;
    }
}
