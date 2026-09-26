import test from 'node:test';
import assert from 'node:assert/strict';
import { io as connect } from 'socket.io-client';
import { GameManager, nextWaypoint, segmentWalkable } from './game.js';
import { createGameServer } from './index.js';
import { isWalkable, objects, spawn, updateObstacles } from '../shared/world-data.js';
import { DEFAULT_PROPS } from '../shared/props.js';

function setup({ solo = false } = {}) {
  let now = 1000;
  const game = new GameManager({ now: () => now });
  const result = game.createRoom('host', { name: 'Дослідник', solo });
  const room = game.roomFor('host');
  return { game, room, code: result.code, player: room.players.get('host'), advance: (ms) => { now += ms; game.tick(); } };
}

function place(room, id, x, z) {
  Object.assign(room.players.get(id), { x, z });
}

test('co-op rooms cap at four, sanitize names, and preserve current room after an invalid join', () => {
  const { game, room, code } = setup();
  for (let i = 1; i < 4; i++) assert.equal(game.joinRoom(`p${i}`, { code, name: '<\u0000Very long player name exceeding twenty characters>' }).ok, true);
  assert.equal(room.players.size, 4);
  assert.equal(game.joinRoom('fifth', { code }).ok, false);
  assert.equal(room.players.get('p1').name.length, 20);
  assert.equal(room.players.get('p1').name.includes('<'), false);
  assert.equal(game.joinRoom('host', { code: 'WRONG' }).ok, false);
  assert.equal(game.roomFor('host'), room);
  assert.equal(game.startGame('p1').ok, false);
  assert.equal(game.startGame('host').ok, true);
});

test('disconnect transfers host and removes empty rooms', () => {
  const { game, room, code } = setup();
  game.joinRoom('second', { code });
  game.leaveRoom('host');
  assert.equal(room.hostId, 'second');
  assert.equal(game.playerRooms.has('host'), false);
  assert.equal(game.startGame('second').ok, true);
  game.leaveRoom('second');
  assert.equal(game.rooms.size, 0);
});

test('solo room refuses guests; solo pause freezes elapsed time and simulation', () => {
  const { game, room, code, player, advance } = setup({ solo: true });
  assert.equal(game.joinRoom('guest', { code }).ok, false);
  game.startGame('host');
  advance(1000);
  assert.equal(room.elapsed, 1);
  game.pauseGame('host', { paused: true });
  advance(20000);
  assert.equal(room.elapsed, 1);
  assert.equal(game.movePlayer('host', { x: player.x, z: player.z - 0.1, yaw: 0 }), false);
  game.pauseGame('host', { paused: false });
  advance(1000);
  assert.equal(room.elapsed, 2);
});

test('authoritative movement rejects teleports, nonfinite values, and crossing walls', () => {
  const { game, room, player, advance } = setup();
  game.startGame('host');
  assert.equal(game.movePlayer('host', { x: -8, z: -3, yaw: 0 }), false);
  assert.equal(game.movePlayer('host', { x: NaN, z: 4, yaw: 0 }), false);
  const nextZ = player.z - 0.5;
  assert.equal(game.movePlayer('host', { x: player.x, z: nextZ, yaw: 0 }), true);
  assert.equal(player.z, nextZ);
  place(room, 'host', -2, 0);
  advance(300);
  assert.equal(game.movePlayer('host', { x: -3, z: 0, yaw: 0 }), false);
  assert.equal(player.x, -2);
  place(room, 'host', -2, -2);
  advance(300);
  assert.equal(game.movePlayer('host', { x: -3, z: -2, yaw: 0 }), true);
});

