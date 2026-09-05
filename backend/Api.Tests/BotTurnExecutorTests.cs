using Game.Application.Features.Bots;
using Game.Domain.Entities;
using Game.Domain.Enums;

namespace Game.Api.Tests;

public sealed class BotTurnExecutorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BotUsesNormalDomainActionsAndReturnsTurnToHuman()
    {
        var (match, human) = GameMatch.Create("Bot testi", "İnsan", Now);
        var bot = match.JoinBot("Komuta Botu", Now);
        match.ReadyPlayer(bot.Id, match.Version, Now);
        match.ReadyPlayer(human.Id, match.Version, Now);
        match.EndTurn(human.Id, match.Version, Now);
        var versionBeforeBotTurn = match.Version;
        var executor = new BotTurnExecutor(new BalancedBotTurnPlanner());

        var executed = executor.ExecuteIfBotTurn(match, Now);

        Assert.True(executed);
        Assert.Equal(MatchStatus.InProgress, match.Status);
        Assert.Equal(human.Id, match.ActivePlayerId);
        Assert.True(match.Version > versionBeforeBotTurn);
        Assert.Contains(match.Events, item =>
            item.ActorPlayerId == bot.Id && item.Type == MatchEventType.UnitMoved);
        Assert.Equal(
            MatchEventType.TurnEnded,
            match.Events.OrderBy(item => item.Sequence).Last().Type);
    }

    [Fact]
    public void ExecutorDoesNothingDuringHumanTurn()
    {
        var (match, human) = GameMatch.Create("Bot testi", "İnsan", Now);
        var bot = match.JoinBot("Komuta Botu", Now);
        match.ReadyPlayer(bot.Id, match.Version, Now);
        match.ReadyPlayer(human.Id, match.Version, Now);
        var executor = new BotTurnExecutor(new BalancedBotTurnPlanner());

        var executed = executor.ExecuteIfBotTurn(match, Now);

        Assert.False(executed);
        Assert.Equal(human.Id, match.ActivePlayerId);
    }

    [Fact]
    public void PlannerPrioritizesAdjacentVisibleEnemy()
    {
        var (match, human) = GameMatch.Create("Bot saldırı testi", "İnsan", Now);
        var bot = match.JoinBot("Komuta Botu", Now);
        match.ReadyPlayer(bot.Id, match.Version, Now);
        match.ReadyPlayer(human.Id, match.Version, Now);
        var humanScout = match.Units.Single(unit =>
            unit.OwnerPlayerId == human.Id && unit.Type == UnitType.Scout);
        var botScout = match.Units.Single(unit =>
            unit.OwnerPlayerId == bot.Id && unit.Type == UnitType.Scout);
        match.MoveUnit(human.Id, humanScout.Id, 2, 2, match.Version, Now);
        match.MoveUnit(human.Id, humanScout.Id, 3, 2, match.Version, Now);
        match.EndTurn(human.Id, match.Version, Now);
        match.MoveUnit(bot.Id, botScout.Id, 6, 2, match.Version, Now);
        match.MoveUnit(bot.Id, botScout.Id, 5, 2, match.Version, Now);
        match.MoveUnit(bot.Id, botScout.Id, 4, 2, match.Version, Now);

        var action = new BalancedBotTurnPlanner().PlanNextAction(match, bot.Id);

        var attack = Assert.IsType<BotAttackAction>(action);
        Assert.Equal(botScout.Id, attack.AttackerUnitId);
        Assert.Equal(humanScout.Id, attack.TargetUnitId);
    }
}
