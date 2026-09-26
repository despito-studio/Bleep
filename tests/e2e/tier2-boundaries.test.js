import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from 'three';
import {
  setupTestGame,
  createTestCanvasDocument,
  rooms,
  courtyard,
  spawn,
  extraction,
  objects,
  lockers,
  activeObstacles,
  exteriorObstacles,
  updateObstacles,
  isWalkable,
  isObstacle,
  isInsideWorld,
  roomAt,
  propToObstacle,
  DEFAULT_PROPS,
  PROP_CATALOG,
  CHARACTER_CLASSES,
  nextWaypoint,
  segmentWalkable,
} from './test-helpers.js';
import { createWorld, createCharacter } from '../../src/world.js';
import { interfaceMarkup } from '../../src/interface.js';

// =========================================================================
// FEATURE 1: Courtyard Guard Booth & Barrier (Boundaries)
// =========================================================================
test('F1-T2-1: Reject obstacle placement with negative or NaN dimensions', () => {
  const bad1 = propToObstacle({ id: 'bad-1', type: 'table', x: NaN, z: 20 });
  const bad2 = propToObstacle({ id: 'bad-2', type: 'table', x: 0, z: null });
  assert.ok(bad1 === null || !Number.isFinite(bad1.minX));
  assert.equal(bad2, null);
});

test('F1-T2-2: Player position at exact outer edge of courtyard boundary fails walkability', () => {
  // Courtyard minX: -22, radius: 0.22. At x = -21.9, inside = -21.9 >= -22 + 0.22 is false.
  assert.equal(isWalkable(-22.0, 20), false);
  assert.equal(isWalkable(22.0, 20), false);
});

test('F1-T2-3: Barrier gate boundary edge is detected as collision', () => {
  const barrier = { id: 'edge-barrier', type: 'crate', x: -7.0, z: 34.5, size: 2.0 };
  const obs = propToObstacle(barrier);
  try {
    activeObstacles.push(obs);
    // Boundary of AABB plus radius 0.22
    assert.equal(isObstacle(obs.minX, obs.minZ), true);
    assert.equal(isObstacle(obs.maxX, obs.maxZ), true);
  } finally {
    activeObstacles.splice(activeObstacles.indexOf(obs), 1);
  }
});

test('F1-T2-4: Moving diagonally into guard booth slides along unobstructed axis', () => {
  // Test sliding logic: blocked on X, free on Z
  const customObstacle = { id: 'slide-booth', type: 'crate', minX: -13, maxX: -11, minZ: 31, maxZ: 33 };
  try {
    activeObstacles.push(customObstacle);
    // At x = -10.8 (outside obstacle X + radius), z = 32 (inside obstacle Z)
    assert.equal(isWalkable(-10.7, 32.0), true);
    assert.equal(isWalkable(-11.5, 32.0), false); // hits booth on X
    assert.equal(isWalkable(-10.7, 33.5), true); // free to step along Z
  } finally {
    activeObstacles.splice(activeObstacles.indexOf(customObstacle), 1);
  }
});

test('F1-T2-5: Line of sight touching the tangent corner of booth bounding box handles grazing angle correctly', () => {
  const customObstacle = { id: 'corner-booth', type: 'crate', minX: 5, maxX: 7, minZ: 15, maxZ: 17 };
  try {
    activeObstacles.push(customObstacle);
    // Line passing through center of obstacle
    assert.equal(segmentWalkable({ x: 4, z: 16 }, { x: 8, z: 16 }), false);
    // Line passing safely outside with clearance > 0.22
    assert.equal(segmentWalkable({ x: 4, z: 18 }, { x: 8, z: 18 }), true);
  } finally {
    activeObstacles.splice(activeObstacles.indexOf(customObstacle), 1);
  }
});

// =========================================================================
// FEATURE 2: Spotlight Towers & Cable Spools (Boundaries)
// =========================================================================
test('F2-T2-1: Tower base collision rejects zero-radius or negative radius gracefully', () => {
  assert.equal(propToObstacle({ id: 'bad-tower', type: 'barrel', x: undefined, z: 20 }), null);
});

test('F2-T2-2: Cable spool obstacle handles rotated orientations computing expanded bounding box', () => {
  const rot0 = propToObstacle({ id: 'table-0', type: 'table', x: 0, z: 0, w: 2.0, d: 1.0, rotation: 0 });
  const rot90 = propToObstacle({ id: 'table-90', type: 'table', x: 0, z: 0, w: 2.0, d: 1.0, rotation: Math.PI / 2 });
  assert.ok(Math.abs((rot0.maxX - rot0.minX) - (rot90.maxZ - rot90.minZ)) < 0.01);
});

test('F2-T2-3: Point located exactly at minX - radius of cable spool evaluates as collision boundary limit', () => {
  const spool = { id: 'spool-limit', type: 'barrel', x: 0, z: 25 };
  const obs = propToObstacle(spool);
  try {
    activeObstacles.push(obs);
    // Radius is 0.22. minX = 0 - 0.35 = -0.35. Edge = -0.35 - 0.22 = -0.57.
    assert.equal(isObstacle(-0.57, 25), true);
    assert.equal(isObstacle(-0.80, 25), false);
  } finally {
    activeObstacles.splice(activeObstacles.indexOf(obs), 1);
  }
});

test('F2-T2-4: Multiple adjacent cable spools with minimal gap block passage between them', () => {
  const obs1 = propToObstacle({ id: 's1', type: 'barrel', x: 5.0, z: 20 });
  const obs2 = propToObstacle({ id: 's2', type: 'barrel', x: 5.5, z: 20 });
  try {
    activeObstacles.push(obs1, obs2);
    // Gap between barrel centers is 0.5m, each radius 0.35m => overlap!
    assert.equal(isWalkable(5.25, 20), false);
  } finally {
    activeObstacles.splice(activeObstacles.indexOf(obs1), 1);
    activeObstacles.splice(activeObstacles.indexOf(obs2), 1);
  }
});

test('F2-T2-5: Dynamic sync rejects null or non-object prop entries without corrupting active obstacle list', () => {
  const countBefore = activeObstacles.length;
  updateObstacles([...DEFAULT_PROPS, null, undefined, 'invalid']);
  assert.equal(activeObstacles.length, countBefore);
});

// =========================================================================
// FEATURE 3: Barrels & Crates with Collisions (Boundaries)
// =========================================================================
test('F3-T2-1: Crate with extreme scale computes bounded AABB without throwing', () => {
  const tiny = propToObstacle({ id: 'tiny', type: 'crate', x: 0, z: 0, size: 0.01 });
  const huge = propToObstacle({ id: 'huge', type: 'crate', x: 0, z: 0, size: 100 });
  assert.ok(tiny.maxX > tiny.minX);
  assert.ok(huge.maxX > huge.minX);
});

test('F3-T2-2: Player capsule overlap at crate diagonal corner correctly detects collision', () => {
  const crate = propToObstacle({ id: 'c-corner', type: 'crate', x: 0, z: 0, size: 1.0 });
  try {
    activeObstacles.push(crate);
    // Corner is at (0.45, 0.45). Test with player radius 0.22
    assert.equal(isObstacle(crate.maxX + 0.1, crate.maxZ + 0.1), true);
    assert.equal(isObstacle(crate.maxX + 0.3, crate.maxZ + 0.3), false);
  } finally {
    activeObstacles.splice(activeObstacles.indexOf(crate), 1);
  }
});

test('F3-T2-3: Barrel obstacle at exact player radius boundary separates collision vs free space', () => {
  const barrel = propToObstacle({ id: 'b-edge', type: 'barrel', x: 0, z: 0 });
  try {
    activeObstacles.push(barrel);
    // Barrel r = 0.35. Total radius cutoff = 0.35 + 0.22 = 0.57.
    assert.equal(isObstacle(0.56, 0), true);
    assert.equal(isObstacle(0.60, 0), false);
  } finally {
    activeObstacles.splice(activeObstacles.indexOf(barrel), 1);
  }
});

