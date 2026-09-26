import assert from 'node:assert/strict';
import { io as connect } from 'socket.io-client';
import { GameManager, nextWaypoint, segmentWalkable } from '../../server/game.js';
import { createGameServer } from '../../server/index.js';
import {
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
  doors,
  DOORS,
  getDoorObstacle,
} from '../../shared/world-data.js';
import { DEFAULT_PROPS, PROP_CATALOG } from '../../shared/props.js';
import { CHARACTER_CLASSES, DEFAULT_CLASS } from '../../shared/classes.js';

export {
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
  doors,
  DOORS,
  getDoorObstacle,
  DEFAULT_PROPS,
  PROP_CATALOG,
  CHARACTER_CLASSES,
  DEFAULT_CLASS,
  nextWaypoint,
  segmentWalkable,
};

export function setupTestGame({ solo = false, creative = false, hostClass = 'engineer' } = {}) {
  let now = 1000;
  const game = new GameManager({ now: () => now });
  const result = game.createRoom('host', { name: 'HostOperator', solo, creative, characterClass: hostClass });
  const room = game.roomFor('host');
  const hostPlayer = room.players.get('host');

  function advance(ms) {
    now += ms;
    game.tick();
  }

  function place(playerId, x, z) {
    const p = room.players.get(playerId);
    if (p) Object.assign(p, { x, z });
  }

  function walkTo(playerId, target, { maxSteps = 1500, stepLength = 0.45, tickMs = 125 } = {}) {
    const p = room.players.get(playerId);
    assert.ok(p, `Player ${playerId} must exist in room`);
    p.invulnerableUntil = Math.max(p.invulnerableUntil || 0, 100000);

    for (let step = 0; step < maxSteps; step++) {
      const dist = Math.hypot(p.x - target.x, p.z - target.z);
      if (dist < 0.08) return true;

      const waypoint = nextWaypoint(p, target);
      const d = Math.hypot(waypoint.x - p.x, waypoint.z - p.z);
      assert.ok(d > 0.0001, `Valid path must exist from (${p.x},${p.z}) to target (${target.x},${target.z})`);

      const moveLen = Math.min(d, stepLength);
      const nextX = p.x + ((waypoint.x - p.x) / d) * moveLen;
      const nextZ = p.z + ((waypoint.z - p.z) / d) * moveLen;

      advance(tickMs);
      const moved = game.movePlayer(playerId, {
        x: nextX,
        z: nextZ,
        yaw: 0,
        pitch: 0,
        flashlight: true,
        camera: false,
        sprinting: true,
      });
      assert.equal(moved, true, `Move step ${step} from (${p.x.toFixed(2)},${p.z.toFixed(2)}) to (${nextX.toFixed(2)},${nextZ.toFixed(2)}) failed`);
    }
    assert.fail(`walkTo did not converge to (${target.x},${target.z}) after ${maxSteps} steps`);
  }

  return {
    game,
    room,
    code: result.code,
    hostPlayer,
    advance,
    place,
    walkTo,
    getNow: () => now,
  };
}

export function createTestCanvasDocument() {
  return {
    createElement(tag) {
      assert.equal(tag, 'canvas');
      const context = new Proxy(
        {
          getImageData: (_x, _y, width, height) => ({ data: new Uint8ClampedArray(width * height * 4) }),
          measureText: (text) => ({ width: String(text).length * 8 }),
          createLinearGradient: () => ({ addColorStop() {} }),
          createRadialGradient: () => ({ addColorStop() {} }),
        },
        { get: (object, key) => object[key] ?? (() => {}) }
      );
      return { width: 128, height: 128, getContext: () => context };
    },
  };
}

export async function createLiveSocketCluster() {
  const server = await createGameServer({ serveClient: false });
  const address = await server.listen(0, '127.0.0.1');
  const sockets = [];

  async function connectClient() {
    const socket = connect(`http://127.0.0.1:${address.port}`, {
      transports: ['websocket'],
      reconnection: false,
      forceNew: true,
    });
    sockets.push(socket);
    await new Promise((resolve, reject) => {
      socket.once('connect', resolve);
      socket.once('connect_error', reject);
    });
    return socket;
  }

  async function cleanup() {
    sockets.forEach((s) => s.disconnect());
    await server.close();
  }

  return {
    server,
    port: address.port,
    connectClient,
    cleanup,
  };
}
