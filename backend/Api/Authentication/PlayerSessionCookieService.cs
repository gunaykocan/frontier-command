using Microsoft.Extensions.Options;

namespace Game.Api.Authentication;

public sealed class PlayerSessionCookieService(
    IPlayerSessionTokenService tokenService,
    IOptions<PlayerSessionOptions> options)
{
    public void Issue(HttpResponse response, bool isHttps, Guid playerId, Guid matchId)
    {
        var token = tokenService.Issue(playerId, matchId);

        response.Cookies.Append(
            options.Value.CookieName,
            token.Value,
            CreateCookieOptions(isHttps, token.ExpiresAtUtc));
    }

    public void Clear(HttpResponse response, bool isHttps)
    {
        response.Cookies.Delete(
            options.Value.CookieName,
            CreateCookieOptions(isHttps, expiresAtUtc: null));
    }

    private static CookieOptions CreateCookieOptions(
        bool isHttps,
        DateTimeOffset? expiresAtUtc) => new()
    {
        HttpOnly = true,
        Secure = isHttps,
        SameSite = SameSiteMode.Lax,
        IsEssential = true,
        Path = "/",
        Expires = expiresAtUtc
    };
}
