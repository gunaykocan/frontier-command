using Game.Domain.Entities;

namespace Game.Application.Features.Bots;

public interface IBotTurnPlanner
{
    BotAction? PlanNextAction(GameMatch match, Guid botPlayerId);
}
