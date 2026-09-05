using Game.Api.Authentication;
using Game.Application.Common;
using Game.Application.Features.Commands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Game.Api.Controllers;

[ApiController]
[Route("api/session")]
public sealed class PlayerSessionController(
    MatchService matchService,
    PlayerSessionCookieService cookieService) : ControllerBase
{
    [Authorize]
    [HttpGet]
    [ProducesResponseType<PlayerSession>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PlayerSession>> GetCurrent(CancellationToken cancellationToken)
    {
        if (!User.TryGetPlayerSession(out var identity))
        {
            return Unauthorized();
        }

        var match = await matchService.GetAsync(
            identity.MatchId,
            identity.PlayerId,
            cancellationToken);

        if (match.Players.All(player => player.Id != identity.PlayerId))
        {
            return Unauthorized();
        }

        cookieService.Issue(Response, Request.IsHttps, identity.PlayerId, identity.MatchId);
        return Ok(new PlayerSession(identity.PlayerId, match));
    }

    [AllowAnonymous]
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Clear()
    {
        cookieService.Clear(Response, Request.IsHttps);
        return NoContent();
    }
}
