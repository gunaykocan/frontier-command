// Creates a disposable host for a manual/browser guest to inspect a real fog contact.
// Join the printed match, click Hazırım, retreat the east scout H3 -> I3,
// then click Turu bitir. Moving the observer away leaves E3 in the fog.
const base = process.env.GAME_API_URL ?? "http://127.0.0.1:5080";
const name = `Keşif Görsel ${Date.now()}`;
let cookie = "";
async function request(path, body) {
  const response = await fetch(`${base}${path}`, {
    method: body ? "POST" : "GET",
    headers: { "Content-Type": "application/json", ...(cookie ? { Cookie: cookie } : {}) },
    ...(body ? { body: JSON.stringify(body) } : {}),
  });
  if (!response.ok) throw new Error(`${path}: ${response.status} ${await response.text()}`);
  cookie = response.headers.getSetCookie?.()[0]?.split(";", 1)[0] ?? cookie;
  return response.json();
}
const host = await request("/api/matches", { matchName: name, playerName: "Keşif Test Batı" });
console.log(JSON.stringify({ name, matchId: host.match.id }));
const path = `/api/matches/${host.match.id}`;
let state = host.match;
async function waitUntil(predicate) {
  const deadline = Date.now() + 180_000;
  while (Date.now() < deadline) {
    state = await request(path);
    if (predicate(state)) return;
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  throw new Error("Visual test timed out waiting for the guest.");
}
async function command(route, body = {}) {
  state = await request(`${path}/${route}`, { ...body, expectedVersion: state.version });
}
await waitUntil((match) => match.status === "Deploying");
await command("deployment/ready");
await waitUntil((match) => match.status === "InProgress");
const scout = state.units.find((unit) => unit.ownerPlayerId === host.playerId && unit.type === "Scout");
for (const column of [2, 3, 4]) {
  await command("moves", { unitId: scout.id, targetColumn: column, targetRow: 2 });
}
await command("turns/end");
console.log("Guest sees the scout on E3. Retreat H3 -> I3, then end the guest turn.");
await waitUntil((match) => match.activePlayerId === host.playerId);
await command("moves", { unitId: scout.id, targetColumn: 3, targetRow: 2 });
console.log("Complete: after retreating, the guest should still see ? on E3, last observed in turn 2, without live health.");
