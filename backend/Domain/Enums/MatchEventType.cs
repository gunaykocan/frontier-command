namespace Game.Domain.Enums;

public enum MatchEventType
{
    DeploymentStarted,
    UnitPlaced,
    PlayerReady,
    BattleStarted,
    UnitMoved,
    ReinforcementTriggered,
    MineTriggered,
    UnitAttacked,
    TurnEnded,
    MatchCompleted,
    RematchRequested,
    RematchAccepted,
    TurnTimedOut
}