test('objective gates require proximity, all tapes, restored power, and a broadcast', () => {
  const { game, room } = setup();
  game.startGame('host');
  assert.equal(game.interact('host', { id: 'tape-1' }).ok, false);
  assert.equal(game.interact('host', { id: 'exit' }).ok, false);
  assert.equal(game.interact('host', { id: '__proto__' }).ok, false);
  place(room, 'host', 10, -2);
  assert.equal(game.interact('host', { id: 'broadcast' }).ok, false);
  for (const [id, x, z] of [['tape-1', -8, -3], ['tape-2', 8, -16.5], ['tape-3', -8, -50.5]]) {
    place(room, 'host', x, z);
    assert.equal(game.interact('host', { id }).ok, true);
    assert.equal(game.interact('host', { id }).ok, true);
  }
  assert.equal(room.tapes.size, 3);
  assert.equal(room.enemy.active, true);
  place(room, 'host', 10, -2);
  assert.equal(game.interact('host', { id: 'broadcast' }).ok, false);
  place(room, 'host', -10.5, -16.5);
  // Power requires 2 fuses from Vent room and Server room
  assert.equal(game.interact('host', { id: 'power' }).ok, false);
  place(room, 'host', -8, -26.5);
  assert.equal(game.interact('host', { id: 'fuse-1' }).ok, true);
  place(room, 'host', 10.5, -31.5);
  assert.equal(game.interact('host', { id: 'fuse-2' }).ok, true);
  assert.equal(room.fuses.size, 2);
  place(room, 'host', -10.5, -16.5);
  assert.equal(game.interact('host', { id: 'power' }).ok, true);
  place(room, 'host', 10, -2);
  assert.equal(game.interact('host', { id: 'broadcast' }).ok, true);
  const extraction = objects.find((object) => object.id === 'exit');
  place(room, 'host', extraction.x, extraction.z - 0.5);
  assert.equal(game.interact('host', { id: 'exit' }).ok, true);
  assert.equal(room.phase, 'won');
  assert.equal(room.enemy.active, false);
  assert.equal(game.restartGame('host').ok, true);
  assert.equal(room.phase, 'playing');
  assert.equal(room.tapes.size, 0);
  assert.equal(room.fuses.size, 0);
  assert.equal(room.power, false);
  assert.equal(room.broadcast, false);
});

test('the creature navigates around room walls via open doorways', () => {
  let location = { x: -8, z: -19 };
  const target = { x: 8, z: -3 };
  assert.equal(segmentWalkable(location, target), false);
  for (let step = 0; step < 1000 && Math.hypot(location.x - target.x, location.z - target.z) > 0.2; step++) {
    const waypoint = nextWaypoint(location, target);
    const d = Math.hypot(waypoint.x - location.x, waypoint.z - location.z);
    assert.ok(d > 0, 'a path through the doorways must exist');
    const move = Math.min(d, 0.15);
    const next = { x: location.x + (waypoint.x - location.x) / d * move, z: location.z + (waypoint.z - location.z) / d * move };
    assert.ok(segmentWalkable(location, next));
    assert.ok(isWalkable(next.x, next.z));
    location = next;
  }
  assert.ok(Math.hypot(location.x - target.x, location.z - target.z) <= 0.2);
});

test('camera plus flashlight stuns only while aiming at a visible creature', () => {
  const { game, room, player, advance } = setup();
  game.startGame('host');
  Object.assign(room.enemy, { x: 0, z: -1, active: true });
  Object.assign(player, { x: 0, z: 3, yaw: 0, camera: true, flashlight: true });
  advance(100);
  assert.equal(room.enemy.stunned, true);
  assert.equal(room.enemy.z, -1);
  player.yaw = Math.PI;
  advance(100);
  assert.equal(room.enemy.stunned, false);
  assert.ok(room.enemy.z > -1);
  player.yaw = 0;
  player.pitch = 1.3;
  advance(100);
  assert.equal(room.enemy.stunned, false, 'looking up does not count as framing the creature');
  player.pitch = 0;
  player.flashlight = false;
  advance(100);
  assert.equal(room.enemy.stunned, false);
});

