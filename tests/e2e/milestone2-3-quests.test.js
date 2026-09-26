import test from 'node:test';
import assert from 'node:assert/strict';
import {
  setupTestGame,
  createLiveSocketCluster,
  rooms,
  objects,
  doors,
  DOORS,
  stationDoors,
  getDoorObstacle,
  activeObstacles,
  isWalkable,
  isObstacle,
  segmentWalkable,
  nextWaypoint,
} from './test-helpers.js';

test('M2-01: Standard station doors catalog and getDoorObstacle helper', () => {
  assert.ok(doors, 'doors catalog must be exported');
  assert.equal(doors, DOORS);
  assert.equal(doors, stationDoors);

  const expectedDoors = [
    { id: 'door-archive', x: -2.5, z: -1.5, requires: 'keycard-guard' },
    { id: 'door-generator', x: -2.5, z: -16.5, requires: 'keycard-engineer' },
    { id: 'door-studio', x: 2.5, z: -16.5, requires: null },
    { id: 'door-vent', x: -2.5, z: -31.5, requires: 'keycard-guard' },
    { id: 'door-server', x: 2.5, z: -31.5, requires: 'keycard-engineer' },
    { id: 'door-tech', x: -2.5, z: -46.5, requires: 'keycard-engineer' },
    { id: 'door-dispatch', x: 2.5, z: -46.5, requires: null },
  ];

  for (const exp of expectedDoors) {
    const d = doors[exp.id];
    assert.ok(d, `Door ${exp.id} must exist in catalog`);
    assert.equal(d.x, exp.x, `Door ${exp.id} x mismatch`);
    assert.equal(d.z, exp.z, `Door ${exp.id} z mismatch`);
    assert.equal(d.requires, exp.requires, `Door ${exp.id} key requirement mismatch`);

    // Helper when door is open returns null
    assert.equal(getDoorObstacle(exp.id, true), null);

    // Helper when door is closed returns AABB
    const obs = getDoorObstacle(exp.id, false);
    assert.ok(obs, `Door obstacle for ${exp.id} must be generated`);
    assert.equal(obs.id, exp.id);
    assert.ok(obs.minX < obs.maxX);
    assert.ok(obs.minZ < obs.maxZ);
    assert.ok(d.x >= obs.minX && d.x <= obs.maxX);
    assert.ok(d.z >= obs.minZ && d.z <= obs.maxZ);
  }
});

test('M2-02: Door interaction rejects player without required keycard with Ukrainian error message', () => {
  const { game, room, place } = setupTestGame();
  game.startGame('host');

  // Approach door-archive (-2.5, -1.5) without keycard
  place('host', -1.5, -1.5);
  const failRes = game.interact('host', { id: 'door-archive' });
  assert.equal(failRes.ok, false);
  assert.ok(failRes.error.includes('Заблоковано магнітним замком'));
  assert.ok(failRes.error.includes('картка охорони'));
  assert.equal(room.doors['door-archive'].locked, true);
  assert.equal(room.doors['door-archive'].open, false);

  // Approach door-generator (-2.5, -16.5) without engineer keycard
  place('host', -1.5, -16.5);
  const genFail = game.interact('host', { id: 'door-generator' });
  assert.equal(genFail.ok, false);
  assert.ok(genFail.error.includes('картка інженера'));

  // Studio door has no keycard requirement (requires: null), can be toggled freely
  place('host', 1.5, -16.5);
  const studioRes = game.interact('host', { id: 'door-studio' });
  assert.equal(studioRes.ok, true);
  assert.equal(studioRes.open, true);
  assert.equal(room.doors['door-studio'].open, true);
});

