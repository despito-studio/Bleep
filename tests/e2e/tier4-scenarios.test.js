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

// =========================================================================
// SCENARIO 1: Full 4-Player Co-op Broadcast Campaign (0-30 min Flow)
// =========================================================================
test('Scenario 1: Complete 4-Player Co-op Campaign Playthrough from Arrival to Van Extraction', () => {
  const { game, room, code, place, walkTo, advance } = setupTestGame({ hostClass: 'engineer' });
  game.joinRoom('p2', { code, name: 'Operator Vivian', characterClass: 'operator' });
  game.joinRoom('p3', { code, name: 'Journalist Edgar', characterClass: 'journalist' });
  game.joinRoom('p4', { code, name: 'Dispatcher Hazel', characterClass: 'dispatcher' });

  assert.equal(room.players.size, 4);
  const startRes = game.startGame('host');
  assert.equal(startRes.ok, true);
  assert.equal(room.phase, 'playing');

  // STAGE 1: Arrival & Recon (0-8 min)
  // All 4 players spawn at courtyard van
  for (const p of room.players.values()) {
    assert.equal(roomAt(p.x, p.z), 'ДВІР СТАНЦІЇ');
  }

  // Operators traverse into station corridor
  place('host', 0, 4);
  place('p2', 0, 4);
  place('p3', 0, 4);
  place('p4', 0, 4);
  assert.equal(roomAt(0, 4), 'ГОЛОВНИЙ КОРИДОР');

  // Host (Engineer) enters Archive and retrieves Tape 1
  place('host', -8, -3);
  const tape1Res = game.interact('host', { id: 'tape-1' });
  assert.equal(tape1Res.ok, true);
  assert.ok(room.tapes.has('tape-1'));
  assert.equal(room.enemy.active, true, 'Monster must activate after first tape');

  // STAGE 2: Power Unit Startup (8-18 min)
  // p2 secures Fuse 1 from Vent Room
  place('p2', -8, -26.5);
  assert.equal(game.interact('p2', { id: 'fuse-1' }).ok, true);

  // p3 secures Fuse 2 from Server Room
  place('p3', 10.5, -31.5);
  assert.equal(game.interact('p3', { id: 'fuse-2' }).ok, true);

  assert.equal(room.fuses.size, 2);

  // Host arrives at Generator Room and activates knife switch
  place('host', -10.5, -16.5);
  const powerRes = game.interact('host', { id: 'power' });
  assert.equal(powerRes.ok, true);
  assert.equal(room.power, true);
  assert.ok(room.message.includes('Живлення відновлено'));

  // STAGE 3: Signal Calibration & Bureau Call (18-25 min)
  // p4 retrieves Tape 2 from Studio 04
  place('p4', 8, -16.5);
  assert.equal(game.interact('p4', { id: 'tape-2' }).ok, true);

  // p2 retrieves Tape 3 from Tech Storage
  place('p2', -8, -50.5);
  assert.equal(game.interact('p2', { id: 'tape-3' }).ok, true);

  assert.equal(room.tapes.size, 3);

  // Team gathers at Apparatus Room broadcast console
  place('host', 10, -2);
  const bcRes = game.interact('host', { id: 'broadcast' });
  assert.equal(bcRes.ok, true);
  assert.equal(room.broadcast, true);
  assert.equal(game.state(room).enemy.bloody, true, 'Nightmare form active');

  // STAGE 4: Evacuation Sprint (25-30 min)
  // Monster pursues at 3.2 m/s from ambush point (0, -22)
  assert.equal(room.enemy.x, 0);
  assert.equal(room.enemy.z, -22);

  advance(1000);
  // Operators sprint down 60m corridor to courtyard van
  place('host', extraction.x, extraction.z - 0.4);
  place('p2', extraction.x, extraction.z - 0.4);
  place('p3', extraction.x, extraction.z - 0.4);
  place('p4', extraction.x, extraction.z - 0.4);

  // Host enters van
  const exitRes = game.interact('host', { id: 'exit' });
  assert.equal(exitRes.ok, true);
  assert.equal(room.phase, 'won');
  assert.equal(room.enemy.active, false);
  assert.ok(room.message.includes('Ви вийшли'));
});