test('contact grace, nearby revives, and all-down defeat work in co-op', () => {
  const { game, room, code, player, advance } = setup();
  game.joinRoom('partner', { code });
  game.startGame('host');
  advance(8000);
  Object.assign(player, { x: 0, z: 1 });
  place(room, 'partner', 0, 4);
  Object.assign(room.enemy, { x: 0, z: 0.5, active: true });
  for (let i = 0; i < 7; i++) advance(250);
  assert.equal(player.down, false);
  advance(250);
  assert.equal(player.down, true);
  assert.equal(room.phase, 'playing');
  assert.equal(game.interact('partner', { id: 'revive:host' }).ok, false);
  place(room, 'partner', 0, 2);
  assert.equal(game.interact('partner', { id: 'revive:host' }).ok, true);
  assert.equal(player.down, false);
  assert.ok(player.invulnerableUntil > room.elapsed);
  room.players.get('partner').down = true;
  player.invulnerableUntil = 0;
  for (let i = 0; i < 8; i++) advance(250);
  assert.equal(room.phase, 'lost');
});

test('lockers hide players from creature and radio pings alert creature when close', () => {
  const { game, room, player, advance } = setup();
  game.startGame('host');
  place(room, 'host', -3.3, 3.2);
  const hideRes = game.interact('host', { id: 'locker-1' });
  assert.equal(hideRes.ok, true);
  assert.equal(player.hiding, 'locker-1');
  assert.equal(player.camera, false);
  assert.equal(player.flashlight, false);

  Object.assign(room.enemy, { x: -3.3, z: 3.5, active: true });
  advance(1000);
  assert.equal(player.down, false, 'player hiding in locker must not be downed');

  // Exit locker
  const exitRes = game.interact('host', { id: 'locker-1' });
  assert.equal(exitRes.ok, true);
  assert.equal(exitRes.hiding, false);
  assert.equal(typeof exitRes.exitX, 'number');
  assert.equal(typeof exitRes.exitZ, 'number');
  assert.equal(player.hiding, null);
  assert.equal(player.x, -3.4);
  assert.equal(player.z, 2.1);
  assert.equal(isWalkable(player.x, player.z), true);

  // Radio ping alerts creature when near
  Object.assign(room.enemy, { x: 0, z: -10, active: true, stunned: true });
  place(room, 'host', 0, -2);
  const radioRes = game.radioPing('host', { phrase: 1 });
  assert.equal(radioRes.ok, true);
  assert.equal(radioRes.heard, true);
  assert.equal(room.enemy.stunned, false);
  assert.ok(room.enemy.heardRadio);
});

test('creative mode keeps monster passive, prevents damage, and maintains full sanity', () => {
  let now = 1000;
  const game = new GameManager({ now: () => now });
  const result = game.createRoom('host', { name: 'Дослідник', solo: true, creative: true });
  const room = game.roomFor('host');
  const player = room.players.get('host');
  assert.equal(room.creative, true);
  assert.equal(room.spawnCountdown, Infinity);

  game.startGame('host');
  assert.equal(room.phase, 'playing');

  // Place enemy right on top of player
  place(room, 'host', 0, 0);
  Object.assign(room.enemy, { x: 0, z: 0.1, active: true });
  for (let i = 0; i < 10; i++) {
    now += 250;
    game.tick();
  }
  assert.equal(player.down, false, 'player in creative mode should never be downed');
  assert.equal(player.sanity, 100, 'player in creative mode should stay at 100 sanity');
  assert.equal(room.phase, 'playing');
});

