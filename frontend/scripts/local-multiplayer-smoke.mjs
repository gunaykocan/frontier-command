import {
  HubConnectionBuilder,
  HttpTransportType,
  LogLevel,
} from "@microsoft/signalr";

const apiBaseUrl = process.env.GAME_API_URL ?? "http://127.0.0.1:5080";
const runId = new Date().toISOString().replaceAll(/[-:.TZ]/g, "").slice(0, 14);

function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}

function readCookie(response) {
  const values = response.headers.getSetCookie?.() ?? [];
  const header = values[0] ?? response.headers.get("set-cookie");
  return header?.split(";", 1)[0] ?? null;
}

async function send(path, { method = "GET", body, session } = {}) {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    method,
    headers: {
      ...(body === undefined ? {} : { "Content-Type": "application/json" }),
      ...(session?.cookie ? { Cookie: session.cookie } : {}),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  if (!response.ok) {
    const detail = await response.text();
    throw new Error(`${method} ${path} returned ${response.status}: ${detail}`);
  }

  if (session) {
    session.cookie = readCookie(response) ?? session.cookie;
  }

  return response.status === 204 ? undefined : response.json();
}

async function sendExpectingRejection(path, expectedStatus, { method, body, session }) {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    method,
    headers: {
      "Content-Type": "application/json",
      ...(session?.cookie ? { Cookie: session.cookie } : {}),
    },
    body: JSON.stringify(body),
  });
  const problem = await response.json().catch(() => null);

  assert(
    response.status === expectedStatus,
    `${method} ${path} returned ${response.status}; expected ${expectedStatus}.`,
  );
  return problem;
}

function createRealtimeClient(updates, session) {
  const connection = new HubConnectionBuilder()
    .withUrl(`${apiBaseUrl}/hubs/game`, {
      skipNegotiation: true,
      transport: HttpTransportType.WebSockets,
      headers: session.cookie ? { Cookie: session.cookie } : {},
    })
    .configureLogging(LogLevel.Warning)
    .build();

  connection.on("MatchUpdated", (match) => {
    updates.push(match);
  });
  connection.on("ServerReady", () => undefined);
  connection.on("PlayerJoined", () => undefined);
  connection.on("PlayerLeft", () => undefined);
  return connection;
}

async function waitForUpdate(updates, version, label) {
  const timeoutAt = Date.now() + 5_000;

  while (Date.now() < timeoutAt) {
    const match = updates.find((candidate) => candidate.version >= version);
    if (match) return match;
    await new Promise((resolve) => setTimeout(resolve, 25));
  }

  throw new Error(`${label} did not receive match version ${version} over SignalR.`);
}

function adjacentPositions(column, row) {
  const directions = row % 2 === 0
    ? [[-1, -1], [0, -1], [-1, 0], [1, 0], [-1, 1], [0, 1]]
    : [[0, -1], [1, -1], [-1, 0], [1, 0], [0, 1], [1, 1]];

  return directions
    .map(([columnOffset, rowOffset]) => ({
      column: column + columnOffset,
      row: row + rowOffset,
    }))
    .filter((position) =>
      position.column >= 0 && position.column < 9 && position.row >= 0 && position.row < 7);
}

function findPath(match, unitId, target) {
  const unit = match.units.find((candidate) => candidate.id === unitId);
  assert(unit, "The exploration unit is missing.");
  const key = (position) => `${position.column}:${position.row}`;
  const start = { column: unit.column, row: unit.row };
  const occupied = new Set(match.units
    .filter((candidate) => candidate.id !== unitId)
    .map((candidate) => key(candidate)));
  const queue = [start];
  const previous = new Map();
  const visited = new Set([key(start)]);

  while (queue.length > 0) {
    const current = queue.shift();
    if (current.column === target.column && current.row === target.row) break;

    for (const next of adjacentPositions(current.column, current.row)) {
      const nextKey = key(next);
      if (occupied.has(nextKey) || visited.has(nextKey)) continue;
      visited.add(nextKey);
      previous.set(nextKey, current);
      queue.push(next);
    }
  }

  assert(visited.has(key(target)), "No exploration path was found.");
  const path = [target];
  let step = target;

  while (step.column !== start.column || step.row !== start.row) {
    step = previous.get(key(step));
    path.push(step);
  }

  return path.reverse();
}