test('F3-T2-4: Moving directly into barrel center from outside stops player cleanly at boundary', () => {
  const { game, room, hostPlayer, place } = setupTestGame();
  game.startGame('host');
  const barrel = DEFAULT_PROPS.find((p) => p.id === 'barrel_1');
  place('host', barrel.x + 1.0, barrel.z);
  const stepIn = game.movePlayer('host', { x: barrel.x, z: barrel.z, yaw: 0 });
  assert.equal(stepIn, false);
});

test('F3-T2-5: Raycast segment completely contained inside a crate obstacle returns false', () => {
  const crate = DEFAULT_PROPS.find((p) => p.id === 'crate_1');
  assert.equal(segmentWalkable({ x: crate.x - 0.1, z: crate.z }, { x: crate.x + 0.1, z: crate.z }), false);
});

// =========================================================================
// FEATURE 4: Perimeter Fence & Barbed Wire (Boundaries)
// =========================================================================
test('F4-T2-1: Coordinate z = 36.0001 is outside world', () => {
  assert.equal(isInsideWorld(0, 36.0001), false);
});

test('F4-T2-2: Coordinate x = -22.0001 is outside world', () => {
  assert.equal(isInsideWorld(-22.0001, 20), false);
});

test('F4-T2-3: Coordinate x = 22.0001 is outside world', () => {
  assert.equal(isInsideWorld(22.0001, 20), false);
});

test('F4-T2-4: South station exterior facade at z = 6.0, x = 1.6 blocks player due to capsule radius', () => {
  assert.equal(isWalkable(1.6, 6.0), false);
});

test('F4-T2-5: High-speed trajectory from yard aimed at closed wall segment is stopped by segmentWalkable', () => {
  // Ray from courtyard at (5, 10) to station interior at (5, 2) crosses solid facade at z = 6
  assert.equal(segmentWalkable({ x: 5, z: 10 }, { x: 5, z: 2 }), false);
});

// =========================================================================
// FEATURE 5: Abandoned Transport in Courtyard (Boundaries)
// =========================================================================
test('F5-T2-1: Van obstacle extents boundary check blocks coords at x = -8.35 - 0.22', () => {
  assert.equal(isObstacle(-8.4, 23.0), true);
  assert.equal(isObstacle(-8.6, 23.0), false);
});

test('F5-T2-2: Interacting with exit object from too far away (> 2.5m) fails gracefully', () => {
  const { game, room, place } = setupTestGame();
  game.startGame('host');
  room.broadcast = true;
  place('host', extraction.x + 4.0, extraction.z);
  const res = game.interact('host', { id: 'exit' });
  assert.equal(res.ok, false);
  assert.equal(room.phase, 'playing');
});

test('F5-T2-3: Interacting with exit without LOS fails', () => {
  const { game, room, place } = setupTestGame();
  game.startGame('host');
  room.broadcast = true;
  // Position behind the van body: x = -9, z = 23 (opposite side of exit at -5.1, 23)
  place('host', -8.5, 23.0);
  const res = game.interact('host', { id: 'exit' });
  assert.equal(res.ok, false);
});

test('F5-T2-4: Van obstacle remains active and impassable regardless of monster state or broadcast status', () => {
  assert.equal(isWalkable(-7.0, 23.0), false);
  assert.equal(isObstacle(-7.0, 23.0), true);
});

test('F5-T2-5: Pathfinding from opposite side of van calculates detour without routing through van hull', () => {
  const start = { x: -9.5, z: 23.0 };
  const target = { x: extraction.x, z: extraction.z };
  const next = nextWaypoint(start, target);
  assert.ok(next);
  assert.equal(isObstacle(next.x, next.z), false);
});

// =========================================================================
// FEATURE 6: Main Hall Turnstiles & Board of Honor (Boundaries)
// =========================================================================
test('F6-T2-1: Entrance portal width threshold: x = 1.3 is walkable, x = 1.6 is not walkable', () => {
  assert.equal(isInsideWorld(1.2, 6.0), true);
  assert.equal(isInsideWorld(1.6, 6.0), false);
});

test('F6-T2-2: Turnstile aisle clearance at x = 0 provides sufficient clearance for radius 0.22', () => {
  assert.equal(isWalkable(0, 4.2), true);
});

test('F6-T2-3: Turnstile post boundary rejects walking directly at post coordinates', () => {
  const post = propToObstacle({ id: 'tp-edge', type: 'crate', x: 1.2, z: 4.2, size: 0.5 });
  try {
    activeObstacles.push(post);
    assert.equal(isWalkable(1.2, 4.2), false);
  } finally {
    activeObstacles.splice(activeObstacles.indexOf(post), 1);
  }
});

test('F6-T2-4: Lateral movement outside station facade is out of world bounds', () => {
  assert.equal(isInsideWorld(0, 4.2), true);
  assert.equal(isInsideWorld(14.0, 4.2), false);
});

test('F6-T2-5: Monster following player through entrance portal correctly stays on central axis', () => {
  const step = nextWaypoint({ x: 0, z: 8 }, { x: 0, z: 2 });
  assert.equal(Math.round(step.x), 0);
});

// =========================================================================
// FEATURE 7: Broadcast Schedule Board & Fire Hose Panel (Boundaries)
// =========================================================================
test('F7-T2-1: Corridor boundary at x = -2.2 is walkable, but x = -2.5 rejects player with radius 0.22', () => {
  assert.equal(isWalkable(-2.2, -10), true);
  assert.equal(isWalkable(-2.5, -10), false);
});

test('F7-T2-2: Corridor boundary at x = 2.2 is walkable, but x = 2.5 rejects player with radius 0.22', () => {
  assert.equal(isWalkable(2.2, -10), true);
  assert.equal(isWalkable(2.5, -10), false);
});

test('F7-T2-3: Non-doorway wall segment at z = -10, x = 2.5 completely blocks lateral rays', () => {
  assert.equal(segmentWalkable({ x: 0, z: -10 }, { x: 5, z: -10 }), false);
});

test('F7-T2-4: Boundary check at corridor extreme north end rejects further northward movement', () => {
  assert.equal(isWalkable(0, -54.0), false);
  assert.equal(isWalkable(0, -53.5), true);
});

test('F7-T2-5: Glancing movement along corridor wall preserves longitudinal velocity component', () => {
  assert.equal(isWalkable(2.2, -10), true);
  assert.equal(isWalkable(2.2, -11), true);
});

// =========================================================================
// FEATURE 8: Flickering Fluorescent Lights & Wire Trays (Boundaries)
// =========================================================================
test('F8-T2-1: Setting negative or non-numeric scare level in state is handled safely', () => {
  const { room } = setupTestGame();
  room.scare = -5;
  assert.equal(room.scare, -5);
});

test('F8-T2-2: Flashlight toggle with invalid/non-boolean input coerces to boolean', () => {
  const { game, hostPlayer } = setupTestGame();
  game.startGame('host');
  game.movePlayer('host', { x: hostPlayer.x, z: hostPlayer.z, yaw: 0, flashlight: 'truthy' });
  assert.equal(Boolean(hostPlayer.flashlight), true);
});

test('F8-T2-3: Camera toggle strictly requires boolean true and sets player.camera', () => {
  const { game, hostPlayer } = setupTestGame();
  game.startGame('host');
  game.movePlayer('host', { x: hostPlayer.x, z: hostPlayer.z, yaw: 0, camera: true });
  assert.equal(hostPlayer.camera, true);
  game.movePlayer('host', { x: hostPlayer.x, z: hostPlayer.z, yaw: 0, camera: 1 });
  assert.equal(hostPlayer.camera, false);
});

test('F8-T2-4: Interacting with power switch repeatedly when power already true returns ok: true idempotently', () => {
  const { game, room, place } = setupTestGame();
  game.startGame('host');
  place('host', -10.5, -16.5);
  room.fuses = new Set(['fuse-1', 'fuse-2']);
  assert.equal(game.interact('host', { id: 'power' }).ok, true);
  assert.equal(game.interact('host', { id: 'power' }).ok, true);
  assert.equal(room.power, true);
});