test('live Socket.IO clients share objectives, enforce four-player limit, and transfer host', async (t) => {
  const server = await createGameServer({ serveClient: false });
  const address = await server.listen(0, '127.0.0.1');
  const clients = [];
  t.after(async () => { clients.forEach((client) => client.disconnect()); await server.close(); });
  async function client() {
    const socket = connect(`http://127.0.0.1:${address.port}`, { transports: ['websocket'], reconnection: false, forceNew: true });
    clients.push(socket);
    await new Promise((resolve, reject) => { socket.once('connect', resolve); socket.once('connect_error', reject); });
    return socket;
  }
  const host = await client();
  const created = await host.timeout(2000).emitWithAck('room:create', { name: 'Host', solo: false });
  assert.equal(created.ok, true);
  const guests = await Promise.all([client(), client(), client(), client()]);
  for (let i = 0; i < 3; i++) assert.equal((await guests[i].timeout(2000).emitWithAck('room:join', { code: created.code, name: `Guest ${i}` })).ok, true);
  assert.equal((await guests[3].timeout(2000).emitWithAck('room:join', { code: created.code })).ok, false);
  assert.equal((await guests[0].timeout(2000).emitWithAck('game:start')).ok, false);
  assert.equal((await host.timeout(2000).emitWithAck('game:start')).ok, true);
  assert.equal((await host.timeout(2000).emitWithAck('game:pause', { paused: true })).ok, false);
  const room = server.game.roomFor(host.id);
  place(room, host.id, -8, -3);
  const sharedState = new Promise((resolve) => {
    const listener = (state) => { if (state.tapes.includes('tape-1')) { guests[0].off('room:state', listener); resolve(state); } };
    guests[0].on('room:state', listener);
  });
  assert.equal((await host.timeout(2000).emitWithAck('game:interact', { id: 'tape-1' })).ok, true);
  const state = await sharedState;
  assert.equal(state.players.length, 4);
  assert.equal(state.enemy.active, true);
  assert.deepEqual(state.tapes, ['tape-1']);
  const newHost = guests[0].id;
  const transferred = new Promise((resolve) => {
    const listener = (next) => { if (next.hostId === newHost) { guests[0].off('room:state', listener); resolve(next); } };
    guests[0].on('room:state', listener);
  });
  host.disconnect();
  assert.equal((await transferred).players.length, 3);
  let receivedStaleRoom = false;
  const staleListener = (next) => { if (!next.players.some((player) => player.id === guests[0].id)) receivedStaleRoom = true; };
  guests[0].on('room:state', staleListener);
  assert.equal((await guests[0].timeout(2000).emitWithAck('room:leave')).ok, true);
  // TCP preserves event order: a state published during leave would arrive
  // before its ack, so no arbitrary delay is required to detect the race.
  assert.equal(receivedStaleRoom, false);
  guests[0].off('room:state', staleListener);
});

test('players can choose from 4 character classes and switch in lobby', () => {
  const { game, room } = setup();
  assert.equal(room.players.get('host').characterClass, 'engineer');
  assert.equal(game.setPlayerClass('host', { characterClass: 'operator' }).ok, true);
  assert.equal(room.players.get('host').characterClass, 'operator');
  assert.equal(game.setPlayerClass('host', { characterClass: 'invalid' }).ok, false);
  assert.equal(room.players.get('host').characterClass, 'operator');
  assert.equal(game.setPlayerClass('host', { characterClass: 'dispatcher' }).ok, true);
  assert.equal(room.players.get('host').characterClass, 'dispatcher');
});

test('arrival grace protects operators before the 35 second signal breach', () => {
  const { game, room, player, advance } = setup({ solo: true });
  game.startGame('host');
  assert.equal(room.spawnCountdown, 35);
  assert.equal(player.invulnerableUntil, 8);
  assert.ok(Math.hypot(player.x - spawn.x, player.z - spawn.z) < 1);
  Object.assign(room.enemy, { x: player.x, z: player.z, active: true });
  for (let i = 0; i < 28; i++) advance(250);
  assert.equal(player.down, false);
  assert.equal(room.elapsed, 7);
  assert.equal(room.spawnCountdown, 28);
});

