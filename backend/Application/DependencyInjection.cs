using Game.Application.Features.Commands;
using Game.Application.Features.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace Game.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<GameCatalog>();
        services.AddScoped<MatchService>();

        return services;
    }
}
