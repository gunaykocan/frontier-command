using Game.Domain.Enums;

namespace Game.Domain.Entities;

public sealed class EnemySighting
{
    private EnemySighting() { }

    public Guid Id { get; private set; }
    public Guid MatchId { get; private set; }
    public Guid ViewerPlayerId { get; private set; }
    public Guid EnemyUnitId { get; private set; }
    public UnitType UnitType { get; private set; }
    public int Column { get; private set; }
    public int Row { get; private set; }
    public int LastSeenTurnNumber { get; private set; }

    internal static EnemySighting Create(Guid matchId, Guid viewerPlayerId, GameUnit enemy, int turnNumber)
    {
        var sighting = new EnemySighting
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            ViewerPlayerId = viewerPlayerId,
            EnemyUnitId = enemy.Id,
        };
        sighting.Observe(enemy, turnNumber);
        return sighting;
    }

    internal void Observe(GameUnit enemy, int turnNumber)
    {
        // Copy only observed information. Never follow the hidden unit's live position or health.
        UnitType = enemy.Type;
        Column = enemy.Column;
        Row = enemy.Row;
        LastSeenTurnNumber = turnNumber;
    }
}