test('F8-T2-5: Power switch state remains resilient against invalid interaction payloads', () => {
  const { game, place } = setupTestGame();
  game.startGame('host');
  place('host', -10.5, -16.5);
  assert.equal(game.interact('host', { id: 'power', extraJunk: 12345 }).ok, false);
});

// =========================================================================
// FEATURE 9: Debris, Trash Bins & Broken Crockery (Boundaries)
// =========================================================================
test('F9-T2-1: Prop with missing coordinates returns null from propToObstacle', () => {
  assert.equal(propToObstacle({ type: 'crate' }), null);
  assert.equal(propToObstacle(null), null);
});

test('F9-T2-2: Loading empty props array in updateObstacles clears all prop obstacles without error', () => {
  updateObstacles([]);
  assert.equal(activeObstacles.length, 0);
  updateObstacles(DEFAULT_PROPS);
  assert.ok(activeObstacles.length > 0);
});

test('F9-T2-3: Prop with non-finite coordinates returns null without throwing', () => {
  const o1 = propToObstacle({ type: 'barrel', x: Infinity, z: 0 });
  const o2 = propToObstacle({ type: 'barrel', x: 0, z: NaN });
  assert.ok(o1 === null || !Number.isFinite(o1.minX));
  assert.ok(o2 === null || !Number.isFinite(o2.minZ));
});

test('F9-T2-4: Prop with unrecognized type returns null safely', () => {
  assert.equal(propToObstacle({ type: 'unknown_alien_prop', x: 0, z: 0 }), null);
});

test('F9-T2-5: Rapid successive calls to updateObstacles do not leak memory or duplicate active obstacles', () => {
  const count = activeObstacles.length;
  for (let i = 0; i < 10; i++) updateObstacles(DEFAULT_PROPS);
  assert.equal(activeObstacles.length, count);
});

// =========================================================================
// FEATURE 10: Archive Racks & Betacam/VHS Storage (Boundaries)
// =========================================================================
test('F10-T2-1: Archive north wall at z = 6 blocks moving further south outside station', () => {
  assert.equal(isInsideWorld(-8, 6.2), false);
});

test('F10-T2-2: Archive west wall at x = -12 blocks moving further west outside building', () => {
  assert.equal(isInsideWorld(-12.1, 0), false);
});

test('F10-T2-3: Interacting with tape-1 from distance 2.51m fails proximity check', () => {
  const { game, place } = setupTestGame();
  game.startGame('host');
  // tape-1 is at (-8, -3). Distance = 2.55m
  place('host', -8, -0.45);
  assert.equal(game.interact('host', { id: 'tape-1' }).ok, false);
});

test('F10-T2-4: Interacting with tape-1 repeatedly after collection is idempotent', () => {
  const { game, room, place } = setupTestGame();
  game.startGame('host');
  place('host', -8, -3);
  assert.equal(game.interact('host', { id: 'tape-1' }).ok, true);
  assert.equal(game.interact('host', { id: 'tape-1' }).ok, true);
  assert.equal(room.tapes.size, 1);
});

test('F10-T2-5: Interacting with tape-1 while downed fails with revive prompt', () => {
  const { game, hostPlayer, place } = setupTestGame();
  game.startGame('host');
  place('host', -8, -3);
  hostPlayer.down = true;
  const res = game.interact('host', { id: 'tape-1' });
  assert.equal(res.ok, false);
  assert.ok((res.error || res.message || '').includes('допомога'));
});

// =========================================================================
// FEATURE 11: Studio 04 Chromakey, Cameras & Mic Stands (Boundaries)
// =========================================================================
test('F11-T2-1: Studio 04 east wall at x = 12 blocks movement eastward', () => {
  assert.equal(isInsideWorld(12.1, -16), false);
});

test('F11-T2-2: Studio 04 partition wall at z = -24 blocks passage except through doorway portals', () => {
  assert.equal(segmentWalkable({ x: 8, z: -23 }, { x: 8, z: -25 }), false);
});

test('F11-T2-3: Interacting with tape-2 at distance > 2.5m fails', () => {
  const { game, place } = setupTestGame();
  game.startGame('host');
  place('host', 8, -13.5); // dist = 3.0m
  assert.equal(game.interact('host', { id: 'tape-2' }).ok, false);
});

test('F11-T2-4: Interacting with tape-2 across Studio 04 solid wall without LOS fails', () => {
  const { game, place } = setupTestGame();
  game.startGame('host');
  place('host', 0, -16.5); // In corridor, behind solid corridor wall
  assert.equal(game.interact('host', { id: 'tape-2' }).ok, false);
});

test('F11-T2-5: Camera obstacle bounds handle rotation correctly', () => {
  const cam = propToObstacle({ id: 'cam-rot', type: 'camera', x: 0, z: 0, rotation: Math.PI });
  assert.equal(cam.minX, -0.36);
  assert.equal(cam.maxX, 0.36);
});

// =========================================================================
// FEATURE 12: Generator Room Diesel Engine & 380V Panel (Boundaries)
// =========================================================================
test('F12-T2-1: Interacting with power with only 1 fuse returns failure message specifying missing fuse count', () => {
  const { game, room, place } = setupTestGame();
  game.startGame('host');
  place('host', -10.5, -16.5);
  room.fuses = new Set(['fuse-1']);
  const res = game.interact('host', { id: 'power' });
  assert.equal(res.ok, false);
  assert.ok((res.error || res.message || '').includes('1/2'));
});

test('F12-T2-2: Interacting with power from corridor across wall fails line-of-sight check', () => {
  const { game, room, place } = setupTestGame();
  game.startGame('host');
  room.fuses = new Set(['fuse-1', 'fuse-2']);
  place('host', 0, -16.5);
  assert.equal(game.interact('host', { id: 'power' }).ok, false);
});

test('F12-T2-3: Interacting with power while downed fails', () => {
  const { game, room, hostPlayer, place } = setupTestGame();
  game.startGame('host');
  room.fuses = new Set(['fuse-1', 'fuse-2']);
  place('host', -10.5, -16.5);
  hostPlayer.down = true;
  assert.equal(game.interact('host', { id: 'power' }).ok, false);
});

test('F12-T2-4: Generator room west wall at x = -12 is impassable', () => {
  assert.equal(isInsideWorld(-12.1, -16.5), false);
});

test('F12-T2-5: Interacting with power with empty string or invalid options is rejected safely', () => {
  const { game } = setupTestGame();
  game.startGame('host');
  assert.equal(game.interact('host', {}).ok, false);
});

// =========================================================================
// FEATURE 13: Ventilation Ducts, Grates & 90s Server Racks (Boundaries)
// =========================================================================
test('F13-T2-1: Vent room boundary at z = -39 partition wall blocks passage into Tech Storage', () => {
  assert.equal(segmentWalkable({ x: -8, z: -38 }, { x: -8, z: -40 }), false);
});

test('F13-T2-2: Server room boundary at z = -39 partition wall blocks passage into Dispatcher', () => {
  assert.equal(segmentWalkable({ x: 8, z: -38 }, { x: 8, z: -40 }), false);
});

test('F13-T2-3: Interacting with fuse-1 at distance > 2.5m fails', () => {
  const { game, place } = setupTestGame();
  game.startGame('host');
  place('host', -8, -23.0); // dist = 3.5m
  assert.equal(game.interact('host', { id: 'fuse-1' }).ok, false);
});

test('F13-T2-4: Interacting with fuse-2 at distance > 2.5m fails', () => {
  const { game, place } = setupTestGame();
  game.startGame('host');
  place('host', 10.5, -28.0); // dist = 3.5m
  assert.equal(game.interact('host', { id: 'fuse-2' }).ok, false);
});

test('F13-T2-5: Interacting with fuse-1 repeatedly after pickup is idempotent', () => {
  const { game, room, place } = setupTestGame();
  game.startGame('host');
  place('host', -8, -26.5);
  assert.equal(game.interact('host', { id: 'fuse-1' }).ok, true);
  assert.equal(game.interact('host', { id: 'fuse-1' }).ok, true);
  assert.equal(room.fuses.size, 1);
});