test('M2-03: Keycard pickup and door unlocking toggles dynamic AABB obstacles blocking passage', () => {
  const { game, room, place } = setupTestGame();
  game.startGame('host');

  // Pick up keycard-guard at courtyard Guard Booth (-15, 1.1, 14)
  place('host', -15, 14);
  const kcRes = game.interact('host', { id: 'keycard-guard' });
  assert.equal(kcRes.ok, true);
  assert.ok(room.keycards.has('keycard-guard'));
  assert.ok(room.message.includes('КАРТКА ОХОРОНИ'));

  // Go to Archive door (-2.5, -1.5)
  place('host', -1.5, -1.5);
  const unlockRes = game.interact('host', { id: 'door-archive' });
  assert.equal(unlockRes.ok, true);
  assert.equal(unlockRes.open, true);
  assert.equal(room.doors['door-archive'].locked, false);
  assert.equal(room.doors['door-archive'].open, true);

  // Doorway is open: path is clear
  assert.equal(segmentWalkable({ x: 0, z: -1.5 }, { x: -5, z: -1.5 }), true);

  // Toggle door closed
  const closeRes = game.interact('host', { id: 'door-archive' });
  assert.equal(closeRes.ok, true);
  assert.equal(closeRes.open, false);
  assert.equal(room.doors['door-archive'].open, false);

  // Door closed: dynamic obstacle is registered and blocks passage
  assert.ok(activeObstacles.some((o) => o.id === 'door-archive'));
  assert.equal(segmentWalkable({ x: 0, z: -1.5 }, { x: -5, z: -1.5 }), false);

  // Re-open door: obstacle removed and passage restored
  const reOpenRes = game.interact('host', { id: 'door-archive' });
  assert.equal(reOpenRes.ok, true);
  assert.equal(reOpenRes.open, true);
  assert.ok(!activeObstacles.some((o) => o.id === 'door-archive'));
  assert.equal(segmentWalkable({ x: 0, z: -1.5 }, { x: -5, z: -1.5 }), true);
});

test('M2-04: Team keycard sharing enables any teammate to open secured doors', () => {
  const { game, room, code, place } = setupTestGame();
  game.joinRoom('p2', { code, name: 'Partner' });
  game.startGame('host');

  // Player 1 collects keycard-engineer in Archive (-8, 1.1, 2)
  place('host', -8, 2);
  const engRes = game.interact('host', { id: 'keycard-engineer' });
  assert.equal(engRes.ok, true);
  assert.ok(room.keycards.has('keycard-engineer'));

  // Player 2 uses the team engineer keycard to open Generator room door (-2.5, -16.5)
  place('p2', -1.5, -16.5);
  const p2Res = game.interact('p2', { id: 'door-generator' });
  assert.equal(p2Res.ok, true);
  assert.equal(room.doors['door-generator'].locked, false);
  assert.equal(room.doors['door-generator'].open, true);

  // State replicated correctly
  const state = game.state(room);
  assert.ok(state.keycards.includes('keycard-engineer'));
  assert.equal(state.doors['door-generator'].open, true);
});

test('M2-05: Fuel canister collection in Courtyard Service Shed', () => {
  const { game, room, place } = setupTestGame();
  game.startGame('host');

  // fuel-canister is at (18, 1.1, 11) in the service shed
  place('host', 16.5, 11);
  const canRes = game.interact('host', { id: 'fuel-canister' });
  assert.equal(canRes.ok, true);
  assert.equal(room.canister, true);
  assert.equal(room.quest.fuelAdded, true);
  assert.ok(room.message.includes('КАНІСТРА З ПАЛИВОМ'));

  const s = game.state(room);
  assert.equal(s.canister, true);
  assert.equal(s.quest.fuelAdded, true);
});

test('M2-06: Pressure valve operation in Vent room', () => {
  const { game, room, place } = setupTestGame();
  game.startGame('host');

  // pressure-valve is at (-10.5, 1.3, -34.5)
  place('host', -10.5, -33.5);
  const vRes = game.interact('host', { id: 'pressure-valve' });
  assert.equal(vRes.ok, true);
  assert.equal(room.valveOpened, true);
  assert.equal(room.quest.valveOpened, true);
  assert.ok(room.message.includes('ВЕНТИЛЬ ТИСКУ'));

  const s = game.state(room);
  assert.equal(s.valveOpened, true);
  assert.equal(s.quest.valveOpened, true);
});

