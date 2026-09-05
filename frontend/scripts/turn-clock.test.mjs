import assert from "node:assert/strict";
import test from "node:test";
import { remainingTurnSeconds, formatTurnSeconds } from "../src/features/game/turnClock.ts";

const started = "2026-08-27T12:00:00Z";
const deadline = "2026-08-27T12:01:30Z";

test("a new player turn lasts 90 seconds", () => {
  assert.equal(remainingTurnSeconds(deadline, started, 0, 90), 90);
  assert.equal(formatTurnSeconds(90), "01:30");
});
test("elapsed time, not the number of ticks, determines the remaining time", () => {
  assert.equal(remainingTurnSeconds(deadline, started, 75_000, 90), 15);
  assert.equal(remainingTurnSeconds(deadline, started, 89_999, 90), 1);
});
test("expiry stays at zero and never becomes negative", () => {
  assert.equal(remainingTurnSeconds(deadline, started, 90_000, 90), 0);
  assert.equal(remainingTurnSeconds(deadline, started, 900_000, 90), 0);
  assert.equal(formatTurnSeconds(0), "00:00");
});
test("a restored snapshot uses the server time without restarting the turn", () => {
  assert.equal(remainingTurnSeconds(deadline, "2026-08-27T12:01:00Z", 0, 90), 30);
  assert.equal(remainingTurnSeconds(deadline, "2026-08-27T12:01:00Z", 10_000, 90), 20);
});
test("new turns reset to a full 90 seconds", () => {
  assert.equal(remainingTurnSeconds("2026-08-27T12:03:00Z", deadline, 0, 90), 90);
});
test("deployment or malformed clocks cannot grant extra time", () => {
  assert.equal(remainingTurnSeconds(null, started, 0, 90), 0);
  assert.equal(remainingTurnSeconds("invalid", started, 0, 90), 0);
  assert.equal(remainingTurnSeconds(deadline, "2026-08-26T12:00:00Z", 0, 90), 90);
});