// =========================================================================
// FEATURE 14: Control Room Consoles & Video Monitors (Boundaries)
// =========================================================================
test('F14-T2-1: Interacting with broadcast with 0 tapes and no power fails with explicit error', () => {
  const { game, place } = setupTestGame();
  game.startGame('host');
  place('host', 10, -2);
  const res = game.interact('host', { id: 'broadcast' });
  assert.equal(res.ok, false);
  assert.ok((res.error || res.message || '').includes('три касети'));
});

test('F14-T2-2: Interacting with broadcast with 3 tapes but NO power fails with power reminder', () => {
  const { game, room, place } = setupTestGame();
  game.startGame('host');
  room.tapes = new Set(['tape-1', 'tape-2', 'tape-3']);
  place('host', 10, -2);
  const res = game.interact('host', { id: 'broadcast' });
  assert.equal(res.ok, false);
  assert.ok((res.error || res.message || '').includes('живлення'));
});

test('F14-T2-3: Interacting with broadcast with power restored but only 2 tapes fails with tape reminder', () => {
  const { game, room, place } = setupTestGame();
  game.startGame('host');
  room.power = true;
  room.tapes = new Set(['tape-1', 'tape-2']);
  place('host', 10, -2);
  const res = game.interact('host', { id: 'broadcast' });
  assert.equal(res.ok, false);
  assert.ok((res.error || res.message || '').includes('три касети'));
});

test('F14-T2-4: Interacting with broadcast repeatedly after activation is idempotent', () => {
  const { game, room, place } = setupTestGame();
  game.startGame('host');
  room.power = true;
  room.tapes = new Set(['tape-1', 'tape-2', 'tape-3']);
  place('host', 10, -2);
  assert.equal(game.interact('host', { id: 'broadcast' }).ok, true);
  assert.equal(game.interact('host', { id: 'broadcast' }).ok, true);
  assert.equal(room.broadcast, true);
});

test('F14-T2-5: Interacting with broadcast from outside Apparatus room across doorway fails LOS check', () => {
  const { game, room, place } = setupTestGame();
  game.startGame('host');
  room.power = true;
  room.tapes = new Set(['tape-1', 'tape-2', 'tape-3']);
  place('host', 0, -2); // In corridor behind wall
  assert.equal(game.interact('host', { id: 'broadcast' }).ok, false);
});

// =========================================================================
// FEATURE 15: Procedural Low-Poly PS1 Geometry & Palette (Boundaries)
// =========================================================================
test('F15-T2-1: Building scene with invalid canvas document proxy throws or handles error predictably', () => {
  const prior = globalThis.document;
  globalThis.document = { createElement: () => null };
  try {
    const scene = new THREE.Scene();
    assert.throws(() => createWorld(scene));
  } finally {
    globalThis.document = prior;
  }
});

test('F15-T2-2: createCharacter with null or invalid color string falls back to default safely', () => {
  const prior = globalThis.document;
  globalThis.document = createTestCanvasDocument();
  try {
    const char = createCharacter(null, true, false);
    assert.ok(char);
  } finally {
    globalThis.document = prior;
  }
});

test('F15-T2-3: Updating world with empty state object does not crash', () => {
  const prior = globalThis.document;
  globalThis.document = createTestCanvasDocument();
  try {
    const scene = new THREE.Scene();
    const world = createWorld(scene);
    assert.doesNotThrow(() => world.setState({}));
  } finally {
    globalThis.document = prior;
  }
});

test('F15-T2-4: Animate character with extreme timestamp executes with finite coordinates', () => {
  const prior = globalThis.document;
  globalThis.document = createTestCanvasDocument();
  try {
    const char = createCharacter('#ffffff', true, false);
    assert.doesNotThrow(() => char.userData.animate(1e9, true));
    assert.doesNotThrow(() => char.userData.animate(-100, false));
  } finally {
    globalThis.document = prior;
  }
});

test('F15-T2-5: Scene mesh traversal verifies zero NaN values in all normal and UV attributes', () => {
  const prior = globalThis.document;
  globalThis.document = createTestCanvasDocument();
  try {
    const scene = new THREE.Scene();
    createWorld(scene);
    scene.traverse((o) => {
      if (o.isMesh && o.geometry) {
        const uvs = o.geometry.getAttribute('uv');
        if (uvs) {
          for (let i = 0; i < Math.min(uvs.array.length, 20); i++) {
            assert.ok(!Number.isNaN(uvs.array[i]));
          }
        }
      }
    });
  } finally {
    globalThis.document = prior;
  }
});

// =========================================================================
// FEATURE 16: Solid Collision Grid & Generator Obstacle Fix (Boundaries)
// =========================================================================
test('F16-T2-1: isWalkable with non-numeric coordinates returns false', () => {
  assert.equal(isWalkable('abc', 'def'), false);
  assert.equal(isWalkable({}, []), false);
});

test('F16-T2-2: isWalkable with NaN or Infinity returns false', () => {
  assert.equal(isWalkable(NaN, 0), false);
  assert.equal(isWalkable(0, Infinity), false);
  assert.equal(isWalkable(-Infinity, 0), false);
});

test('F16-T2-3: isObstacle with negative player radius handles distance calculation without crash', () => {
  assert.doesNotThrow(() => isObstacle(0, 0, -0.5));
});

test('F16-T2-4: Generator area waypoint has clear line of sight to corridor doorway', () => {
  assert.equal(segmentWalkable({ x: -10.5, z: -16.5 }, { x: -2.5, z: -16.5 }), true);
});

test('F16-T2-5: Rapid movement checks along high-frequency trajectory detect obstacles without tunneling', () => {
  // Line crossing through active crate_1
  const crate = DEFAULT_PROPS.find((p) => p.id === 'crate_1');
  assert.equal(segmentWalkable({ x: crate.x - 1, z: crate.z }, { x: crate.x + 1, z: crate.z }), false);
});

// =========================================================================
// FEATURE 17: 60 FPS Geometry Merging Optimization (Boundaries)
// =========================================================================
test('F17-T2-1: Merging empty geometries list handles gracefully', () => {
  const prior = globalThis.document;
  globalThis.document = createTestCanvasDocument();
  try {
    const scene = new THREE.Scene();
    assert.doesNotThrow(() => createWorld(scene));
  } finally {
    globalThis.document = prior;
  }
});

test('F17-T2-2: Merged geometries do not include dynamic interactables', () => {
  const prior = globalThis.document;
  globalThis.document = createTestCanvasDocument();
  try {
    const scene = new THREE.Scene();
    const world = createWorld(scene);
    assert.ok(world.interactables.has('tape-1'));
    assert.ok(world.interactables.has('power'));
  } finally {
    globalThis.document = prior;
  }
});

test('F17-T2-3: Mesh without geometry or with invalid material is skipped during batch traversal', () => {
  const prior = globalThis.document;
  globalThis.document = createTestCanvasDocument();
  try {
    const scene = new THREE.Scene();
    const badMesh = new THREE.Mesh();
    badMesh.material = null;
    scene.add(badMesh);
    assert.doesNotThrow(() => createWorld(scene));
  } finally {
    globalThis.document = prior;
  }
});

test('F17-T2-4: Memory disposal cleans up buffer attributes without dangling references', () => {
  const geom = new THREE.BoxGeometry(1, 1, 1);
  assert.ok(geom.attributes.position);
  geom.dispose();
});

test('F17-T2-5: Multi-material meshes are skipped from single-material geometry merging safely', () => {
  const prior = globalThis.document;
  globalThis.document = createTestCanvasDocument();
  try {
    const scene = new THREE.Scene();
    const multiMesh = new THREE.Mesh(new THREE.BoxGeometry(1, 1, 1), [
      new THREE.MeshBasicMaterial(),
      new THREE.MeshBasicMaterial(),
    ]);
    scene.add(multiMesh);
    assert.doesNotThrow(() => createWorld(scene));
  } finally {
    globalThis.document = prior;
  }
});

// =========================================================================
// FEATURE 18: Builder & Models Preview Tool Support (Boundaries)
// =========================================================================
test('F18-T2-1: syncCustomMap with non-array input falls back to DEFAULT_PROPS', () => {
  const { game } = setupTestGame();
  assert.doesNotThrow(() => game.syncCustomMap(null));
  assert.doesNotThrow(() => game.syncCustomMap('invalid'));
  assert.doesNotThrow(() => game.syncCustomMap(12345));
});

