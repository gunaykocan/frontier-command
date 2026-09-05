import assert from "node:assert/strict";
import { HubConnectionBuilder, HttpTransportType, LogLevel } from "@microsoft/signalr";

const api = process.env.GAME_API_URL ?? "http://127.0.0.1:5080";
const runId = Date.now();
const hostSession = {};
const guestSession = {};
const connections = [];
const pause = milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds));

async function request(path, session, body) {
  const response = await fetch(`${api}${path}`, {
    method: body === undefined ? "GET" : "POST",
    headers: {
      ...(session.cookie ? {Cookie: session.cookie} : {}),
      ...(body === undefined ? {} : {"Content-Type": "application/json"}),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  assert.equal(response.ok, true, `${path}: ${response.status} ${response.ok ? "" : await response.text()}`);
  const cookie = response.headers.getSetCookie?.()[0];
  if (cookie) session.cookie = cookie.split(";", 1)[0];
  return response.json();
}

async function connect(session, matchId, updates) {
  const connection = new HubConnectionBuilder()
    .withUrl(`${api}/hubs/game`, {
      skipNegotiation: true,
      transport: HttpTransportType.WebSockets,
      headers: { Cookie: session.cookie },
    })
    .configureLogging(LogLevel.Warning)
    .build();
  connection.on("ServerReady", () => {});
  connection.on("PlayerJoined", () => {});
  connection.on("PlayerLeft", () => {});
  connection.on("MatchUpdated", snapshot => { updates.push(snapshot); });
  connections.push(connection);
  await connection.start();
  await connection.invoke("JoinMatch", matchId);
  return connection;
}

async function timeoutUpdate(updates, turnNumber, waitUntil) {
  while (Date.now() < waitUntil) {
    const update = updates.find(match => match.turnNumber === turnNumber);
    if (update) return update;
    await pause(100);
  }
  throw new Error(`No automatic turn ${turnNumber} was received in real time.`);
}

try {
  const host = await request("/api/matches", hostSession, {matchName:`Timer E2E ${runId}`,playerName:"Timer West"});
  const id = host.match.id;
  const guest = await request(`/api/matches/${id}/players`, guestSession, {playerName:"Timer East"});
  assert.equal(guest.match.turnExpiresAtUtc, null, "Deployment must remain untimed.");
  let match = await request(`/api/matches/${id}/deployment/ready`, hostSession, {expectedVersion:guest.match.version});
  match = await request(`/api/matches/${id}/deployment/ready`, guestSession, {expectedVersion:match.version});
  assert.equal(match.turnDurationSeconds, 90);
  const deadline = match.turnExpiresAtUtc;
  const initialRemaining = Date.parse(deadline) - Date.parse(match.serverTimeUtc);
  assert(initialRemaining > 85_000 && initialRemaining <= 90_000);

  const hostUpdates = [];
  const guestUpdates = [];
  const hostConnection = await connect(hostSession, id, hostUpdates);
  await connect(guestSession, id, guestUpdates);
  const hostView = await request("/api/session", hostSession);
  const scout = hostView.match.units.find(unit => unit.type === "Scout");
  match = await request(`/api/matches/${id}/moves`, hostSession, {
    unitId:scout.id,targetColumn:2,targetRow:2,expectedVersion:match.version,
  });
  assert.equal(Date.parse(match.turnExpiresAtUtc), Date.parse(deadline), "A move must not extend the turn.");
  const movedVersion = match.version;
  console.log("Waiting for the real 90-second deadline; no turn-end request will be sent.");

  await pause(10_000);
  await hostConnection.stop();
  await connect(hostSession, id, hostUpdates);
  const restored = await request("/api/session", hostSession);
  assert.equal(Date.parse(restored.match.turnExpiresAtUtc), Date.parse(deadline), "Reconnecting must not reset the clock.");
  assert(Date.parse(deadline) - Date.parse(restored.match.serverTimeUtc) < initialRemaining - 8_000);

  const waitUntil = Date.now() + Math.max(0, Date.parse(deadline) - Date.parse(restored.match.serverTimeUtc)) + 10_000;
  const [hostTimeout, guestTimeout] = await Promise.all([
    timeoutUpdate(hostUpdates, 2, waitUntil),
    timeoutUpdate(guestUpdates, 2, waitUntil),
  ]);
  for (const snapshot of [hostTimeout, guestTimeout]) {
    assert.equal(snapshot.activePlayerId, guest.playerId);
    assert.equal(snapshot.version, movedVersion + 1);
    assert.equal(snapshot.events.filter(event => event.type === "TurnTimedOut").length, 1);
    assert.equal(snapshot.events.at(-1).type, "TurnTimedOut");
  }
  assert.equal(hostTimeout.turnExpiresAtUtc, guestTimeout.turnExpiresAtUtc);
  const movedScout = hostTimeout.units.find(unit => unit.id === scout.id);
  assert.deepEqual([movedScout.column, movedScout.row, movedScout.remainingMovement], [2, 2, 0]);
  assert(guestTimeout.units.every(unit => unit.remainingMovement === unit.movementAllowance));

  await Promise.all(connections.map(connection => connection.stop()));
  console.log("First timeout reached both players. Checking the next turn with both players disconnected.");
  const remaining = Date.parse(guestTimeout.turnExpiresAtUtc) - Date.parse(guestTimeout.serverTimeUtc);
  await pause(Math.max(0, remaining) + 2_000);
  const disconnectedResult = await request("/api/session", hostSession);
  assert.equal(disconnectedResult.match.turnNumber, 3);
  assert.equal(disconnectedResult.match.activePlayerId, host.playerId);
  assert.equal(disconnectedResult.match.version, movedVersion + 2);
  assert.equal(disconnectedResult.match.events.filter(event => event.type === "TurnTimedOut").length, 2);
  console.log(JSON.stringify({
    status:"passed",matchId:id,turnDurationSeconds:90,
    automaticTimeoutPublishedToBothPlayers:true,
    reconnectPreservedDeadline:true,
    disconnectedPlayersTimedOut:true,
    movedUnitPreserved:true,
  }, null, 2));
} finally {
  await Promise.allSettled(connections.map(connection => connection.stop()));
}
