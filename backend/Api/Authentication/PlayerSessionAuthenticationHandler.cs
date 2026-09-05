using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Game.Api.Authentication;

public static class PlayerSessionAuthenticationDefaults
{
    public const string Scheme = "PlayerSession";

    public const string MatchIdClaim = "frontier-command:match-id";
}

public sealed class PlayerSessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<PlayerSessionOptions> playerSessionOptions,
    IPlayerSessionTokenService tokenService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var cookieName = playerSessionOptions.Value.CookieName;

        if (!Request.Cookies.TryGetValue(cookieName, out var token))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!tokenService.TryValidate(token, out var identity))
        {
            return Task.FromResult(AuthenticateResult.Fail("The player session is invalid or expired."));
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, identity.PlayerId.ToString("D")),
            new Claim(PlayerSessionAuthenticationDefaults.MatchIdClaim, identity.MatchId.ToString("D"))
        };
        var claimsIdentity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(claimsIdentity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