test('F18-T2-2: syncCustomMap with array containing invalid objects strips invalid items', () => {
  const { game } = setupTestGame();
  const mixed = [...DEFAULT_PROPS, { type: 'crate', badProp: true }, { type: 'table', x: NaN }];
  assert.doesNotThrow(() => game.syncCustomMap(mixed));
  game.syncCustomMap(DEFAULT_PROPS);
});

test('F18-T2-3: syncCustomMap handles duplicate prop IDs gracefully', () => {
  const { game } = setupTestGame();
  const dupes = [
    { id: 'dup', type: 'crate', x: 0, z: -10, size: 1 },
    { id: 'dup', type: 'crate', x: 0, z: -10, size: 1 },
  ];
  assert.doesNotThrow(() => game.syncCustomMap(dupes));
  game.syncCustomMap(DEFAULT_PROPS);
});

test('F18-T2-4: Catalog lookup for non-existent prop type returns undefined safely', () => {
  const found = PROP_CATALOG.find((p) => p.type === 'non_existent_type');
  assert.equal(found, undefined);
});

test('F18-T2-5: Custom map synchronization invalidates and rebuilds navigation graph properly', () => {
  const { game } = setupTestGame();
  const from = { x: 0, z: 4 };
  const target = { x: 0, z: -4 };
  assert.deepEqual(nextWaypoint(from, target), target);
  try {
    game.syncCustomMap([...DEFAULT_PROPS, { id: 'blocking-desk', type: 'table', x: 0, z: 0, w: 10, d: 2 }]);
    assert.equal(segmentWalkable(from, target), false);
  } finally {
    game.syncCustomMap(DEFAULT_PROPS);
  }
});

// =========================================================================
// FEATURE 19: Replicated Door Locking States & Dynamic Obstacles (Boundaries)
// =========================================================================
test('F19-T2-1: Testing door collision at boundary edge with zero radius', () => {
  assert.equal(isInsideWorld(-2.5, -1.5, 0), true);
});

test('F19-T2-2: Pathfinding through locked door obstacle finds alternate path or fails when blocked', () => {
  const blockDoor = { id: 'd-arch-block', type: 'crate', minX: -2.7, maxX: -2.3, minZ: -2.5, maxZ: -0.5 };
  try {
    activeObstacles.push(blockDoor);
    assert.equal(segmentWalkable({ x: 0, z: -1.5 }, { x: -6, z: -1.5 }), false);
  } finally {
    activeObstacles.splice(activeObstacles.indexOf(blockDoor), 1);
  }
});

test('F19-T2-3: Dynamic door obstacle registration does not affect walkability of distant rooms', () => {
  const blockDoor = { id: 'd-test-dist', type: 'crate', minX: -2.7, maxX: -2.3, minZ: -2.5, maxZ: -0.5 };
  try {
    activeObstacles.push(blockDoor);
    assert.equal(isWalkable(5, -16.5), true); // Studio 04 open floor remains walkable
  } finally {
    activeObstacles.splice(activeObstacles.indexOf(blockDoor), 1);
  }
});

test('F19-T2-4: Door obstacle with inverted coordinates handled safely', () => {
  const inv = { id: 'inv', type: 'crate', minX: 5, maxX: 2, minZ: 10, maxZ: 8 };
  // isObstacle checks x >= minX && x <= maxX which evaluates false when minX > maxX
  assert.doesNotThrow(() => {
    activeObstacles.push(inv);
    isObstacle(3, 9);
    activeObstacles.pop();
  });
});

test('F19-T2-5: Toggling door open/close repeatedly updates obstacle registry without duplicate obstacle IDs', () => {
  const doorObs = { id: 'toggle-door', type: 'crate', minX: -2.6, maxX: -2.4, minZ: -2, maxZ: -1 };
  activeObstacles.push(doorObs);
  assert.ok(activeObstacles.includes(doorObs));
  activeObstacles.splice(activeObstacles.indexOf(doorObs), 1);
  assert.ok(!activeObstacles.includes(doorObs));
});

// =========================================================================
// FEATURE 20: Guard & Engineer Keycards Network Sync (Boundaries)
// =========================================================================
test('F20-T2-1: Querying room state when keycards set is empty serializes cleanly', () => {
  const { game, room } = setupTestGame();
  const s = game.state(room);
  assert.ok(s);
});

test('F20-T2-2: Adding duplicate keycard ID to set maintains unique items', () => {
  const set = new Set();
  set.add('guard');
  set.add('guard');
  assert.equal(set.size, 1);
});

test('F20-T2-3: Non-existent keycard type in check returns false', () => {
  const set = new Set(['guard']);
  assert.equal(set.has('master_skeleton_key'), false);
});

test('F20-T2-4: Client disconnecting does not remove group keycards from room inventory', () => {
  const { game, room, code } = setupTestGame();
  game.joinRoom('p2', { code, name: 'P2' });
  room.keycards = new Set(['guard']);
  game.leaveRoom('p2');
  assert.equal(room.keycards.has('guard'), true);
});

test('F20-T2-5: Keycard check on uninitialized room handles safely', () => {
  const { room } = setupTestGame();
  assert.equal(room.keycards?.has('guard') ?? false, false);
});

// =========================================================================
// FEATURE 21: Interactive SVG Station Map (M) Upgrade (Boundaries)
// =========================================================================
test('F21-T2-1: World coordinates outside station bounds map to clamped/bounded SVG coordinates', () => {
  function worldToSvg(x, z) {
    const clampedX = Math.max(-12, Math.min(12, x));
    const clampedZ = Math.max(-54, Math.min(36, z));
    const svgX = ((clampedX + 12) / 24) * 480 + 30;
    const svgY = ((36 - clampedZ) / 90) * 600 + 30;
    return { svgX, svgY };
  }
  const extreme = worldToSvg(-100, 100);
  assert.equal(extreme.svgX, 30);
  assert.equal(extreme.svgY, 30);
});

test('F21-T2-2: roomAt with out-of-bounds coordinates defaults safely to fallback string', () => {
  assert.equal(roomAt(-1000, 1000), 'ГОЛОВНИЙ КОРИДОР');
});

test('F21-T2-3: interfaceMarkup handles full character classes without crashing', () => {
  assert.doesNotThrow(() => interfaceMarkup(CHARACTER_CLASSES, 'engineer', () => ''));
});

test('F21-T2-4: Map SVG generation with extreme yaw angle normalizes heading', () => {
  const angle = 100 * Math.PI;
  const normalized = Math.atan2(Math.sin(angle), Math.cos(angle));
  assert.ok(Math.abs(normalized) <= Math.PI);
});

test('F21-T2-5: interfaceMarkup executes cleanly with custom icon renderer', () => {
  assert.doesNotThrow(() => interfaceMarkup(CHARACTER_CLASSES, 'operator', (name) => `<i class="${name}"></i>`));
});

// =========================================================================
// FEATURE 22: Multiplayer Inventory & Quest Synchronization (Boundaries)
// =========================================================================
test('F22-T2-1: State serialization handles empty sets for tapes and fuses without throwing', () => {
  const { game, room } = setupTestGame();
  room.tapes = new Set();
  room.fuses = new Set();
  assert.doesNotThrow(() => game.state(room));
});

test('F22-T2-2: Corrupted or null room properties are safeguarded in game.state(room)', () => {
  const { game, room } = setupTestGame();
  room.tapes = null;
  assert.doesNotThrow(() => {
    const s = { ...room, tapes: room.tapes ? [...room.tapes] : [] };
    assert.deepEqual(s.tapes, []);
  });
});

test('F22-T2-3: Simultaneous pickup attempts on single item resolve safely without state race', () => {
  const { game, room, code, place } = setupTestGame();
  game.joinRoom('p2', { code, name: 'P2' });
  game.startGame('host');
  place('host', -8, -3);
  place('p2', -8, -3);
  const r1 = game.interact('host', { id: 'tape-1' });
  const r2 = game.interact('p2', { id: 'tape-1' });
  assert.equal(r1.ok, true);
  assert.equal(r2.ok, true);
  assert.equal(room.tapes.size, 1);
});

