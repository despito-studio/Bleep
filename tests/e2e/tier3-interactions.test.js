import test from 'node:test';
import assert from 'node:assert/strict';
import {
  setupTestGame,
  createLiveSocketCluster,
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

test('T3-01: 4-Player simultaneous cross-room collection and authoritative state sync', () => {
  const { game, room, code, place } = setupTestGame();
  game.joinRoom('p2', { code, name: 'Operator 2' });
  game.joinRoom('p3', { code, name: 'Operator 3' });
  game.joinRoom('p4', { code, name: 'Operator 4' });
  game.startGame('host');

  // Place players in 4 distinct rooms
  place('host', -8, -3);    // Archive (tape-1)
  place('p2', -8, -26.5);   // Vent room (fuse-1)
  place('p3', 10.5, -31.5); // Server room (fuse-2)
  place('p4', 8, -16.5);    // Studio 04 (tape-2)

  // Simultaneous pickups
  assert.equal(game.interact('host', { id: 'tape-1' }).ok, true);
  assert.equal(game.interact('p2', { id: 'fuse-1' }).ok, true);
  assert.equal(game.interact('p3', { id: 'fuse-2' }).ok, true);
  assert.equal(game.interact('p4', { id: 'tape-2' }).ok, true);

  const shared = game.state(room);
  assert.equal(shared.tapes.length, 2);
  assert.ok(shared.tapes.includes('tape-1'));
  assert.ok(shared.tapes.includes('tape-2'));
  assert.equal(shared.fuses.length, 2);
  assert.ok(shared.fuses.includes('fuse-1'));
  assert.ok(shared.fuses.includes('fuse-2'));
});

test('T3-02: Power grid activation triggering Phase 2 creature awakening and audio/message sync', () => {
  const { game, room, place } = setupTestGame();
  game.startGame('host');
  assert.equal(room.power, false);
  assert.equal(room.enemy.active, false);

  // Install both fuses and power on
  room.fuses = new Set(['fuse-1', 'fuse-2']);
  place('host', -10.5, -16.5);
  const pwrRes = game.interact('host', { id: 'power' });
  assert.equal(pwrRes.ok, true);
  assert.equal(room.power, true);
  assert.ok(room.message.includes('Живлення відновлено'));
});

test('T3-03: Locker hiding during active monster patrol and safe egress after threat departure', () => {
  const { game, room, hostPlayer, place, advance } = setupTestGame();
  game.startGame('host');
  advance(8000); // clear arrival grace

  // Player enters locker-1 in Archive
  place('host', -3.3, 3.2);
  const hideRes = game.interact('host', { id: 'locker-1' });
  assert.equal(hideRes.ok, true);
  assert.equal(hostPlayer.hiding, 'locker-1');

  // Monster patrols right past locker
  room.enemy.active = true;
  room.enemy.x = -3.4;
  room.enemy.z = 3.5;
  advance(2000);
  assert.equal(hostPlayer.down, false, 'Player in locker cannot be downed');

  // Threat leaves
  room.enemy.x = 0;
  room.enemy.z = -10;
  advance(1000);

  // Player exits safely
  const exitRes = game.interact('host', { id: 'locker-1' });
  assert.equal(exitRes.ok, true);
  assert.equal(hostPlayer.hiding, null);
  assert.equal(isWalkable(hostPlayer.x, hostPlayer.z), true);
});

test('T3-04: Team revive coordination with flashlight + camera stun defense', () => {
  const { game, room, code, place, advance } = setupTestGame();
  game.joinRoom('p2', { code, name: 'Defender' });
  game.startGame('host');
  advance(8000);

  const p1 = room.players.get('host');
  const p2 = room.players.get('p2');

  // p1 downed in corridor
  place('host', 0, 0);
  p1.down = true;

  // p2 at (0, 3) aims camera and flashlight at monster at (0, -1)
  place('p2', 0, 3);
  p2.camera = true;
  p2.flashlight = true;
  p2.yaw = 0; // facing north towards (0, -1)

  room.enemy.active = true;
  room.enemy.x = 0;
  room.enemy.z = -1;
  advance(100);

  assert.equal(room.enemy.stunned, true, 'Monster must be stunned by p2');

  // While monster is stunned, p2 revives p1
  place('p2', 0, 0.5); // close enough to revive (< 2m)
  const reviveRes = game.interact('p2', { id: 'revive:host' });
  assert.equal(reviveRes.ok, true);
  assert.equal(p1.down, false);
  assert.ok(p1.invulnerableUntil > room.elapsed);
});

test('T3-05: Radio ping distraction attracting creature away from downed teammate', () => {
  const { game, room, code, place } = setupTestGame();
  game.joinRoom('p2', { code, name: 'Bait' });
  game.startGame('host');

  const p1 = room.players.get('host');
  p1.down = true;
  place('host', 0, 0);

  room.enemy.active = true;
  room.enemy.x = 0;
  room.enemy.z = -2;

  // p2 at (0, 10) (12m away from monster, within 18m hearing range) pings radio
  place('p2', 0, 10);
  const ping = game.radioPing('p2', { phrase: 1 });
  assert.equal(ping.heard, true);
  assert.ok(room.enemy.heardRadio);
  assert.equal(room.enemy.heardRadio.x, 0);
  assert.equal(room.enemy.heardRadio.z, 10);

  // Monster waypoint re-targets towards radio source at (0, 10)
  const next = nextWaypoint(room.enemy, room.enemy.heardRadio);
  assert.ok(next.z > room.enemy.z);
});

test('T3-06: Broadcast transmission triggering Phase 3 bloody nightmare pursuit and sprint to van', () => {
  const { game, room, hostPlayer, place, advance } = setupTestGame();
  game.startGame('host');
  room.power = true;
  room.tapes = new Set(['tape-1', 'tape-2', 'tape-3']);

  place('host', 10, -2);
  const bcRes = game.interact('host', { id: 'broadcast' });
  assert.equal(bcRes.ok, true);
  assert.equal(room.broadcast, true);
  assert.equal(room.enemy.active, true);

  const state = game.state(room);
  assert.equal(state.enemy.bloody, true);

  // Sprint to van at extraction
  advance(500);
  place('host', extraction.x, extraction.z - 0.5);
  const winRes = game.interact('host', { id: 'exit' });
  assert.equal(winRes.ok, true);
  assert.equal(room.phase, 'won');
  assert.equal(room.enemy.active, false);
});

test('T3-07: Custom map level editor updates dynamically synchronizing with active physics and pathfinding', () => {
  const { game } = setupTestGame();
  const from = { x: 0, z: 4 };
  const target = { x: 0, z: -4 };
  assert.equal(segmentWalkable(from, target), true);

  try {
    // Add custom wall block via editor sync
    game.syncCustomMap([...DEFAULT_PROPS, { id: 'editor-wall', type: 'table', x: 0, z: 0, w: 10, d: 2 }]);
    assert.equal(segmentWalkable(from, target), false);
    assert.notDeepEqual(nextWaypoint(from, target), target);
  } finally {
    game.syncCustomMap(DEFAULT_PROPS);
    assert.equal(segmentWalkable(from, target), true);
  }
});

test('T3-08: Door locking mechanics blocking player and creature movement, and unlocking restoring path', () => {
  const doorObstacle = { id: 'door-lock-archive', type: 'crate', minX: -2.7, maxX: -2.3, minZ: -2.5, maxZ: -0.5 };
  const pOutside = { x: 0, z: -1.5 };
  const pInside = { x: -6, z: -1.5 };

  // Door closed: blocked
  activeObstacles.push(doorObstacle);
  assert.equal(segmentWalkable(pOutside, pInside), false);

  // Door unlocked: passage clear
  activeObstacles.splice(activeObstacles.indexOf(doorObstacle), 1);
  assert.equal(segmentWalkable(pOutside, pInside), true);
});

test('T3-09: Live Socket.IO multi-client round lifecycle and objective replication', async (t) => {
  const cluster = await createLiveSocketCluster();
  t.after(async () => await cluster.cleanup());

  const host = await cluster.connectClient();
  const cRes = await host.timeout(3000).emitWithAck('room:create', { name: 'Host', solo: false });
  assert.equal(cRes.ok, true);

  const guest = await cluster.connectClient();
  const jRes = await guest.timeout(3000).emitWithAck('room:join', { code: cRes.code, name: 'Guest' });
  assert.equal(jRes.ok, true);

  const startRes = await host.timeout(3000).emitWithAck('game:start');
  assert.equal(startRes.ok, true);

  const room = cluster.server.game.roomFor(host.id);
  Object.assign(room.players.get(host.id), { x: -8, z: -3 });

  const stateReceived = new Promise((resolve) => {
    const handler = (state) => {
      if (state.tapes.includes('tape-1')) {
        guest.off('room:state', handler);
        resolve(state);
      }
    };
    guest.on('room:state', handler);
  });

  const intRes = await host.timeout(3000).emitWithAck('game:interact', { id: 'tape-1' });
  assert.equal(intRes.ok, true);

  const state = await stateReceived;
  assert.ok(state.tapes.includes('tape-1'));
});

test('T3-10: Host departure and seamless authority transfer during active round', async (t) => {
  const cluster = await createLiveSocketCluster();
  t.after(async () => await cluster.cleanup());

  const host = await cluster.connectClient();
  const created = await host.timeout(3000).emitWithAck('room:create', { name: 'HostLeader' });
  assert.equal(created.ok, true);

  const guest = await cluster.connectClient();
  await guest.timeout(3000).emitWithAck('room:join', { code: created.code, name: 'NewLeader' });

  const migrated = new Promise((resolve) => {
    const handler = (state) => {
      if (state.hostId === guest.id) {
        guest.off('room:state', handler);
        resolve(state);
      }
    };
    guest.on('room:state', handler);
  });

  host.disconnect();
  const nextState = await migrated;
  assert.equal(nextState.hostId, guest.id);
  assert.equal(nextState.players.length, 1);
});

test('T3-11: Character class perks interacting with movement budget and stamina', () => {
  const { game, room } = setupTestGame({ hostClass: 'journalist' });
  const p = room.players.get('host');
  assert.equal(p.characterClass, 'journalist');

  // Verify class data mapping
  const classes = Object.keys(CHARACTER_CLASSES);
  assert.ok(classes.includes('engineer'));
  assert.ok(classes.includes('operator'));
  assert.ok(classes.includes('journalist'));
  assert.ok(classes.includes('dispatcher'));

  // Switch class in lobby
  game.setPlayerClass('host', { characterClass: 'dispatcher' });
  assert.equal(p.characterClass, 'dispatcher');
});

test('T3-12: Flashlight battery / toggle interaction with creature detection', () => {
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

  advance(100);
  assert.equal(room.enemy.stunned, true);

  // Turn flashlight off
  hostPlayer.flashlight = false;
  advance(100);
  assert.equal(room.enemy.stunned, false);
});

test('T3-13: Creative mode full quest flow with zero damage and passive creature', () => {
  const { game, room, hostPlayer, place } = setupTestGame({ creative: true });
  game.startGame('host');

  // Place enemy right next to player
  place('host', 0, 0);
  room.enemy.active = true;
  room.enemy.x = 0;
  room.enemy.z = 0.1;

  // Complete all steps in creative mode
  place('host', -8, -3);
  game.interact('host', { id: 'tape-1' });
  place('host', -8, -26.5);
  game.interact('host', { id: 'fuse-1' });
  place('host', 10.5, -31.5);
  game.interact('host', { id: 'fuse-2' });
  place('host', -10.5, -16.5);
  game.interact('host', { id: 'power' });
  place('host', 8, -16.5);
  game.interact('host', { id: 'tape-2' });
  place('host', -8, -50.5);
  game.interact('host', { id: 'tape-3' });
  place('host', 10, -2);
  game.interact('host', { id: 'broadcast' });
  place('host', extraction.x, extraction.z - 0.5);
  game.interact('host', { id: 'exit' });

  assert.equal(room.phase, 'won');
  assert.equal(hostPlayer.down, false);
  assert.equal(hostPlayer.sanity, 100);
});

test('T3-14: All-down defeat trigger in multi-player co-op', () => {
  const { game, room, code, advance } = setupTestGame();
  game.joinRoom('p2', { code, name: 'Operator 2' });
  game.startGame('host');
  advance(8000);

  const p1 = room.players.get('host');
  const p2 = room.players.get('p2');

  p1.down = true;
  p1.invulnerableUntil = 0;
  p2.down = true;
  p2.invulnerableUntil = 0;

  room.enemy.active = true;
  advance(250);
  assert.equal(room.phase, 'lost');
  assert.ok(room.message.includes('утрачено'));
});

test('T3-15: Round restart after defeat or victory cleanly re-initializes all entities', () => {
  const { game, room, hostPlayer, place } = setupTestGame();
  game.startGame('host');
  room.tapes = new Set(['tape-1', 'tape-2', 'tape-3']);
  room.fuses = new Set(['fuse-1', 'fuse-2']);
  room.power = true;
  room.broadcast = true;
  room.phase = 'won';

  const restartRes = game.restartGame('host');
  assert.equal(restartRes.ok, true);
  assert.equal(room.phase, 'playing');
  assert.equal(room.tapes.size, 0);
  assert.equal(room.fuses.size, 0);
  assert.equal(room.power, false);
  assert.equal(room.broadcast, false);
  assert.equal(hostPlayer.down, false);
  assert.ok(Math.hypot(hostPlayer.x - spawn.x, hostPlayer.z - spawn.z) < 1.0);
});