test('a complete round keeps simulating after broadcast and permits walking to courtyard extraction', () => {
  updateObstacles(DEFAULT_PROPS);
  const { game, room, player, advance } = setup({ solo: true });
  game.startGame('host');
  // Exercise collision, objectives, chasing and ticking without making the
  // route test depend on player survival or how often its fixture stops.
  player.invulnerableUntil = 10000;
  let moveCount = 0;
  function walkTo(target) {
    for (let step = 0; step < 1500; step++) {
      if (Math.hypot(player.x - target.x, player.z - target.z) < 0.05) return;
      const waypoint = nextWaypoint(player, target);
      const d = Math.hypot(waypoint.x - player.x, waypoint.z - player.z);
      assert.ok(d > 0.001, `route to ${JSON.stringify(target)} from ${player.x},${player.z} exists`);
      const length = Math.min(d, 0.45);
      const next = { x: player.x + (waypoint.x - player.x) / d * length, z: player.z + (waypoint.z - player.z) / d * length };
      advance(125);
      assert.equal(game.movePlayer('host', { ...next, yaw: 0, pitch: 0, flashlight: true, camera: false, sprinting: true }), true, `move ${player.x},${player.z} to ${next.x},${next.z}; phase=${room.phase}, down=${player.down}, budget=${player.moveBudget}`);
      assert.equal(room.phase, 'playing');
      assert.equal(room.paused, false);
      assert.ok(Number.isFinite(room.enemy.x) && Number.isFinite(room.enemy.z));
      moveCount++;
    }
    assert.fail('walking route did not converge');
  }
  for (const [id, x, z] of [
    ['tape-1', -8, -1.5], ['tape-2', 8, -15],
    ['fuse-2', 9, -31.5], ['fuse-1', -8, -28],
    ['tape-3', -8, -49], ['power', -10.5, -16.5], ['broadcast', 8.7, -2],
  ]) {
    walkTo({ x, z });
    assert.equal(game.interact('host', { id }).ok, true, `interact with ${id}`);
  }
  assert.equal(room.broadcast, true);
  assert.equal(room.enemy.active, true);
  assert.equal(game.state(room).enemy.bloody, true);
  player.invulnerableUntil = 10000;
  const beforeEscape = room.elapsed;
  const beforeEnemy = { ...room.enemy };
  const extraction = objects.find((object) => object.id === 'exit');
  walkTo({ x: extraction.x, z: extraction.z - 0.4 });
  assert.ok(room.elapsed > beforeEscape + 5, 'simulation time continues during the escape');
  assert.ok(Math.hypot(room.enemy.x - beforeEnemy.x, room.enemy.z - beforeEnemy.z) > 1, 'the creature keeps moving after broadcast');
  assert.ok(moveCount > 100, 'the test traverses the actual expanded station');
  assert.equal(game.interact('host', { id: 'exit' }).ok, true);
  assert.equal(room.phase, 'won');
  assert.equal(room.enemy.active, false);
});

test('creative broadcast stays safe and a changed custom map invalidates navigation', () => {
  const { game, room, player } = setup();
  room.creative = true;
  game.startGame('host');
  room.tapes = new Set(['tape-1', 'tape-2', 'tape-3']);
  room.power = true;
  place(room, 'host', 8.7, -2);
  assert.equal(game.interact('host', { id: 'broadcast' }).ok, true);
  assert.equal(room.enemy.active, false);
  assert.equal(player.down, false);
  const from = { x: 0, z: 4 };
  const target = { x: 0, z: -4 };
  assert.deepEqual(nextWaypoint(from, target), target);
  try {
    game.syncCustomMap([...DEFAULT_PROPS, { id: 'blocking-desk', type: 'table', x: 0, z: 0, w: 10, d: 2 }]);
    assert.equal(segmentWalkable(from, target), false);
    assert.notDeepEqual(nextWaypoint(from, target), target);
  } finally {
    game.syncCustomMap(DEFAULT_PROPS);
  }
});