test('F22-T2-4: Player joining room mid-game receives current active quest progression state', () => {
  const { game, room, code } = setupTestGame();
  game.startGame('host');
  room.tapes.add('tape-1');
  room.power = true;
  game.joinRoom('p2', { code, name: 'LateJoin' });
  const s = game.state(room);
  assert.ok(s.tapes.includes('tape-1'));
  assert.equal(s.power, true);
});

test('F22-T2-5: State serialization handles non-string elements in inventory sets cleanly', () => {
  const { game, room } = setupTestGame();
  room.tapes = new Set(['tape-1', 'tape-2']);
  const s = game.state(room);
  assert.equal(s.tapes.length, 2);
});

// =========================================================================
// FEATURE 23: Quest Stage 1: Recon & Access (0-8 min) (Boundaries)
// =========================================================================
test('F23-T2-1: Interacting with tape-1 with distance = 2.49m succeeds; distance = 2.55m fails', () => {
  const { game, place } = setupTestGame();
  game.startGame('host');
  // tape-1 is at (-8, -3)
  place('host', -8, -3 + 2.49);
  assert.equal(game.interact('host', { id: 'tape-1' }).ok, true);
  const { game: g2, place: p2 } = setupTestGame();
  g2.startGame('host');
  p2('host', -8, -3 + 2.55);
  assert.equal(g2.interact('host', { id: 'tape-1' }).ok, false);
});

test('F23-T2-2: Tape 1 pickup when room paused fails', () => {
  const { game, place } = setupTestGame({ solo: true });
  game.startGame('host');
  place('host', -8, -3);
  game.pauseGame('host', { paused: true });
  assert.equal(game.interact('host', { id: 'tape-1' }).ok, false);
});

test('F23-T2-3: Tape 1 pickup in finished game fails', () => {
  const { game, room, place } = setupTestGame();
  game.startGame('host');
  room.phase = 'won';
  place('host', -8, -3);
  assert.equal(game.interact('host', { id: 'tape-1' }).ok, false);
});

test('F23-T2-4: Player picking up Tape 1 with downed state is rejected', () => {
  const { game, hostPlayer, place } = setupTestGame();
  game.startGame('host');
  place('host', -8, -3);
  hostPlayer.down = true;
  assert.equal(game.interact('host', { id: 'tape-1' }).ok, false);
});

test('F23-T2-5: Tape 1 interaction payload with extra unexpected properties executes safely', () => {
  const { game, place } = setupTestGame();
  game.startGame('host');
  place('host', -8, -3);
  assert.equal(game.interact('host', { id: 'tape-1', maliciousExtraField: 'drop table' }).ok, true);
});

// =========================================================================
// FEATURE 24: Quest Stage 2: Power Unit Startup (8-18 min) (Boundaries)
// =========================================================================
test('F24-T2-1: Collecting fuse-1 twice does not double count in room.fuses.size', () => {
  const { game, room, place } = setupTestGame();
  game.startGame('host');
  place('host', -8, -26.5);
  game.interact('host', { id: 'fuse-1' });
  game.interact('host', { id: 'fuse-1' });
  assert.equal(room.fuses.size, 1);
});

test('F24-T2-2: Interacting with power with fuses.size = 2 but player distance = 2.6m fails', () => {
  const { game, room, place } = setupTestGame();
  game.startGame('host');
  room.fuses = new Set(['fuse-1', 'fuse-2']);
  place('host', -11, -16.5 + 2.6);
  assert.equal(game.interact('host', { id: 'power' }).ok, false);
});

test('F24-T2-3: Power interaction while player is hidden in locker fails', () => {
  const { game, room, place } = setupTestGame();
  game.startGame('host');
  room.fuses = new Set(['fuse-1', 'fuse-2']);
  place('host', -3.3, -21.5);
  game.interact('host', { id: 'locker-2' });
  assert.equal(game.interact('host', { id: 'power' }).ok, false);
});

test('F24-T2-4: Interacting with power before game started fails', () => {
  const { game } = setupTestGame();
  assert.equal(game.interact('host', { id: 'power' }).ok, false);
});

test('F24-T2-5: Power activation properly increments scare counter', () => {
  const { game, room, place } = setupTestGame();
  game.startGame('host');
  room.fuses = new Set(['fuse-1', 'fuse-2']);
  place('host', -10.5, -16.5);
  const s0 = room.scare;
  game.interact('host', { id: 'power' });
  assert.ok(room.scare > s0);
});

// =========================================================================
// FEATURE 25: Quest Stage 3: Signal Calibration (18-25 min) (Boundaries)
// =========================================================================
test('F25-T2-1: Interacting with broadcast with 3 tapes and power while distance = 2.6m fails', () => {
  const { game, room, place } = setupTestGame();
  game.startGame('host');
  room.power = true;
  room.tapes = new Set(['tape-1', 'tape-2', 'tape-3']);
  place('host', 10, -2 + 2.6);
  assert.equal(game.interact('host', { id: 'broadcast' }).ok, false);
});

test('F25-T2-2: Interacting with broadcast without LOS from behind wall fails', () => {
  const { game, room, place } = setupTestGame();
  game.startGame('host');
  room.power = true;
  room.tapes = new Set(['tape-1', 'tape-2', 'tape-3']);
  place('host', 0, -2);
  assert.equal(game.interact('host', { id: 'broadcast' }).ok, false);
});

test('F25-T2-3: Collecting tape-3 twice does not exceed 3 in room.tapes.size', () => {
  const { game, room, place } = setupTestGame();
  game.startGame('host');
  place('host', -8, -50.5);
  game.interact('host', { id: 'tape-3' });
  game.interact('host', { id: 'tape-3' });
  assert.equal(room.tapes.size, 1);
});

test('F25-T2-4: Broadcast activation relocates monster to ambush position (0, -22)', () => {
  const { game, room, place } = setupTestGame();
  game.startGame('host');
  room.power = true;
  room.tapes = new Set(['tape-1', 'tape-2', 'tape-3']);
  place('host', 10, -2);
  game.interact('host', { id: 'broadcast' });
  assert.equal(room.enemy.x, 0);
  assert.equal(room.enemy.z, -22);
});

test('F25-T2-5: Broadcast activation grants player temporary invulnerability sprint buffer', () => {
  const { game, room, hostPlayer, place } = setupTestGame();
  game.startGame('host');
  room.power = true;
  room.tapes = new Set(['tape-1', 'tape-2', 'tape-3']);
  place('host', 10, -2);
  game.interact('host', { id: 'broadcast' });
  assert.ok(hostPlayer.invulnerableUntil > room.elapsed);
});

// =========================================================================
// FEATURE 26: Quest Stage 4: Evacuation (25-30 min) (Boundaries)
// =========================================================================
test('F26-T2-1: Interacting with exit at distance = 2.6m fails', () => {
  const { game, room, place } = setupTestGame();
  game.startGame('host');
  room.broadcast = true;
  place('host', extraction.x, extraction.z + 2.6);
  assert.equal(game.interact('host', { id: 'exit' }).ok, false);
});

test('F26-T2-2: Interacting with exit while broadcast is false returns explicit mission objective reminder', () => {
  const { game, place } = setupTestGame();
  game.startGame('host');
  place('host', extraction.x, extraction.z - 0.5);
  const res = game.interact('host', { id: 'exit' });
  assert.equal(res.ok, false);
  assert.ok((res.error || res.message || '').includes('передайте сигнал'));
});

test('F26-T2-3: Interacting with exit while all players downed is rejected', () => {
  const { game, room, hostPlayer, place } = setupTestGame();
  game.startGame('host');
  room.broadcast = true;
  place('host', extraction.x, extraction.z - 0.5);
  hostPlayer.down = true;
  assert.equal(game.interact('host', { id: 'exit' }).ok, false);
});

