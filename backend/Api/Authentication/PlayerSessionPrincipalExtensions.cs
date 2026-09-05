using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

namespace Game.Api.Authentication;

public static class PlayerSessionPrincipalExtensions
{
    public static bool TryGetPlayerSession(
        this ClaimsPrincipal principal,
        [NotNullWhen(true)] out PlayerSessionIdentity? identity)
    {
        identity = null;
        var playerIdValue = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var matchIdValue = principal.FindFirstValue(PlayerSessionAuthenticationDefaults.MatchIdClaim);

        if (!Guid.TryParse(playerIdValue, out var playerId)
            || !Guid.TryParse(matchIdValue, out var matchId))
        {
            return false;
        }

        identity = new PlayerSessionIdentity(playerId, matchId);
        return true;
    }
}
