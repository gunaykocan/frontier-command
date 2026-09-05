using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Game.Api.Authentication;

public static class PlayerSessionAuthenticationExtensions
{
    public static IServiceCollection AddPlayerSessionAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services
            .AddOptions<PlayerSessionOptions>()
            .Bind(configuration.GetSection(PlayerSessionOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.CookieName),
                "PlayerSessions:CookieName must be configured.")
            .Validate(
                options => options.LifetimeHours is >= 1 and <= 24 * 30,
                "PlayerSessions:LifetimeHours must be between 1 and 720.")
            .ValidateOnStart();

        var dataProtection = services
            .AddDataProtection()
            .SetApplicationName("FrontierCommand");

        var keyDirectory = configuration[$"{PlayerSessionOptions.SectionName}:KeyDirectory"];
        if (!string.IsNullOrWhiteSpace(keyDirectory))
        {
            var resolvedKeyDirectory = Path.IsPathRooted(keyDirectory)
                ? keyDirectory
                : Path.GetFullPath(keyDirectory, environment.ContentRootPath);

            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(resolvedKeyDirectory));
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPlayerSessionTokenService, PlayerSessionTokenService>();
        services.AddSingleton<PlayerSessionCookieService>();
        services
            .AddAuthentication(PlayerSessionAuthenticationDefaults.Scheme)
            .AddScheme<AuthenticationSchemeOptions, PlayerSessionAuthenticationHandler>(
                PlayerSessionAuthenticationDefaults.Scheme,
                _ => { });
        services.AddAuthorization();

        return services;
    }
}