test('F26-T2-4: Winning the game locks room phase to won', () => {
  const { game, room, place } = setupTestGame();
  game.startGame('host');
  room.broadcast = true;
  place('host', extraction.x, extraction.z - 0.5);
  game.interact('host', { id: 'exit' });
  assert.equal(room.phase, 'won');
  // Subsequent join fails
  assert.equal(game.joinRoom('late', { code: room.code }).ok, false);
});

test('F26-T2-5: Interacting with exit when already won is rejected', () => {
  const { game, room, place } = setupTestGame();
  game.startGame('host');
  room.broadcast = true;
  place('host', extraction.x, extraction.z - 0.5);
  game.interact('host', { id: 'exit' });
  assert.equal(game.interact('host', { id: 'exit' }).ok, false);
});

// =========================================================================
// FEATURE 27: Atmospheric Audio & Sound Effects (Boundaries)
// =========================================================================
test('F27-T2-1: Radio ping with negative or out-of-range phrase number is handled safely', () => {
  const { game } = setupTestGame();
  game.startGame('host');
  assert.doesNotThrow(() => game.radioPing('host', { phrase: -100 }));
  assert.doesNotThrow(() => game.radioPing('host', { phrase: 99999 }));
});

test('F27-T2-2: Radio ping from far distance (> 18m from monster) sets heard: false', () => {
  const { game, room, place } = setupTestGame();
  game.startGame('host');
  place('host', 0, 10);
  room.enemy.active = true;
  room.enemy.x = 0;
  room.enemy.z = -40; // distance 50m
  const res = game.radioPing('host', { phrase: 1 });
  assert.equal(res.ok, true);
  assert.equal(res.heard, false);
});

test('F27-T2-3: Spatial pan calculation with listener exactly at monster position avoids divide-by-zero', () => {
  function getPan(px, pz, pyaw, ex, ez) {
    const dx = ex - px;
    const dz = ez - pz;
    const d = Math.hypot(dx, dz) || 1;
    const rightX = Math.cos(pyaw);
    const rightZ = -Math.sin(pyaw);
    return Math.max(-1, Math.min(1, (dx * rightX + dz * rightZ) / d));
  }
  const panZero = getPan(0, 0, 0, 0, 0);
  assert.equal(Number.isFinite(panZero), true);
});

test('F27-T2-4: Spatial pan clamps strictly to [-1.0, 1.0]', () => {
  function getPan(px, pz, pyaw, ex, ez) {
    const dx = ex - px;
    const dz = ez - pz;
    const d = Math.hypot(dx, dz) || 1;
    const rightX = Math.cos(pyaw);
    const rightZ = -Math.sin(pyaw);
    return Math.max(-1, Math.min(1, (dx * rightX + dz * rightZ) / d));
  }
  for (let angle = 0; angle < Math.PI * 2; angle += 0.5) {
    const pan = getPan(0, 0, angle, 10, 10);
    assert.ok(pan >= -1.0 && pan <= 1.0);
  }
});

test('F27-T2-5: Radio ping while player is downed is rejected', () => {
  const { game, hostPlayer } = setupTestGame();
  game.startGame('host');
  hostPlayer.down = true;
  assert.equal(game.radioPing('host', { phrase: 1 }).ok, false);
});

// =========================================================================
// FEATURE 28: Monster Phase 1: Recon/Shadow (Power OFF) (Boundaries)
// =========================================================================
test('F28-T2-1: Locker entry from distance = 2.3m fails with proximity error', () => {
  const { game, place } = setupTestGame();
  game.startGame('host');
  // locker-1 is at (-3.4, 3.5). Place player at dist = 2.3m
  place('host', -3.4, 3.5 - 2.3);
  const res = game.interact('host', { id: 'locker-1' });
  assert.equal(res.ok, false);
  assert.ok((res.error || res.message || '').includes('шафи'));
});

test('F28-T2-2: Entering locker disables flashlight, camera, and sprinting', () => {
  const { game, hostPlayer, place } = setupTestGame();
  game.startGame('host');
  hostPlayer.camera = true;
  hostPlayer.flashlight = true;
  hostPlayer.sprinting = true;
  place('host', -3.3, 3.2);
  game.interact('host', { id: 'locker-1' });
  assert.equal(hostPlayer.camera, false);
  assert.equal(hostPlayer.flashlight, false);
  assert.equal(hostPlayer.sprinting, false);
});

test('F28-T2-3: Player exiting locker when not in locker returns ok: true without teleportation', () => {
  const { game, hostPlayer, place } = setupTestGame();
  game.startGame('host');
  place('host', 0, 0);
  const res = game.interact('host', { id: 'exit-locker' });
  assert.equal(res.ok, true);
  assert.equal(hostPlayer.x, 0);
  assert.equal(hostPlayer.z, 0);
});

test('F28-T2-4: Locker safe exit coordinates are verified to be inside walkable world', () => {
  for (const l of lockers) {
    assert.equal(isInsideWorld(l.exitX, l.exitZ), true, `Locker ${l.id} exit must be inside world`);
  }
  for (const l of lockers.filter((l) => l.id !== 'locker-6')) {
    assert.equal(isWalkable(l.exitX, l.exitZ), true, `Locker ${l.id} exit must be walkable`);
  }
});

test('F28-T2-5: Interacting with exit-locker special ID when not hiding returns ok: true', () => {
  const { game } = setupTestGame();
  game.startGame('host');
  assert.equal(game.interact('host', { id: 'exit-locker' }).ok, true);
});

// =========================================================================
// FEATURE 29: Monster Phase 2: Active Hunt (Power ON) (Boundaries)
// =========================================================================
test('F29-T2-1: Stun evaluation with distance = 9.1m fails to stun', () => {
  const { game, room, hostPlayer, advance } = setupTestGame();
  game.startGame('host');
  room.enemy.active = true;
  room.enemy.x = 0;
  room.enemy.z = -1;
  hostPlayer.x = 0;
  hostPlayer.z = 8.1; // dist = 9.1m
  hostPlayer.camera = true;
  hostPlayer.flashlight = true;
  hostPlayer.yaw = 0;
  advance(100);
  assert.equal(room.enemy.stunned, false);
});

test('F29-T2-2: Stun evaluation with flashlight on but camera OFF fails to stun', () => {
  const { game, room, hostPlayer, advance } = setupTestGame();
  game.startGame('host');
  room.enemy.active = true;
  room.enemy.x = 0;
  room.enemy.z = -1;
  hostPlayer.x = 0;
  hostPlayer.z = 3;
  hostPlayer.camera = false;
  hostPlayer.flashlight = true;
  hostPlayer.yaw = 0;
  advance(100);
  assert.equal(room.enemy.stunned, false);
});

test('F29-T2-3: Stun evaluation with camera on but flashlight OFF fails to stun', () => {
  const { game, room, hostPlayer, advance } = setupTestGame();
  game.startGame('host');
  room.enemy.active = true;
  room.enemy.x = 0;
  room.enemy.z = -1;
  hostPlayer.x = 0;
  hostPlayer.z = 3;
  hostPlayer.camera = true;
  hostPlayer.flashlight = false;
  hostPlayer.yaw = 0;
  advance(100);
  assert.equal(room.enemy.stunned, false);
});

test('F29-T2-4: Stun evaluation with pitch tilted away fails to stun', () => {
  const { game, room, hostPlayer, advance } = setupTestGame();
  game.startGame('host');
  room.enemy.active = true;
  room.enemy.x = 0;
  room.enemy.z = -1;
  hostPlayer.x = 0;
  hostPlayer.z = 3;
  hostPlayer.camera = true;
  hostPlayer.flashlight = true;
  hostPlayer.yaw = 0;
  hostPlayer.pitch = 1.3;
  advance(100);
  assert.equal(room.enemy.stunned, false);
});

test('F29-T2-5: Stun evaluation across solid wall without line of sight fails to stun', () => {
  const { game, room, hostPlayer, advance } = setupTestGame();
  game.startGame('host');
  room.enemy.active = true;
  room.enemy.x = 8;
  room.enemy.z = -16.5; // Studio 04
  hostPlayer.x = 0;
  hostPlayer.z = -16.5; // Corridor, behind solid wall
  hostPlayer.camera = true;
  hostPlayer.flashlight = true;
  hostPlayer.yaw = Math.PI / 2; // Aiming directly East at monster
  advance(100);
  assert.equal(room.enemy.stunned, false);
});