// =========================================================================
// SCENARIO 2: Solo Hardcore Survival & Locker Evasion
// =========================================================================
test('Scenario 2: Solo Operator Hardcore Survival, Locker Hiding & Evasion', () => {
  const { game, room, hostPlayer, place, advance } = setupTestGame({ solo: true });
  game.startGame('host');
  assert.equal(room.solo, true);

  // Operator advances to Archive and retrieves Tape 1
  place('host', -8, -3);
  game.interact('host', { id: 'tape-1' });

  // Threat approaches corridor outside Archive. Operator enters corridor locker-4
  advance(8000); // clear arrival grace
  place('host', -1.5, -12.0);
  const hideRes = game.interact('host', { id: 'locker-4' });
  assert.equal(hideRes.ok, true);
  assert.equal(hostPlayer.hiding, 'locker-4');

  // Monster patrols along corridor axis past locker
  room.enemy.x = 0;
  room.enemy.z = -12.0;
  advance(2000);
  assert.equal(hostPlayer.down, false, 'Operator safely hidden from creature in locker');

  // Monster patrols northward away to z = -35
  room.enemy.z = -35.0;
  advance(1000);

  // Operator exits locker safely
  const exitRes = game.interact('host', { id: 'locker-4' });
  assert.equal(exitRes.ok, true);
  assert.equal(hostPlayer.hiding, null);

  // Collect fuses, power on, and tapes
  room.fuses = new Set(['fuse-1', 'fuse-2']);
  place('host', -10.5, -16.5);
  game.interact('host', { id: 'power' });

  room.tapes = new Set(['tape-1', 'tape-2', 'tape-3']);
  place('host', 10, -2);
  game.interact('host', { id: 'broadcast' });

  // Escape to van
  place('host', extraction.x, extraction.z - 0.5);
  const winRes = game.interact('host', { id: 'exit' });
  assert.equal(winRes.ok, true);
  assert.equal(room.phase, 'won');
});

// =========================================================================
// SCENARIO 3: Creative Mode Complete Safe Station Survey
// =========================================================================
test('Scenario 3: Creative Mode Complete Station Survey and Threat-Free Puzzle Completion', () => {
  const { game, room, hostPlayer, place, advance } = setupTestGame({ creative: true });
  game.startGame('host');
  assert.equal(room.creative, true);

  // Systematically visit all 9 station sectors and outdoor yard
  const sectors = [
    { name: 'ДВІР СТАНЦІЇ', x: 0, z: 20 },
    { name: 'ГОЛОВНИЙ КОРИДОР', x: 0, z: 0 },
    { name: 'АРХІВ', x: -6, z: -3 },
    { name: 'АПАРАТНА', x: 8, z: -3 },
    { name: 'ГЕНЕРАТОРНА', x: -8, z: -17 },
    { name: 'СТУДІЯ 04', x: 6, z: -17 },
    { name: 'ВЕНТВУЗОЛ', x: -8, z: -32 },
    { name: 'СЕРВЕРНА', x: 8, z: -32 },
    { name: 'ТЕХСКЛАД', x: -8, z: -47 },
    { name: 'ДИСПЕТЧЕРСЬКА', x: 6, z: -47 },
  ];

  for (const s of sectors) {
    place('host', s.x, s.z);
    assert.equal(roomAt(s.x, s.z), s.name, `Sector ${s.name} at (${s.x},${s.z}) must match roomAt`);
    assert.equal(isWalkable(s.x, s.z), true);
  }

  // Monster present right beside player does zero damage
  room.enemy.active = true;
  room.enemy.x = hostPlayer.x;
  room.enemy.z = hostPlayer.z + 0.1;
  advance(5000);
  assert.equal(hostPlayer.down, false);
  assert.equal(hostPlayer.sanity, 100);

  // Solve all puzzle steps cleanly
  room.tapes = new Set(['tape-1', 'tape-2', 'tape-3']);
  room.fuses = new Set(['fuse-1', 'fuse-2']);
  place('host', -10.5, -16.5);
  game.interact('host', { id: 'power' });
  place('host', 10, -2);
  game.interact('host', { id: 'broadcast' });
  place('host', extraction.x, extraction.z - 0.5);
  game.interact('host', { id: 'exit' });

  assert.equal(room.phase, 'won');
  assert.equal(hostPlayer.down, false);
});

