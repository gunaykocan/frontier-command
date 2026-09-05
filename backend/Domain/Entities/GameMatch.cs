using Game.Domain.Enums;
using Game.Domain.Rules;

namespace Game.Domain.Entities;

public sealed class GameMatch
{
    public const int BoardColumns = BattlefieldTerrain.Columns;
    public const int BoardRows = BattlefieldTerrain.Rows;
    public const int PlayerCapacity = 2;
    public const int DeploymentColumns = 2;
    public const int TurnDurationSeconds = 90;

    private readonly List<GamePlayer> _players = [];
    private readonly List<GameUnit> _units = [];
    private readonly List<GameMatchEvent> _events = [];
    private readonly List<GameSpecialTile> _specialTiles = [];
    private readonly List<EnemySighting> _enemySightings = [];

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

    public DateTimeOffset? TurnExpiresAtUtc { get; private set; }

    public Guid? WinnerPlayerId { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public Guid? RematchRequestedByPlayerId { get; private set; }

    public Guid? RematchMatchId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public IReadOnlyCollection<GamePlayer> Players => _players;

    public IReadOnlyCollection<GameUnit> Units => _units;

    public IReadOnlyCollection<GameMatchEvent> Events => _events;

    public IReadOnlyCollection<GameSpecialTile> SpecialTiles => _specialTiles;

    public IReadOnlyCollection<EnemySighting> EnemySightings => _enemySightings;

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
        return JoinPlayer(playerName, joinedAtUtc, isBot: false);
    }

    public GamePlayer JoinBot(string playerName, DateTimeOffset joinedAtUtc)
    {
        return JoinPlayer(playerName, joinedAtUtc, isBot: true);
    }

    private GamePlayer JoinPlayer(string playerName, DateTimeOffset joinedAtUtc, bool isBot)
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

        var player = GamePlayer.Create(Id, normalizedName, seat: 2, joinedAtUtc, isBot);
        _players.Add(player);
        StartDeployment();