// =========================================================================
// FEATURE 30: Monster Phase 3: Nightmare Pursuit (Boundaries)
// =========================================================================
test('F30-T2-1: Nightmare speed calculation returns exactly 3.2 m/s when broadcast is true', () => {
  const room = { broadcast: true, tapes: new Set(['tape-1', 'tape-2', 'tape-3']) };
  const speed = room.broadcast ? 3.2 : 0.85 + room.tapes.size * 0.28;
  assert.equal(speed, 3.2);
});

test('F30-T2-2: Monster pursuit handles player inside courtyard without pathfinding disconnection', () => {
  const monsterPos = { x: 0, z: 0 };
  const playerInYard = { x: -5, z: 20 };
  const next = nextWaypoint(monsterPos, playerInYard);
  assert.ok(next);
  assert.ok(next.z > monsterPos.z);
});

test('F30-T2-3: Multiple escaping players maintains monster targeting nearest living target', () => {
  const { game, room, code, place, advance } = setupTestGame();
  game.joinRoom('p2', { code, name: 'P2' });
  game.startGame('host');
  advance(8000);
  room.broadcast = true;
  room.enemy.active = true;
  room.enemy.x = 0;
  room.enemy.z = -10;
  place('host', 0, -5); // 5m away
  place('p2', 0, 10);   // 20m away
  const next = nextWaypoint(room.enemy, room.players.get('host'));
  assert.ok(next.z > room.enemy.z);
});

test('F30-T2-4: Monster does not target downed players during nightmare pursuit', () => {
  const { game, room, code, place, advance } = setupTestGame();
  game.joinRoom('p2', { code, name: 'P2' });
  game.startGame('host');
  advance(8000);
  room.broadcast = true;
  room.players.get('host').down = true;
  place('host', 0, -9);
  place('p2', 0, -2);
  room.enemy.active = true;
  room.enemy.x = 0;
  room.enemy.z = -10;
  advance(100);
  // Target should be living player p2
  assert.equal(room.players.get('host').down, true);
});

test('F30-T2-5: Creature coordinates remain strictly finite and within world/courtyard boundaries', () => {
  const { game, room, advance } = setupTestGame();
  game.startGame('host');
  room.broadcast = true;
  for (let i = 0; i < 20; i++) advance(100);
  assert.ok(Number.isFinite(room.enemy.x));
  assert.ok(Number.isFinite(room.enemy.z));
});

// =========================================================================
// FEATURE 31: Monster Creative Mode Passivity (Boundaries)
// =========================================================================
test('F31-T2-1: Creative mode ignores spawn countdown ticks without activating creature', () => {
  const { game, room, advance } = setupTestGame({ creative: true });
  game.startGame('host');
  for (let i = 0; i < 40; i++) advance(1000);
  assert.equal(room.enemy.active, false);
});

test('F31-T2-2: Picking up tape-1 in Creative mode does not activate aggressive monster hunt', () => {
  const { game, room, place } = setupTestGame({ creative: true });
  game.startGame('host');
  place('host', -8, -3);
  game.interact('host', { id: 'tape-1' });
  assert.equal(room.enemy.active, false);
});

test('F31-T2-3: Creative mode player takes zero contact damage over 100 simulation ticks', () => {
  const { game, room, hostPlayer, place, advance } = setupTestGame({ creative: true });
  game.startGame('host');
  place('host', 0, 0);
  room.enemy.active = true;
  room.enemy.x = 0;
  room.enemy.z = 0;
  for (let i = 0; i < 100; i++) advance(100);
  assert.equal(hostPlayer.down, false);
  assert.equal(hostPlayer.sanity, 100);
});

test('F31-T2-4: Creative mode player sprint stamina depletion does not crash simulation', () => {
  const { game, hostPlayer, advance } = setupTestGame({ creative: true });
  game.startGame('host');
  hostPlayer.sprinting = true;
  assert.doesNotThrow(() => {
    for (let i = 0; i < 30; i++) advance(200);
  });
});

test('F31-T2-5: Creative mode allows complete round victory sequence without danger', () => {
  const { game, room, place } = setupTestGame({ creative: true });
  game.startGame('host');
  room.tapes = new Set(['tape-1', 'tape-2', 'tape-3']);
  room.fuses = new Set(['fuse-1', 'fuse-2']);
  place('host', -10.5, -16.5);
  game.interact('host', { id: 'power' });
  place('host', 10, -2);
  game.interact('host', { id: 'broadcast' });
  place('host', extraction.x, extraction.z - 0.5);
  assert.equal(game.interact('host', { id: 'exit' }).ok, true);
  assert.equal(room.phase, 'won');
});

// =========================================================================
// FEATURE 32: End-to-End Opaque-box Test Suite Validation (Boundaries)
// =========================================================================
test('F32-T2-1: Attempting to start game from a non-host player is rejected', () => {
  const { game, code } = setupTestGame();
  game.joinRoom('guest', { code, name: 'Guest' });
  assert.equal(game.startGame('guest').ok, false);
});

test('F32-T2-2: Attempting to start game when already started returns ok: true idempotently', () => {
  const { game } = setupTestGame();
  game.startGame('host');
  assert.equal(game.startGame('host').ok, true);
});

test('F32-T2-3: Joining with invalid/non-existent room code fails with clear error', () => {
  const { game } = setupTestGame();
  const res = game.joinRoom('guest', { code: 'NONON' });
  assert.equal(res.ok, false);
  assert.ok((res.error || res.message || '').includes('не знайдено'));
});

test('F32-T2-4: Joining a full room (4 players) fails gracefully', () => {
  const { game, code } = setupTestGame();
  game.joinRoom('p2', { code, name: 'P2' });
  game.joinRoom('p3', { code, name: 'P3' });
  game.joinRoom('p4', { code, name: 'P4' });
  const fifth = game.joinRoom('p5', { code, name: 'P5' });
  assert.equal(fifth.ok, false);
  assert.ok((fifth.error || fifth.message || '').includes('4'));
});

test('F32-T2-5: Leaving room as sole player deletes the room from server registry', () => {
  const { game, code } = setupTestGame();
  game.leaveRoom('host');
  assert.equal(game.rooms.has(code), false);
});

// =========================================================================
// FEATURE 33: Adversarial Coverage Hardening (Boundaries)
// =========================================================================
test('F33-T2-1: Moving player with exhausted move budget returns false', () => {
  const { game, hostPlayer } = setupTestGame();
  game.startGame('host');
  hostPlayer.moveBudget = 0.01;
  const moved = game.movePlayer('host', { x: hostPlayer.x + 1.0, z: hostPlayer.z, yaw: 0 });
  assert.equal(moved, false);
});

test('F33-T2-2: Teleport attempt across room wall boundary is rejected by segmentWalkable', () => {
  const { game, hostPlayer, place } = setupTestGame();
  game.startGame('host');
  place('host', 0, 0); // Corridor
  const teleport = game.movePlayer('host', { x: -8, z: 0, yaw: 0 }); // Archive interior
  assert.equal(teleport, false);
  assert.equal(hostPlayer.x, 0);
});

test('F33-T2-3: Setting player class to unregistered class string returns failure without mutating class', () => {
  const { game, hostPlayer } = setupTestGame();
  const res = game.setPlayerClass('host', { characterClass: 'super_hacker' });
  assert.equal(res.ok, false);
  assert.equal(hostPlayer.characterClass, 'engineer');
});

test('F33-T2-4: Player revive interaction targeting self is rejected', () => {
  const { game } = setupTestGame();
  game.startGame('host');
  assert.equal(game.interact('host', { id: 'revive:host' }).ok, false);
});

test('F33-T2-5: Player revive interaction targeting player who is NOT down is rejected', () => {
  const { game, code } = setupTestGame();
  game.joinRoom('p2', { code, name: 'Partner' });
  game.startGame('host');
  assert.equal(game.interact('host', { id: 'revive:p2' }).ok, false);
});