// =========================================================================
// SCENARIO 4: Disaster Recovery & Revive Under Pressure
// =========================================================================
test('Scenario 4: Teammate Downed by Creature, Camera Stun Defense, and Emergency Revive', () => {
  const { game, room, code, place, advance } = setupTestGame();
  game.joinRoom('p2', { code, name: 'Medic Operator' });
  game.startGame('host');
  advance(8000); // clear arrival grace

  const p1 = room.players.get('host');
  const p2 = room.players.get('p2');

  // Player 1 corners creature in generator corridor and is downed
  place('host', 0, -17);
  room.enemy.active = true;
  room.enemy.x = 0;
  room.enemy.z = -16.5;

  for (let i = 0; i < 9; i++) advance(250);
  assert.equal(p1.down, true, 'Player 1 must be downed after contact');
  assert.equal(room.phase, 'playing', 'Game must still be active while Player 2 survives');

  // Player 2 arrives with camera + flashlight aiming at creature
  place('p2', 0, -13); // 3.5m away from creature, line of sight along corridor
  p2.camera = true;
  p2.flashlight = true;
  p2.yaw = 0; // facing north towards z = -16.5
  p2.pitch = 0;
  advance(100);

  assert.equal(room.enemy.stunned, true, 'Creature stunned by Player 2 beam');

  // Player 2 rushes to downed teammate and revives
  place('p2', 0, -16.5);
  const revRes = game.interact('p2', { id: 'revive:host' });
  assert.equal(revRes.ok, true);
  assert.equal(p1.down, false);
  assert.ok(p1.invulnerableUntil > room.elapsed, 'Revived player receives invulnerability buffer');

  // Both players retreat safely
  place('host', 0, 0);
  place('p2', 0, 0);
  advance(1000);
  assert.equal(p1.down, false);
  assert.equal(p2.down, false);
});

// =========================================================================
// SCENARIO 5: Dynamic Host Migration and Network Session Continuity
// =========================================================================
test('Scenario 5: Multi-Client Co-op with Host Drop, Authority Reassignment, and Extraction', async (t) => {
  const cluster = await createLiveSocketCluster();
  t.after(async () => await cluster.cleanup());

  const host = await cluster.connectClient();
  const cRes = await host.timeout(3000).emitWithAck('room:create', { name: 'Captain' });
  assert.equal(cRes.ok, true);

  const guest1 = await cluster.connectClient();
  const guest2 = await cluster.connectClient();
  await guest1.timeout(3000).emitWithAck('room:join', { code: cRes.code, name: 'FirstMate' });
  await guest2.timeout(3000).emitWithAck('room:join', { code: cRes.code, name: 'Navigator' });

  await host.timeout(3000).emitWithAck('game:start');

  const room = cluster.server.game.roomFor(host.id);
  assert.equal(room.players.size, 3);

  // Collect initial items
  room.tapes.add('tape-1');
  room.fuses.add('fuse-1');

  // Listen for host transfer event
  const hostTransferred = new Promise((resolve) => {
    const handler = (state) => {
      if (state.hostId === guest1.id) {
        guest1.off('room:state', handler);
        resolve(state);
      }
    };
    guest1.on('room:state', handler);
  });

  // Host disconnects
  host.disconnect();

  const migratedState = await hostTransferred;
  assert.equal(migratedState.hostId, guest1.id);
  assert.equal(migratedState.players.length, 2);
  assert.ok(migratedState.tapes.includes('tape-1'), 'Quest progression preserved after host migration');
  assert.ok(migratedState.fuses.includes('fuse-1'), 'Inventory preserved after host migration');

  // New host completes quest
  room.fuses.add('fuse-2');
  room.tapes.add('tape-2');
  room.tapes.add('tape-3');
  room.power = true;
  room.broadcast = true;

  Object.assign(room.players.get(guest1.id), { x: extraction.x, z: extraction.z - 0.5 });
  const exitRes = await guest1.timeout(3000).emitWithAck('game:interact', { id: 'exit' });
  assert.equal(exitRes.ok, true);

  assert.equal(room.phase, 'won');
});