const hostSession = { cookie: null };
const guestSession = { cookie: null };
const hostUpdates = [];
const guestUpdates = [];
let hostConnection = null;
let guestConnection = null;

try {
  const host = await send("/api/matches", {
    method: "POST",
    session: hostSession,
    body: { matchName: `Docker E2E ${runId}`, playerName: `Atlas ${runId}` },
  });

  assert(hostSession.cookie, "Host session cookie was not issued.");
  assert(host.match.status === "WaitingForPlayers", "New match is not waiting for a guest.");

  hostConnection = createRealtimeClient(hostUpdates, hostSession);
  await hostConnection.start();
  await hostConnection.invoke("JoinMatch", host.match.id);

  const guest = await send(`/api/matches/${host.match.id}/players`, {
    method: "POST",
    session: guestSession,
    body: { playerName: `Poyraz ${runId}` },
  });

  assert(guestSession.cookie, "Guest session cookie was not issued.");
  assert(guest.match.status === "Deploying", "The match did not enter deployment.");
  assert(guest.match.turnExpiresAtUtc === null, "Deployment must not have a turn deadline.");
  assert(guest.match.units.length === 5, "The guest view must contain exactly five starting units.");
  assert(guest.match.units.filter((unit) => unit.type === "Infantry").length === 3,
    "The starting army must contain three infantry.");
  assert(
    guest.match.units.every((unit) => unit.ownerPlayerId === guest.playerId),
    "An enemy unit leaked outside the guest's field of view.",
  );
  assert(
    guest.match.visibleCoordinates.length > 0 && guest.match.visibleCoordinates.length < 63,
    "The deployment view did not publish a limited field of view.",
  );
  assert(
    guest.match.revealedSpecialTiles.length === 0,
    "Hidden special-tile coordinates leaked before discovery.",
  );
  assert(
    guest.match.events.length === 1 && guest.match.events[0].type === "DeploymentStarted",
    "The deployment event was not published.",
  );
  assert(guest.match.terrainTiles.length === 63, "The battlefield terrain was not published.");
  assert(
    guest.match.terrainTiles.some((tile) => tile.type === "Marsh" && tile.movementCost === 3),
    "The battlefield does not expose terrain movement costs.",
  );
  assert(
    guest.match.terrainTiles.every((tile) =>
      tile.blocksVision === (tile.type === "Forest" || tile.type === "Hill")),
    "The battlefield does not expose terrain line-of-sight rules.",
  );
  const anonymousMatchResponse = await fetch(`${apiBaseUrl}/api/matches/${host.match.id}`);
  assert(
    anonymousMatchResponse.status === 401,
    "The player-specific battlefield was exposed without a session.",
  );
  await waitForUpdate(hostUpdates, 1, "Host");

  guestConnection = createRealtimeClient(guestUpdates, guestSession);
  await guestConnection.start();
  await guestConnection.invoke("JoinMatch", host.match.id);

  const hostDeployment = await send("/api/session", { session: hostSession });
  assert(hostDeployment.match.units.length === 5
    && hostDeployment.match.units.filter((unit) => unit.type === "Infantry").length === 3,
    "The host's persisted starting army is not one scout, three infantry and one armor.");

  const hostScout = hostDeployment.match.units.find(
    (unit) => unit.ownerPlayerId === host.playerId && unit.type === "Scout",
  );
  const guestScout = guest.match.units.find(
    (unit) => unit.ownerPlayerId === guest.playerId && unit.type === "Scout",
  );
  const hostArmor = hostDeployment.match.units.find(
    (unit) => unit.ownerPlayerId === host.playerId && unit.type === "Armor",
  );
  const hostInfantry = hostDeployment.match.units.find(
    (unit) => unit.ownerPlayerId === host.playerId && unit.type === "Infantry" && unit.column === 1,
  );
  const guestInfantry = guest.match.units.find(
    (unit) => unit.ownerPlayerId === guest.playerId && unit.type === "Infantry" && unit.column === 7,
  );

  assert(
    hostScout && guestScout && hostArmor && hostInfantry && guestInfantry,
    "Expected units were not deployed.",
  );
  assert(hostScout.visionRange === 3, "Scout vision range was not published.");
  assert(
    guest.match.terrainTiles.find((tile) => tile.column === 7 && tile.row === 1)?.defenseBonus === 1,
    "Forest defense bonus was not published.",
  );

  await send(`/api/matches/${host.match.id}/deployment/placements`, {
    method: "POST",
    session: hostSession,
    body: {
      unitId: hostInfantry.id,
      targetColumn: 0,
      targetRow: 0,
      expectedVersion: 1,
    },
  });
  await send(`/api/matches/${host.match.id}/deployment/placements`, {
    method: "POST",
    session: guestSession,
    body: {
      unitId: guestInfantry.id,
      targetColumn: 8,
      targetRow: 0,
      expectedVersion: 2,
    },
  });
  await send(`/api/matches/${host.match.id}/deployment/ready`, {
    method: "POST",
    session: hostSession,
    body: { expectedVersion: 3 },
  });
  const started = await send(`/api/matches/${host.match.id}/deployment/ready`, {
    method: "POST",
    session: guestSession,
    body: { expectedVersion: 4 },
  });

  assert(started.status === "InProgress", "The battle did not start after both players became ready.");
  assert(started.turnDurationSeconds === 90 && started.turnExpiresAtUtc && started.serverTimeUtc,
    "The battle did not publish its server-controlled 90-second clock.");
  assert(started.players.every((player) => player.isReady), "Player readiness was not persisted.");
  assert(
    started.events.at(-1)?.type === "BattleStarted",
    "Deployment events were not persisted in order.",
  );
  assert(
    !started.events.some((event) =>
      event.type === "UnitPlaced" && event.actorPlayerId === host.playerId),
    "The hidden host deployment leaked into the guest event log.",
  );
  assert(
    started.units.some((unit) => unit.id === guestInfantry.id && unit.column === 8 && unit.row === 0),
    "Guest deployment was not persisted.",
  );

  const terrainRejection = await sendExpectingRejection(
    `/api/matches/${host.match.id}/moves`,
    422,
    {
      method: "POST",
      session: hostSession,
      body: {
        unitId: hostArmor.id,
        targetColumn: 1,
        targetRow: 5,
        expectedVersion: 5,
      },
    },
  );
  assert(
    terrainRejection?.detail?.includes("needs 2 movement points"),
    "Insufficient terrain movement was not rejected by the API.",
  );

  await send(`/api/matches/${host.match.id}/moves`, {
    method: "POST",
    session: hostSession,
    body: {
      unitId: hostScout.id,
      targetColumn: 2,
      targetRow: 2,
      expectedVersion: 5,
    },
  });
  const [hostMoveView, guestMoveView] = await Promise.all([
    waitForUpdate(hostUpdates, 6, "Host"),
    waitForUpdate(guestUpdates, 6, "Guest"),
  ]);
  assert(
    hostMoveView.units.some((unit) => unit.id === hostScout.id)
      && !guestMoveView.units.some((unit) => unit.id === hostScout.id),
    "Personalized real-time views leaked a scout outside enemy vision.",
  );

  await send(`/api/matches/${host.match.id}/turns/end`, {
    method: "POST",
    session: hostSession,
    body: { expectedVersion: 6 },
  });

  for (const [column, expectedVersion] of [[6, 7], [5, 8], [4, 9]]) {
    await send(`/api/matches/${host.match.id}/moves`, {
      method: "POST",
      session: guestSession,
      body: {
        unitId: guestScout.id,
        targetColumn: column,
        targetRow: 2,
        expectedVersion,
      },
    });
  }

  await send(`/api/matches/${host.match.id}/turns/end`, {
    method: "POST",
    session: guestSession,
    body: { expectedVersion: 10 },
  });

  await send(`/api/matches/${host.match.id}/moves`, {
    method: "POST",
    session: hostSession,
    body: {
      unitId: hostScout.id,
      targetColumn: 3,
      targetRow: 2,
      expectedVersion: 11,
    },
  });

  const attacked = await send(`/api/matches/${host.match.id}/attacks`, {
    method: "POST",
    session: hostSession,
    body: {
      attackerUnitId: hostScout.id,
      targetUnitId: guestScout.id,
      expectedVersion: 12,
    },
  });

  const damagedGuestScout = attacked.units.find((unit) => unit.id === guestScout.id);
  assert(damagedGuestScout?.health === 1, "Combat damage was not persisted.");
  const attackEvent = attacked.events.at(-1);
  assert(
    attackEvent?.type === "UnitAttacked"
      && attackEvent.amount === 1
      && attackEvent.toColumn === 4
      && attackEvent.toRow === 2,
    "The combat event was not recorded with its damage and target.",
  );
  assert(
    attacked.events.every((event, index) =>
      index === 0 || event.sequence > attacked.events[index - 1].sequence),
    "Visible match events are not ordered.",
  );

  const [hostFinal, guestFinal] = await Promise.all([
    waitForUpdate(hostUpdates, 13, "Host"),
    waitForUpdate(guestUpdates, 13, "Guest"),
  ]);
  assert(hostFinal.version === 13 && guestFinal.version === 13, "Clients did not converge on version 13.");

  let exploration = await send(`/api/matches/${host.match.id}/turns/end`, {
    method: "POST",
    session: hostSession,
    body: { expectedVersion: attacked.version },
  });
  exploration = await send(`/api/matches/${host.match.id}/turns/end`, {
    method: "POST",
    session: guestSession,
    body: { expectedVersion: exploration.version },
  });
  exploration = (await send("/api/session", { session: hostSession })).match;

  const explorationTargets = [0, 1, 4, 5, 6]
    .flatMap((row) => [2, 3, 4, 5, 6].map((column) => ({ column, row })));

  for (const target of explorationTargets) {
    while (exploration.revealedSpecialTiles.length === 0) {
      const currentScout = exploration.units.find((unit) => unit.id === hostScout.id);
      assert(currentScout, "The host scout was lost before discovering a special tile.");
      if (currentScout.column === target.column && currentScout.row === target.row) break;

      const path = findPath(exploration, hostScout.id, target);
      const next = path[1];
      const movementCost = exploration.terrainTiles.find(
        (tile) => tile.column === next.column && tile.row === next.row,
      )?.movementCost;
      assert(movementCost, "The next exploration terrain is missing.");

      if (currentScout.remainingMovement < movementCost) {
        exploration = await send(`/api/matches/${host.match.id}/turns/end`, {
          method: "POST",
          session: hostSession,
          body: { expectedVersion: exploration.version },
        });
        exploration = await send(`/api/matches/${host.match.id}/turns/end`, {
          method: "POST",
          session: guestSession,
          body: { expectedVersion: exploration.version },
        });
        exploration = (await send("/api/session", { session: hostSession })).match;
        continue;
      }

      exploration = await send(`/api/matches/${host.match.id}/moves`, {
        method: "POST",
        session: hostSession,
        body: {
          unitId: hostScout.id,
          targetColumn: next.column,
          targetRow: next.row,
          expectedVersion: exploration.version,
        },
      });
    }

    if (exploration.revealedSpecialTiles.length > 0) break;
  }

  assert(exploration.revealedSpecialTiles.length === 1, "No hidden special tile was discovered.");
  const specialEvent = exploration.events.at(-1);
  assert(
    specialEvent?.type === "MineTriggered" || specialEvent?.type === "ReinforcementTriggered",
    "The discovered tile did not emit a special event.",
  );
  const revealedTile = exploration.revealedSpecialTiles[0];
  const eventTileColumn = specialEvent.type === "MineTriggered"
    ? specialEvent.toColumn
    : specialEvent.fromColumn;
  const eventTileRow = specialEvent.type === "MineTriggered"
    ? specialEvent.toRow
    : specialEvent.fromRow;
  assert(
    revealedTile.column === eventTileColumn && revealedTile.row === eventTileRow,
    "The revealed tile and special event coordinates do not match.",
  );

  await Promise.all([
    waitForUpdate(hostUpdates, exploration.version, "Host"),
    waitForUpdate(guestUpdates, exploration.version, "Guest"),
  ]);

  const restoredHost = await send("/api/session", { session: hostSession });
  const restoredGuest = await send("/api/session", { session: guestSession });
  assert(restoredHost.playerId === host.playerId, "Host session was not restored.");
  assert(restoredGuest.playerId === guest.playerId, "Guest session was not restored.");
  assert(restoredHost.match.version === exploration.version, "Restored match state is stale.");
  assert(
    restoredHost.match.revealedSpecialTiles.length === 1,
    "The discovered special tile was not restored with the session.",
  );

  const completedFlowMatchId = restoredHost.match.id;
  const initialHostRealtimeUpdates = hostUpdates.length;
  const initialGuestRealtimeUpdates = guestUpdates.length;
  await Promise.all([
    hostConnection.invoke("LeaveMatch", host.match.id),
    guestConnection.invoke("LeaveMatch", host.match.id),
  ]);
  await Promise.all([
    hostConnection.stop(),
    guestConnection.stop(),
  ]);
  await Promise.all([
    send("/api/session", { method: "DELETE", session: hostSession }),
    send("/api/session", { method: "DELETE", session: guestSession }),
  ]);
  const clearedSessionResponse = await fetch(`${apiBaseUrl}/api/session`, {
    headers: { Cookie: hostSession.cookie },
  });
  assert(clearedSessionResponse.status === 401, "The old player session was not cleared.");

  const nextHost = await send("/api/matches", {
    method: "POST",
    session: hostSession,
    body: { matchName: `Replay ${runId}`, playerName: `Atlas ${runId}` },
  });
  const nextGuest = await send(`/api/matches/${nextHost.match.id}/players`, {
    method: "POST",
    session: guestSession,
    body: { playerName: `Poyraz ${runId}` },
  });
  assert(nextHost.match.id !== completedFlowMatchId, "New game reused the previous match identity.");
  assert(nextHost.match.status === "WaitingForPlayers", "New game did not start in the lobby state.");
  assert(nextGuest.match.status === "Deploying", "The second player could not join the new game.");

  hostUpdates.length = 0;
  guestUpdates.length = 0;
  hostConnection = createRealtimeClient(hostUpdates, hostSession);
  guestConnection = createRealtimeClient(guestUpdates, guestSession);
  await Promise.all([
    hostConnection.start(),
    guestConnection.start(),
  ]);
  await Promise.all([
    hostConnection.invoke("JoinMatch", nextHost.match.id),
    guestConnection.invoke("JoinMatch", nextHost.match.id),
  ]);

  await send(`/api/matches/${nextHost.match.id}/deployment/ready`, {
    method: "POST",
    session: hostSession,
    body: { expectedVersion: nextGuest.match.version },
  });
  const guestStartedRematchCandidate = await send(`/api/matches/${nextHost.match.id}/deployment/ready`, {
    method: "POST",
    session: guestSession,
    body: { expectedVersion: nextGuest.match.version + 1 },
  });
  let rematchCandidate = (await send("/api/session", { session: hostSession })).match;
  assert(rematchCandidate.status === "InProgress", "The rematch candidate did not start.");
  assert(
    guestStartedRematchCandidate.status === "InProgress",
    "The guest did not receive the started rematch candidate.",
  );

  const rematchHostScout = rematchCandidate.units.find(
    (unit) => unit.ownerPlayerId === nextHost.playerId && unit.type === "Scout",
  );
  assert(rematchHostScout, "The rematch candidate host scout is missing.");

  const completionPath = [
    { column: 2, row: 2 },
    { column: 3, row: 2 },
    { column: 4, row: 2 },
    { column: 5, row: 2 },
    { column: 6, row: 2 },
    { column: 6, row: 1 },
    { column: 7, row: 1 },
    { column: 8, row: 1 },
  ];

  for (const target of completionPath) {
    const currentScout = rematchCandidate.units.find((unit) => unit.id === rematchHostScout.id);
    assert(currentScout, "The rematch candidate scout was lost before reaching headquarters.");
    const movementCost = rematchCandidate.terrainTiles.find(
      (tile) => tile.column === target.column && tile.row === target.row,
    )?.movementCost;
    assert(movementCost, "Completion path terrain is missing.");

    if (currentScout.remainingMovement < movementCost) {
      rematchCandidate = await send(`/api/matches/${nextHost.match.id}/turns/end`, {
        method: "POST",
        session: hostSession,
        body: { expectedVersion: rematchCandidate.version },
      });
      rematchCandidate = await send(`/api/matches/${nextHost.match.id}/turns/end`, {
        method: "POST",
        session: guestSession,
        body: { expectedVersion: rematchCandidate.version },
      });
    }

    rematchCandidate = await send(`/api/matches/${nextHost.match.id}/moves`, {
      method: "POST",
      session: hostSession,
      body: {
        unitId: rematchHostScout.id,
        targetColumn: target.column,
        targetRow: target.row,
        expectedVersion: rematchCandidate.version,
      },
    });
  }

  assert(rematchCandidate.status === "Completed", "The rematch candidate did not finish.");
  assert(rematchCandidate.turnExpiresAtUtc === null, "Completed matches must stop their clock.");
  assert(rematchCandidate.winnerPlayerId === nextHost.playerId, "The expected player did not win.");
  assert(rematchCandidate.completedAtUtc, "The match completion time was not persisted.");

  hostUpdates.length = 0;
  guestUpdates.length = 0;

  const rematchRequested = await send(`/api/matches/${nextHost.match.id}/rematch/request`, {
    method: "POST",
    session: hostSession,
    body: { expectedVersion: rematchCandidate.version },
  });
  assert(
    rematchRequested.rematchRequestedByPlayerId === nextHost.playerId,
    "The rematch request was not attributed to its player.",
  );
  assert(!rematchRequested.rematchMatchId, "A rematch was created before the opponent accepted.");
  await Promise.all([
    waitForUpdate(hostUpdates, rematchRequested.version, "Rematch requester"),
    waitForUpdate(guestUpdates, rematchRequested.version, "Rematch opponent"),
  ]);

  const acceptedRematch = await send(`/api/matches/${nextHost.match.id}/rematch/accept`, {
    method: "POST",
    session: guestSession,
    body: { expectedVersion: rematchRequested.version },
  });
  const [hostAcceptedUpdate, guestAcceptedUpdate] = await Promise.all([
    waitForUpdate(hostUpdates, rematchRequested.version + 1, "Rematch requester"),
    waitForUpdate(guestUpdates, rematchRequested.version + 1, "Rematch opponent"),
  ]);
  assert(
    hostAcceptedUpdate.rematchMatchId === acceptedRematch.match.id
      && guestAcceptedUpdate.rematchMatchId === acceptedRematch.match.id,
    "The accepted rematch was not published to both players in real time.",
  );
  const linkedOldMatch = await send("/api/session", { session: hostSession });
  assert(
    linkedOldMatch.match.rematchMatchId === acceptedRematch.match.id,
    "The completed match was not linked to its rematch.",
  );

  const enteredRematch = await send(`/api/matches/${nextHost.match.id}/rematch/enter`, {
    method: "POST",
    session: hostSession,
  });
  assert(enteredRematch.match.id === acceptedRematch.match.id, "Players entered different rematches.");
  assert(enteredRematch.match.status === "Deploying", "The accepted rematch is not in deployment.");
  for (const playerView of [enteredRematch.match, acceptedRematch.match]) {
    assert(playerView.units.length === 5
      && playerView.units.filter((unit) => unit.type === "Scout").length === 1
      && playerView.units.filter((unit) => unit.type === "Infantry").length === 3
      && playerView.units.filter((unit) => unit.type === "Armor").length === 1,
      "The rematch did not recreate a five-unit army for each player.");
  }
  assert(enteredRematch.match.lastKnownEnemies.length === 0, "A rematch inherited old enemy contacts.");
  assert(enteredRematch.match.turnExpiresAtUtc === null, "A rematch inherited the old turn clock.");
  assert(
    acceptedRematch.match.players.find((player) => player.id === acceptedRematch.playerId)?.seat === 1,
    "The accepting guest did not move to the first side.",
  );
  assert(
    enteredRematch.match.players.find((player) => player.id === enteredRematch.playerId)?.seat === 2,
    "The requesting host did not move to the second side.",
  );
  assert(
    acceptedRematch.match.players.find((player) => player.seat === 1)?.name === `Poyraz ${runId}`
      && acceptedRematch.match.players.find((player) => player.seat === 2)?.name === `Atlas ${runId}`,
    "Player names were not preserved while swapping sides.",
  );

  // Rematch cookies contain new player identities; reconnect before joining its private views.
  await Promise.all([hostConnection.stop(), guestConnection.stop()]);
  hostUpdates.length = 0;
  guestUpdates.length = 0;
  hostConnection = createRealtimeClient(hostUpdates, hostSession);
  guestConnection = createRealtimeClient(guestUpdates, guestSession);
  await Promise.all([hostConnection.start(), guestConnection.start()]);
  await Promise.all([
    hostConnection.invoke("JoinMatch", acceptedRematch.match.id),
    guestConnection.invoke("JoinMatch", acceptedRematch.match.id),
  ]);

  const westScout = acceptedRematch.match.units.find((unit) => unit.type === "Scout");
  const eastScout = enteredRematch.match.units.find((unit) => unit.type === "Scout");
  assert(westScout && eastScout, "The line-of-sight test scouts are missing.");
  let sightState = acceptedRematch.match;
  async function sightCommand(session, path, body = {}) {
    sightState = await send(`/api/matches/${acceptedRematch.match.id}/${path}`, {
      method: "POST",
      session,
      body: { ...body, expectedVersion: sightState.version },
    });
    return sightState;
  }
  const moveScout = (session, scout, column, row) => sightCommand(session, "moves", {
    unitId: scout.id,
    targetColumn: column,
    targetRow: row,
  });
  const seesEastScout = (match) => match.units.some((unit) => unit.id === eastScout.id);
  const assertTerrainHidden = (match) => {
    assert(!seesEastScout(match), "A scout behind the hill leaked into the west player's view.");
    assert(
      !match.visibleCoordinates.some((position) => position.column === 4 && position.row === 3),
      "The hill did not hide the cell behind it.",
    );
    assert(
      !match.events.some((event) => event.toColumn === 4 && event.toRow === 3),
      "A movement event leaked the hidden scout's position.",
    );
  };

  // The accepting guest is now west; rows 2 and 3 cannot contain hidden special tiles.
  await sightCommand(guestSession, "deployment/ready");
  await sightCommand(hostSession, "deployment/ready");
  await moveScout(guestSession, westScout, 2, 2);
  await moveScout(guestSession, westScout, 2, 3);
  await sightCommand(guestSession, "turns/end");
  await moveScout(hostSession, eastScout, 6, 2);
  await moveScout(hostSession, eastScout, 5, 2);
  await sightCommand(hostSession, "turns/end");
  await sightCommand(guestSession, "turns/end");
  await moveScout(hostSession, eastScout, 4, 3);
  assertTerrainHidden((await send("/api/session", { session: guestSession })).match);
  assertTerrainHidden(await waitForUpdate(guestUpdates, sightState.version, "West terrain view"));

  await sightCommand(hostSession, "turns/end");
  await moveScout(guestSession, westScout, 2, 2);
  await moveScout(guestSession, westScout, 3, 2);
  assert(seesEastScout(sightState), "The scout stayed hidden after opening a clear line of sight.");
  assert(!sightState.events.some((event) => event.unitId === eastScout.id && event.toColumn === 4 && event.toRow === 3),
    "New vision revealed a historical hidden move retroactively.");
  assert(
    seesEastScout(await waitForUpdate(guestUpdates, sightState.version, "West flanking view")),
    "The revealed scout was not published over the rematch's private real-time connection.",
  );
  await moveScout(guestSession, westScout, 2, 3);
  assertTerrainHidden(sightState);
  assertTerrainHidden(await waitForUpdate(guestUpdates, sightState.version, "West obscured view"));

  const contact = sightState.lastKnownEnemies.find((item) => item.unitId === eastScout.id);
  assert(contact?.column === 4 && contact.row === 3 && contact.lastSeenTurnNumber === 5,
    "Losing sight did not preserve the last observed enemy position and turn.");
  assert(Object.keys(contact).sort().join(",") === "column,lastSeenTurnNumber,row,unitId,unitType",
    "A historical contact contains live enemy fields.");
  await sightCommand(guestSession, "turns/end");
  await moveScout(hostSession, eastScout, 5, 3);
  await moveScout(hostSession, eastScout, 6, 3);
  const persistedContacts = (await send("/api/session", { session: guestSession })).match;
  assert(JSON.stringify(persistedContacts.lastKnownEnemies.find((item) => item.unitId === eastScout.id)) === JSON.stringify(contact),
    "A persisted contact followed the enemy's hidden movement.");
  assert(!seesEastScout(persistedContacts), "A hidden moving enemy leaked to the opponent.");

  // Reconnect uses a fresh server read, not the browser's previous contact array.
  await guestConnection.stop();
  guestUpdates.length = 0;
  guestConnection = createRealtimeClient(guestUpdates, guestSession);
  await guestConnection.start();
  await guestConnection.invoke("JoinMatch", acceptedRematch.match.id);
  const reconnectedView = (await send("/api/session", { session: guestSession })).match;
  assert(JSON.stringify(reconnectedView.lastKnownEnemies.find((item) => item.unitId === eastScout.id)) === JSON.stringify(contact),
    "The last-known contact was lost after reconnecting.");

  await sightCommand(hostSession, "turns/end");
  const reconnectedUpdate = await waitForUpdate(guestUpdates, sightState.version, "Recon reconnect");
  assert(JSON.stringify(reconnectedUpdate.lastKnownEnemies.find((item) => item.unitId === eastScout.id)) === JSON.stringify(contact),
    "The private real-time update lost the contact after reconnecting.");
  await moveScout(guestSession, westScout, 2, 2);
  await moveScout(guestSession, westScout, 3, 2);
  assert(!sightState.lastKnownEnemies.some((item) => item.unitId === eastScout.id),
    "Re-scouting an empty old position did not clear its contact.");
  assert(!seesEastScout(sightState), "Clearing an old contact exposed the enemy's current location.");
  const clearedContacts = (await send("/api/session", { session: guestSession })).match;
  assert(!clearedContacts.lastKnownEnemies.some((item) => item.unitId === eastScout.id),
    "The cleared contact remained in PostgreSQL.");

  console.log(JSON.stringify({
    status: "passed",
    matchId: host.match.id,
    version: restoredHost.match.version,
    units: restoredHost.match.units.length,
    hostRealtimeUpdates: initialHostRealtimeUpdates,
    guestRealtimeUpdates: initialGuestRealtimeUpdates,
    damagedUnitHealth: damagedGuestScout.health,
    events: restoredHost.match.events.length,
    latestEvent: restoredHost.match.events.at(-1)?.type,
    revealedSpecialTiles: restoredHost.match.revealedSpecialTiles.length,
    newMatchId: nextHost.match.id,
    completedAtUtc: rematchCandidate.completedAtUtc,
    rematchMatchId: acceptedRematch.match.id,
    rematchSidesSwapped: true,
    rematchPublishedToBothPlayers: true,
    fogOfWarVerified: true,
    terrainLineOfSightVerified: true,
    reconnaissanceMemoryVerified: true,
    reconnaissanceReconnectVerified: true,
    historicalEventPrivacyVerified: true,
    anonymousBattlefieldBlocked: anonymousMatchResponse.status === 401,
    oldSessionCleared: clearedSessionResponse.status === 401,
  }, null, 2));
} finally {
  await Promise.allSettled([
    hostConnection?.stop(),
    guestConnection?.stop(),
  ]);
}
