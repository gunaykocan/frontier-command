using Game.Domain.Entities;
using Game.Domain.Enums;

namespace Game.Application.Features.Bots;

public sealed class BotTurnExecutor(IBotTurnPlanner planner)
{
    private const int MaximumActionsPerTurn = 40;

    public bool ExecuteIfBotTurn(GameMatch match, DateTimeOffset now)
    {
        var bot = match.Players.SingleOrDefault(player =>
            player.IsBot && player.Id == match.ActivePlayerId);

        if (bot is null || match.Status is not MatchStatus.InProgress)
        {
            return false;
        }

        for (var actionCount = 0;
             actionCount < MaximumActionsPerTurn
             && match.Status is MatchStatus.InProgress
             && match.ActivePlayerId == bot.Id;
             actionCount++)
        {
            switch (planner.PlanNextAction(match, bot.Id))
            {
                case BotAttackAction attack:
                    match.AttackUnit(
                        bot.Id,
                        attack.AttackerUnitId,
                        attack.TargetUnitId,
                        match.Version,
                        now);
                    break;
                case BotMoveAction move:
                    match.MoveUnit(
                        bot.Id,
                        move.UnitId,
                        move.TargetColumn,
                        move.TargetRow,
                        match.Version,
                        now);
                    break;
                default:
                    match.EndTurn(bot.Id, match.Version, now);
                    return true;
            }
        }

        if (match.Status is MatchStatus.InProgress && match.ActivePlayerId == bot.Id)
        {
            match.EndTurn(bot.Id, match.Version, now);
        }

        return true;
    }
}
