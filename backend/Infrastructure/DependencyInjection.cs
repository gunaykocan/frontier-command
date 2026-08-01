using Game.Application.Abstractions;
using Game.Infrastructure.Persistence;
using Game.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Game.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var databaseConnection = configuration.GetConnectionString("GameDatabase");

        if (string.IsNullOrWhiteSpace(databaseConnection))
        {
            throw new InvalidOperationException("ConnectionStrings:GameDatabase must be configured.");
        }

        services.AddDbContext<GameDbContext>(options => options.UseNpgsql(databaseConnection));
        services.AddScoped<IGameMatchRepository, PostgresGameMatchRepository>();

        var redisConnection = configuration.GetConnectionString("Redis");

        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnection;
                options.InstanceName = "game:";
            });
        }

        return services;
    }
}
