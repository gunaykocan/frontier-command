using Game.Domain.Enums;
using Game.Domain.Rules;

namespace Game.Domain.Entities;

public sealed class GameMatch
{
    public const int BoardColumns = 9;
    public const int BoardRows = 7;
    public const int PlayerCapacity = 2;

    private readonly List<GamePlayer> _players = [];
    private readonly List<GameUnit> _units = [];

    private GameMatch()
    {
    }

    private GameMatch(Guid id, string name, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        CreatedAtUtc = createdAtUtc;
        Status = MatchStatus.WaitingForPlayers;
        TurnNumber = 1;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public MatchStatus Status { get; private set; }

    public int TurnNumber { get; private set; }

    public int Version { get; private set; }

    public Guid? ActivePlayerId { get; private set; }

    public Guid? WinnerPlayerId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public IReadOnlyCollection<GamePlayer> Players => _players;

    public IReadOnlyCollection<GameUnit> Units => _units;

    public static (GameMatch Match, GamePlayer Host) Create(
        string matchName,
        string hostName,
        DateTimeOffset createdAtUtc)
    {
        var normalizedMatchName = NormalizeName(matchName, "Match name", 120);
        var normalizedHostName = NormalizeName(hostName, "Player name", 40);
        var match = new GameMatch(Guid.NewGuid(), normalizedMatchName, createdAtUtc);
        var host = GamePlayer.Create(match.Id, normalizedHostName, seat: 1, createdAtUtc);

        match._players.Add(host);

        return (match, host);
    }

    public GamePlayer Join(string playerName, DateTimeOffset joinedAtUtc)
    {
        if (Status is not MatchStatus.WaitingForPlayers || _players.Count >= PlayerCapacity)
        {
            throw new DomainRuleException("This match is no longer accepting players.");
        }

        var normalizedName = NormalizeName(playerName, "Player name", 40);

        if (_players.Any(player => string.Equals(player.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainRuleException("Player names must be unique within a match.");
        }

        var player = GamePlayer.Create(Id, normalizedName, seat: 2, joinedAtUtc);
        _players.Add(player);
        Start();

        return player;
    }

    public void MoveUnit(
        Guid playerId,
        Guid unitId,
        int targetColumn,
        int targetRow,
        int expectedVersion)
    {
        if (Status is not MatchStatus.InProgress)
        {
            throw new DomainRuleException("Moves can only be made in an active match.");
        }

        if (Version != expectedVersion)
        {
            throw new DomainRuleException("The match state is stale. Refresh before sending another move.");
        }

        if (ActivePlayerId != playerId)
        {
            throw new DomainRuleException("It is not this player's turn.");
        }

        var unit = _units.SingleOrDefault(candidate => candidate.Id == unitId)
            ?? throw new DomainRuleException("The selected unit does not exist.");

        if (unit.OwnerPlayerId != playerId)
        {
            throw new DomainRuleException("A player can only move their own unit.");
        }

        EnsureInsideBoard(targetColumn, targetRow);

        if (!AreAdjacent(unit.Column, unit.Row, targetColumn, targetRow))
        {
            throw new DomainRuleException("A unit can move to one adjacent hex per turn.");
        }

        if (_units.Any(candidate => candidate.Column == targetColumn && candidate.Row == targetRow))
        {
            throw new DomainRuleException("The target hex is occupied.");
        }

        unit.MoveTo(targetColumn, targetRow);
        Version++;

        if (HasReachedEnemyCommandEdge(playerId, targetColumn))
        {
            Status = MatchStatus.Completed;
            WinnerPlayerId = playerId;
            ActivePlayerId = null;
            return;
        }

        ActivePlayerId = _players.Single(player => player.Id != playerId).Id;
        TurnNumber++;
    }

    private void Start()
    {
        Status = MatchStatus.InProgress;
        ActivePlayerId = _players[0].Id;
        Version++;

        _units.Add(GameUnit.Create(Id, _players[0].Id, column: 1, row: 3));
        _units.Add(GameUnit.Create(Id, _players[1].Id, column: 7, row: 3));
    }

    private bool HasReachedEnemyCommandEdge(Guid playerId, int targetColumn)
    {
        var player = _players.Single(candidate => candidate.Id == playerId);
        return player.Seat == 1 ? targetColumn == BoardColumns - 1 : targetColumn == 0;
    }

    private static bool AreAdjacent(int column, int row, int targetColumn, int targetRow)
    {
        var directions = row % 2 == 0
            ? new (int Column, int Row)[] { (-1, -1), (0, -1), (-1, 0), (1, 0), (-1, 1), (0, 1) }
            : [(0, -1), (1, -1), (-1, 0), (1, 0), (0, 1), (1, 1)];

        return directions.Any(direction =>
            column + direction.Column == targetColumn && row + direction.Row == targetRow);
    }

    private static void EnsureInsideBoard(int column, int row)
    {
        if (column is < 0 or >= BoardColumns || row is < 0 or >= BoardRows)
        {
            throw new DomainRuleException("The target hex is outside the board.");
        }
    }

    private static string NormalizeName(string value, string fieldName, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();

        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException($"{fieldName} cannot exceed {maximumLength} characters.", nameof(value));
        }

        return normalized;
    }
}