test('M2-07: Power grid startup reflects fuel and valve in status message', () => {
  const { game, room, place } = setupTestGame();
  game.startGame('host');

  // Collect 2 fuses, fuel, and valve
  room.fuses = new Set(['fuse-1', 'fuse-2']);
  room.quest.fuelAdded = true;
  room.quest.valveOpened = true;

  // Power switch at (-11, 1.4, -16.5)
  place('host', -10.5, -16.5);
  const pwrRes = game.interact('host', { id: 'power' });
  assert.equal(pwrRes.ok, true);
  assert.equal(room.power, true);
  assert.equal(room.quest.power, true);
  assert.ok(room.message.includes('Живлення відновлено'));
  assert.ok(room.message.includes('Паливо заправлено'));
  assert.ok(room.message.includes('тиск у нормі'));
});

test('M2-08: Signal calibration minigame validates proximity and 96.4 MHz tolerance', () => {
  const { game, room, place } = setupTestGame();
  game.startGame('host');

  // Broadcast console is at (10, 1.2, -2)
  // 1. Far away: fails proximity check
  place('host', 0, -2);
  const farRes = game.calibrate('host', { frequency: 96.4 });
  assert.equal(farRes.ok, false);
  assert.ok(farRes.error.includes('ближче до пульта'));

  // 2. Near console, but incorrect frequency: fails tolerance check
  place('host', 10, -2);
  const badFreq = game.calibrate('host', { frequency: 101.5 });
  assert.equal(badFreq.ok, false);
  assert.ok(badFreq.error.includes('96.4 МГц'));
  assert.equal(room.quest.calibrated, false);

  // 3. Near console with exact 96.4 MHz: succeeds
  const goodFreq = game.calibrate('host', { frequency: 96.4 });
  assert.equal(goodFreq.ok, true);
  assert.equal(goodFreq.frequency, 96.4);
  assert.equal(room.calibrated, true);
  assert.equal(room.quest.calibrated, true);
  assert.ok(room.message.includes('ЧАСТОТУ НАЛАШТОВАНО'));

  const s = game.state(room);
  assert.equal(s.calibrated, true);
  assert.equal(s.quest.calibrated, true);
});

test('M2-09: Calibrated broadcast triggers Phase 3 pursuit and updates message with frequency', () => {
  const { game, room, place } = setupTestGame();
  game.startGame('host');

  room.power = true;
  room.quest.power = true;
  room.tapes = new Set(['tape-1', 'tape-2', 'tape-3']);
  room.quest.calibrated = true;

  place('host', 10, -2);
  const bcRes = game.interact('host', { id: 'broadcast' });
  assert.equal(bcRes.ok, true);
  assert.equal(room.broadcast, true);
  assert.equal(room.quest.broadcast, true);
  assert.ok(room.message.includes('96.4 МГц'));
  assert.equal(room.quest.stage, 4);
});

test('M2-10: Live Socket.IO multi-client quest synchronization and calibration', async (t) => {
  const cluster = await createLiveSocketCluster();
  t.after(async () => await cluster.cleanup());

  const host = await cluster.connectClient();
  const cRes = await host.timeout(3000).emitWithAck('room:create', { name: 'HostLeader' });
  assert.equal(cRes.ok, true);

  const guest = await cluster.connectClient();
  const jRes = await guest.timeout(3000).emitWithAck('room:join', { code: cRes.code, name: 'GuestEngineer' });
  assert.equal(jRes.ok, true);

  const startRes = await host.timeout(3000).emitWithAck('game:start');
  assert.equal(startRes.ok, true);

  const room = cluster.server.game.roomFor(host.id);

  // Position host at broadcast console and guest in hallway
  Object.assign(room.players.get(host.id), { x: 10, z: -2 });

  // Guest listens for state containing calibrated quest
  const calibratedPromise = new Promise((resolve) => {
    const handler = (state) => {
      if (state.quest?.calibrated) {
        guest.off('room:state', handler);
        resolve(state);
      }
    };
    guest.on('room:state', handler);
  });

  // Host sends game:calibrate event
  const calRes = await host.timeout(3000).emitWithAck('game:calibrate', { frequency: 96.4 });
  assert.equal(calRes.ok, true);

  const receivedState = await calibratedPromise;
  assert.equal(receivedState.calibrated, true);
  assert.equal(receivedState.quest.calibrated, true);
  assert.ok(Array.isArray(receivedState.keycards));
  assert.ok(receivedState.doors['door-archive']);
});
