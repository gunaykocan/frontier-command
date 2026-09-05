using Game.Application.Features.Commands;
using Game.Application.Features.Bots;
using Game.Application.Features.Queries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Game.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<GameCatalog>();
        services.AddSingleton<IBotTurnPlanner, BalancedBotTurnPlanner>();
        services.AddScoped<BotTurnExecutor>();
        services.AddScoped<MatchService>();

        return services;
    }
}