        return player;
    }

    public void PlaceUnit(
        Guid playerId,
        Guid unitId,
        int targetColumn,
        int targetRow,
        int expectedVersion)
    {
        EnsureDeploymentAction(playerId, expectedVersion);
        EnsureInsideBoard(targetColumn, targetRow);

        var player = GetPlayer(playerId);
        var unit = GetUnit(unitId, "The selected unit does not exist.");

        if (unit.OwnerPlayerId != playerId)
        {
            throw new DomainRuleException("A player can only place their own unit.");
        }

        if (!IsInsideDeploymentZone(player, targetColumn))
        {
            throw new DomainRuleException("The selected hex is outside this player's deployment zone.");
        }

        if (_units.Any(candidate => candidate.Column == targetColumn && candidate.Row == targetRow))
        {
            throw new DomainRuleException("The target hex is occupied.");
        }

        var fromColumn = unit.Column;
        var fromRow = unit.Row;
        unit.PlaceAt(targetColumn, targetRow);
        AddEvent(
            MatchEventType.UnitPlaced,
            actorPlayerId: playerId,
            unit: unit,
            fromColumn: fromColumn,
            fromRow: fromRow,
            toColumn: targetColumn,
            toRow: targetRow);
        Version++;
    }

    public void ReadyPlayer(Guid playerId, int expectedVersion, DateTimeOffset? occurredAtUtc = null)
    {
        EnsureDeploymentAction(playerId, expectedVersion);
        var player = GetPlayer(playerId);
        player.MarkReady();
        AddEvent(MatchEventType.PlayerReady, actorPlayerId: playerId);
        Version++;

        if (_players.All(candidate => candidate.IsReady))
        {
            BeginBattle(occurredAtUtc ?? DateTimeOffset.UtcNow);
        }
    }

    public void MoveUnit(
        Guid playerId,
        Guid unitId,
        int targetColumn,
        int targetRow,
        int expectedVersion,
        DateTimeOffset? occurredAtUtc = null)
    {
        var now = occurredAtUtc ?? DateTimeOffset.UtcNow;
        EnsurePlayerCanAct(playerId, expectedVersion, now);

        var unit = GetUnit(unitId, "The selected unit does not exist.");

        if (unit.OwnerPlayerId != playerId)
        {
            throw new DomainRuleException("A player can only move their own unit.");
        }

        if (unit.RemainingMovement <= 0)
        {
            throw new DomainRuleException("The selected unit has no movement points remaining.");
        }

        EnsureInsideBoard(targetColumn, targetRow);

        if (!AreAdjacent(unit.Column, unit.Row, targetColumn, targetRow))
        {
            throw new DomainRuleException("A unit can move to one adjacent hex at a time.");
        }

        if (_units.Any(candidate => candidate.Column == targetColumn && candidate.Row == targetRow))
        {
            throw new DomainRuleException("The target hex is occupied.");
        }

        var terrain = BattlefieldTerrain.At(targetColumn, targetRow);

        if (unit.RemainingMovement < terrain.MovementCost)
        {
            throw new DomainRuleException(
                $"The selected unit needs {terrain.MovementCost} movement points to enter {terrain.Type} terrain.");
        }

        UpdateReconnaissance();
        var fromColumn = unit.Column;
        var fromRow = unit.Row;
        unit.MoveTo(targetColumn, targetRow, terrain.MovementCost);
        AddEvent(
            MatchEventType.UnitMoved,
            actorPlayerId: playerId,
            unit: unit,
            fromColumn: fromColumn,
            fromRow: fromRow,
            toColumn: targetColumn,
            toRow: targetRow,
            amount: terrain.MovementCost);
        ResolveSpecialTile(playerId, unit, now);
        Version++;

        if (Status is MatchStatus.InProgress && HasReachedEnemyCommandEdge(playerId, targetColumn))
        {
            Complete(playerId, now);
        }
        UpdateReconnaissance();
    }

    public void AttackUnit(
        Guid playerId,
        Guid attackerUnitId,
        Guid targetUnitId,
        int expectedVersion,
        DateTimeOffset? occurredAtUtc = null)
    {
        var now = occurredAtUtc ?? DateTimeOffset.UtcNow;
        EnsurePlayerCanAct(playerId, expectedVersion, now);

        var attacker = GetUnit(attackerUnitId, "The attacking unit does not exist.");
        var target = GetUnit(targetUnitId, "The target unit does not exist.");

        if (attacker.OwnerPlayerId != playerId)
        {
            throw new DomainRuleException("A player can only attack with their own unit.");
        }

        if (target.OwnerPlayerId == playerId)
        {
            throw new DomainRuleException("A player cannot attack their own unit.");
        }

        if (attacker.HasAttackedThisTurn)
        {
            throw new DomainRuleException("The selected unit has already attacked this turn.");
        }

        if (!AreAdjacent(attacker.Column, attacker.Row, target.Column, target.Row))
        {
            throw new DomainRuleException("A unit can only attack an adjacent enemy.");
        }

        var defenseBonus = BattlefieldTerrain.At(target.Column, target.Row).DefenseBonus;
        var attackDamage = Math.Max(1, attacker.AttackPower - defenseBonus);
        var damage = Math.Min(target.Health, attackDamage);
        UpdateReconnaissance();
        attacker.RevealByAttack(TurnNumber);
        attacker.MarkAttackUsed();
        target.ReceiveDamage(attackDamage);
        AddEvent(
            MatchEventType.UnitAttacked,
            actorPlayerId: playerId,
            unit: attacker,
            targetUnit: target,
            fromColumn: attacker.Column,
            fromRow: attacker.Row,
            toColumn: target.Column,
            toRow: target.Row,
            amount: damage,
            wasDestroyed: target.IsDestroyed);
        Version++;

        if (target.IsDestroyed)
        {
            _units.Remove(target);

            if (_units.All(unit => unit.OwnerPlayerId == playerId))
            {
                Complete(playerId, now);
            }
        }
        UpdateReconnaissance();
    }

    public void RequestRematch(Guid playerId, int expectedVersion)
    {
        EnsureRematchAction(playerId, expectedVersion);

        if (RematchMatchId is not null)
        {
            throw new DomainRuleException("A rematch has already been created for this match.");
        }

        if (RematchRequestedByPlayerId is not null)
        {
            throw new DomainRuleException(
                RematchRequestedByPlayerId == playerId
                    ? "This player has already requested a rematch."
                    : "The other player has already requested a rematch. Accept it instead.");
        }

        RematchRequestedByPlayerId = playerId;
        AddEvent(MatchEventType.RematchRequested, actorPlayerId: playerId);
        Version++;
    }

    public GameMatch AcceptRematch(
        Guid playerId,
        int expectedVersion,
        DateTimeOffset createdAtUtc)
    {
        EnsureRematchAction(playerId, expectedVersion);

        if (RematchMatchId is not null)
        {
            throw new DomainRuleException("A rematch has already been created for this match.");
        }

        if (RematchRequestedByPlayerId is null)
        {
            throw new DomainRuleException("A rematch must be requested before it can be accepted.");
        }

        if (RematchRequestedByPlayerId == playerId)
        {
            throw new DomainRuleException("The player who requested the rematch cannot accept it.");
        }

        var originalPlayers = _players.OrderBy(player => player.Seat).ToArray();
        var rematch = new GameMatch(Guid.NewGuid(), Name, createdAtUtc);
        rematch._players.Add(GamePlayer.Create(
            rematch.Id,
            originalPlayers[1].Name,
            seat: 1,
            createdAtUtc,
            originalPlayers[1].IsBot));
        rematch._players.Add(GamePlayer.Create(
            rematch.Id,
            originalPlayers[0].Name,
            seat: 2,
            createdAtUtc,
            originalPlayers[0].IsBot));
        rematch.StartDeployment();

        AddEvent(
            MatchEventType.RematchAccepted,
            actorPlayerId: playerId,
            relatedPlayerId: RematchRequestedByPlayerId);
        RematchMatchId = rematch.Id;
        Version++;
        return rematch;
    }

    public void EndTurn(Guid playerId, int expectedVersion, DateTimeOffset? occurredAtUtc = null)
    {
        var now = occurredAtUtc ?? DateTimeOffset.UtcNow;
        EnsurePlayerCanAct(playerId, expectedVersion, now);
        AdvanceTurn(playerId, now, MatchEventType.TurnEnded);
    }

    public bool TryExpireTurn(DateTimeOffset now)
    {
        if (Status is not MatchStatus.InProgress
            || ActivePlayerId is not Guid playerId
            || TurnExpiresAtUtc is not DateTimeOffset deadline
            || now < deadline)
        {
            return false;
        }

        // Resume with one fresh turn after downtime, never skip multiple players' turns.
        AdvanceTurn(playerId, now, MatchEventType.TurnTimedOut);
        return true;
    }

    private void AdvanceTurn(Guid playerId, DateTimeOffset now, MatchEventType reason)
    {
        UpdateReconnaissance();
        foreach (var unit in _units.Where(unit => unit.OwnerPlayerId == playerId))
        {
            unit.EndTurn();
        }

        var nextPlayerId = _players.Single(player => player.Id != playerId).Id;
        ActivePlayerId = nextPlayerId;
        TurnExpiresAtUtc = now.AddSeconds(TurnDurationSeconds);
        BeginTurn(nextPlayerId);
        AddEvent(
            reason,
            actorPlayerId: playerId,
            relatedPlayerId: nextPlayerId);
        TurnNumber++;
        Version++;
        UpdateReconnaissance();
    }

    private void EnsurePlayerCanAct(Guid playerId, int expectedVersion, DateTimeOffset now)
    {
        if (Status is not MatchStatus.InProgress)
        {
            throw new DomainRuleException("Actions can only be made in an active match.");
        }

        EnsureExpectedVersion(expectedVersion);

        if (ActivePlayerId != playerId)
        {
            throw new DomainRuleException("It is not this player's turn.");
        }

        if (TurnExpiresAtUtc is null || now >= TurnExpiresAtUtc)
        {
            throw new DomainRuleException("The turn time has expired. Wait for the next turn.");
        }
    }

    private void EnsureDeploymentAction(Guid playerId, int expectedVersion)
    {
        if (Status is not MatchStatus.Deploying)
        {
            throw new DomainRuleException("Units can only be placed during deployment.");
        }

        EnsureExpectedVersion(expectedVersion);
        var player = GetPlayer(playerId);

        if (player.IsReady)
        {
            throw new DomainRuleException("A ready player can no longer change their deployment.");
        }
    }

    private void EnsureExpectedVersion(int expectedVersion)
    {
        if (Version != expectedVersion)
        {
            throw new DomainRuleException("The match state is stale. Refresh before sending another action.");
        }
    }

    private void EnsureRematchAction(Guid playerId, int expectedVersion)
    {
        if (Status is not MatchStatus.Completed)
        {
            throw new DomainRuleException("A rematch can only be arranged after the match is completed.");
        }

        EnsureExpectedVersion(expectedVersion);
        _ = GetPlayer(playerId);
    }

    private void StartDeployment()
    {
        var playersBySeat = _players.OrderBy(player => player.Seat).ToArray();

        Status = MatchStatus.Deploying;
        ActivePlayerId = null;
        Version++;

        DeployArmy(playersBySeat[0].Id, column: 1, reserveColumn: 0);
        DeployArmy(playersBySeat[1].Id, column: 7, reserveColumn: 8);
        CreateSpecialTiles();
        AddEvent(MatchEventType.DeploymentStarted);
    }

    private void BeginBattle(DateTimeOffset now)
    {
        var firstPlayerId = _players.OrderBy(player => player.Seat).First().Id;

        Status = MatchStatus.InProgress;
        ActivePlayerId = firstPlayerId;
        TurnExpiresAtUtc = now.AddSeconds(TurnDurationSeconds);
        BeginTurn(firstPlayerId);
        AddEvent(
            MatchEventType.BattleStarted,
            relatedPlayerId: firstPlayerId);
        UpdateReconnaissance();
    }

    private void DeployArmy(Guid playerId, int column, int reserveColumn)
    {
        _units.Add(GameUnit.Create(Id, playerId, UnitType.Scout, column, row: 2));
        _units.Add(GameUnit.Create(Id, playerId, UnitType.Infantry, column, row: 3));
        _units.Add(GameUnit.Create(Id, playerId, UnitType.Armor, column, row: 4));
        // Keep the original front line and add two independently controlled infantry in reserve.
        _units.Add(GameUnit.Create(Id, playerId, UnitType.Infantry, reserveColumn, row: 3));
        _units.Add(GameUnit.Create(Id, playerId, UnitType.Infantry, reserveColumn, row: 4));
    }

    private void CreateSpecialTiles()
    {
        var candidates = Enumerable.Range(DeploymentColumns, BoardColumns - DeploymentColumns * 2)
            .SelectMany(column => new[] { 0, 1, 4, 5, 6 }.Select(row => (Column: column, Row: row)))
            .ToList();
        var random = new Random(BitConverter.ToInt32(Id.ToByteArray(), 0));

        for (var index = candidates.Count - 1; index > 0; index--)
        {
            var swapIndex = random.Next(index + 1);
            (candidates[index], candidates[swapIndex]) = (candidates[swapIndex], candidates[index]);
        }

        var types = new[]
        {
            SpecialTileType.Reinforcement,
            SpecialTileType.Mine,
            SpecialTileType.Reinforcement,
            SpecialTileType.Mine
        };

        for (var index = 0; index < types.Length; index++)
        {
            var position = candidates[index];
            _specialTiles.Add(GameSpecialTile.Create(Id, types[index], position.Column, position.Row));
        }
    }

    private void ResolveSpecialTile(Guid playerId, GameUnit unit, DateTimeOffset occurredAtUtc)
    {
        var specialTile = _specialTiles.SingleOrDefault(tile =>
            !tile.IsRevealed && tile.Column == unit.Column && tile.Row == unit.Row);

        if (specialTile is null) return;

        specialTile.Reveal(unit.Id);

        if (specialTile.Type is SpecialTileType.Reinforcement)
        {
            var reinforcementPosition = FindNearestReinforcementPosition(unit.Column, unit.Row);
            var reinforcement = GameUnit.Create(
                Id,
                playerId,
                unit.Type,
                reinforcementPosition.Column,
                reinforcementPosition.Row);
            reinforcement.ExhaustForCurrentTurn();
            _units.Add(reinforcement);
            AddEvent(
                MatchEventType.ReinforcementTriggered,
                actorPlayerId: playerId,
                unit: unit,
                targetUnit: reinforcement,
                fromColumn: specialTile.Column,
                fromRow: specialTile.Row,
                toColumn: reinforcement.Column,
                toRow: reinforcement.Row,
                amount: 1);
            return;
        }

        var mineDamage = Math.Min(unit.Health, Math.Max(1, (unit.Health + 1) / 2));
        unit.ReceiveDamage(mineDamage);
        AddEvent(
            MatchEventType.MineTriggered,
            actorPlayerId: playerId,
            unit: unit,
            toColumn: specialTile.Column,
            toRow: specialTile.Row,
            amount: mineDamage,
            wasDestroyed: unit.IsDestroyed);

        if (!unit.IsDestroyed) return;

        _units.Remove(unit);

        if (_units.All(candidate => candidate.OwnerPlayerId != playerId))
        {
            var winnerPlayerId = _players.Single(player => player.Id != playerId).Id;
            Complete(winnerPlayerId, occurredAtUtc);
        }
    }

    private (int Column, int Row) FindNearestReinforcementPosition(int column, int row)
    {
        var visited = new HashSet<(int Column, int Row)> { (column, row) };
        var queue = new Queue<(int Column, int Row)>();

        foreach (var position in AdjacentPositions(column, row))
        {
            visited.Add(position);
            queue.Enqueue(position);
        }

        while (queue.TryDequeue(out var candidate))
        {
            var isOccupied = _units.Any(unit => unit.Column == candidate.Column && unit.Row == candidate.Row);
            var hidesSpecialTile = _specialTiles.Any(tile =>
                !tile.IsRevealed && tile.Column == candidate.Column && tile.Row == candidate.Row);

            if (!isOccupied && !hidesSpecialTile)
            {
                return candidate;
            }

            foreach (var next in AdjacentPositions(candidate.Column, candidate.Row))
            {
                if (visited.Add(next)) queue.Enqueue(next);
            }
        }

        throw new DomainRuleException("No empty hex is available for the reinforcement unit.");
    }

    private void BeginTurn(Guid playerId)
    {
        foreach (var unit in _units.Where(unit => unit.OwnerPlayerId == playerId))
        {
            unit.BeginTurn();
        }
    }

    private GameUnit GetUnit(Guid unitId, string errorMessage) =>
        _units.SingleOrDefault(candidate => candidate.Id == unitId)
        ?? throw new DomainRuleException(errorMessage);

    private GamePlayer GetPlayer(Guid playerId) =>
        _players.SingleOrDefault(candidate => candidate.Id == playerId)
        ?? throw new DomainRuleException("The player does not belong to this match.");

    private static bool IsInsideDeploymentZone(GamePlayer player, int targetColumn) =>
        player.Seat == 1
            ? targetColumn < DeploymentColumns
            : targetColumn >= BoardColumns - DeploymentColumns;

    private void Complete(Guid winnerPlayerId, DateTimeOffset completedAtUtc)
    {
        if (Status is MatchStatus.Completed) return;

        Status = MatchStatus.Completed;
        WinnerPlayerId = winnerPlayerId;
        CompletedAtUtc = completedAtUtc;
        ActivePlayerId = null;
        TurnExpiresAtUtc = null;
        AddEvent(MatchEventType.MatchCompleted, relatedPlayerId: winnerPlayerId);
    }

    private void UpdateReconnaissance()
    {
        if (Status is not MatchStatus.InProgress) return;

        foreach (var player in _players)
        {
            var visible = BattlefieldVision.ForPlayer(this, player.Id);
            var observedEnemies = _units.Where(unit => unit.OwnerPlayerId != player.Id
                && visible.Contains(new BattlefieldCoordinate(unit.Column, unit.Row))).ToList();
            var observedIds = observedEnemies.Select(unit => unit.Id).ToHashSet();

            // A newly inspected, empty old location is confirmed clear. An unseen death is not.
            _enemySightings.RemoveAll(sighting => sighting.ViewerPlayerId == player.Id
                && !observedIds.Contains(sighting.EnemyUnitId)
                && visible.Contains(new BattlefieldCoordinate(sighting.Column, sighting.Row)));

            foreach (var enemy in observedEnemies)
            {
                var sighting = _enemySightings.SingleOrDefault(item =>
                    item.ViewerPlayerId == player.Id && item.EnemyUnitId == enemy.Id);
                if (sighting is null)
                {
                    _enemySightings.Add(EnemySighting.Create(Id, player.Id, enemy, TurnNumber));
                }
                else
                {
                    sighting.Observe(enemy, TurnNumber);
                }
            }
        }
    }

    private void AddEvent(
        MatchEventType type,
        Guid? actorPlayerId = null,
        Guid? relatedPlayerId = null,
        GameUnit? unit = null,
        GameUnit? targetUnit = null,
        int? fromColumn = null,
        int? fromRow = null,
        int? toColumn = null,
        int? toRow = null,
        int? amount = null,
        bool wasDestroyed = false)
    {
        var sequence = _events.Count == 0 ? 1 : _events.Max(item => item.Sequence) + 1;
        var matchEvent = GameMatchEvent.Create(
            Id,
            sequence,
            type,
            TurnNumber,
            actorPlayerId,
            relatedPlayerId,
            unit?.Id,
            unit?.Type,
            targetUnit?.Id,
            targetUnit?.Type,
            fromColumn,
            fromRow,
            toColumn,
            toRow,
            amount,
            wasDestroyed);

        foreach (var player in _players)
        {
            var visible = BattlefieldVision.ForPlayer(this, player.Id);
            matchEvent.RecordVisibility(
                player.Seat,
                fromColumn is int fc && fromRow is int fr && visible.Contains(new BattlefieldCoordinate(fc, fr)),
                toColumn is int tc && toRow is int tr && visible.Contains(new BattlefieldCoordinate(tc, tr)));
            if (wasDestroyed && matchEvent.WasToVisibleTo(player.Seat))
            {
                var destroyedUnitId = type is MatchEventType.UnitAttacked ? targetUnit?.Id : unit?.Id;
                _enemySightings.RemoveAll(sighting => sighting.ViewerPlayerId == player.Id
                    && sighting.EnemyUnitId == destroyedUnitId);
            }
        }
        _events.Add(matchEvent);
    }

    private bool HasReachedEnemyCommandEdge(Guid playerId, int targetColumn)
    {
        var player = _players.Single(candidate => candidate.Id == playerId);
        return player.Seat == 1 ? targetColumn == BoardColumns - 1 : targetColumn == 0;
    }

    private static bool AreAdjacent(int column, int row, int targetColumn, int targetRow)
        => AdjacentPositions(column, row).Contains((targetColumn, targetRow));

    private static IEnumerable<(int Column, int Row)> AdjacentPositions(int column, int row)
    {
        var directions = row % 2 == 0
            ? new (int Column, int Row)[] { (-1, -1), (0, -1), (-1, 0), (1, 0), (-1, 1), (0, 1) }
            : [(0, -1), (1, -1), (-1, 0), (1, 0), (0, 1), (1, 1)];

        return directions
            .Select(direction => (Column: column + direction.Column, Row: row + direction.Row))
            .Where(position =>
                position.Column >= 0 && position.Column < BoardColumns
                && position.Row >= 0 && position.Row < BoardRows);
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
