export const spawn = { x: -5, z: 19 };
export const courtyard = { minX: -22, maxX: 22, minZ: 6, maxZ: 36 };
export const extraction = { x: -5.1, y: 1.2, z: 23 };
export const exteriorObstacles = [
  { id: 'crew-van', minX: -8.35, maxX: -5.65, minZ: 20.35, maxZ: 25.65 },
  { id: 'service-shed', minX: 15.8, maxX: 20.8, minZ: 8.3, maxZ: 13.7 },
];

// Contiguous 15-meter sectors: ZERO black gaps/holes!
// From z = 6 down to z = -54. Entire floor is solid and continuous.
export const rooms = [
  { name: 'ГОЛОВНИЙ КОРИДОР', minX: -2.5, maxX: 2.5, minZ: -54, maxZ: 6 },
  { name: 'АРХІВ', minX: -12, maxX: -2.5, minZ: -9, maxZ: 6 },
  { name: 'АПАРАТНА', minX: 2.5, maxX: 12, minZ: -9, maxZ: 6 },
  { name: 'ГЕНЕРАТОРНА', minX: -12, maxX: -2.5, minZ: -24, maxZ: -9 },
  { name: 'СТУДІЯ 04', minX: 2.5, maxX: 12, minZ: -24, maxZ: -9 },
  { name: 'ВЕНТВУЗОЛ', minX: -12, maxX: -2.5, minZ: -39, maxZ: -24 },
  { name: 'СЕРВЕРНА', minX: 2.5, maxX: 12, minZ: -39, maxZ: -24 },
  { name: 'ТЕХСКЛАД', minX: -12, maxX: -2.5, minZ: -54, maxZ: -39 },
  { name: 'ДИСПЕТЧЕРСЬКА', minX: 2.5, maxX: 12, minZ: -54, maxZ: -39 },
  { name: 'ДВІР СТАНЦІЇ', ...courtyard },
];

export const objects = [
  { id: 'tape-1', x: -8, y: 1.1, z: -3, type: 'tape', label: 'Касета 01 · Нічна зміна' },
  { id: 'tape-2', x: 8, y: 1.1, z: -16.5, type: 'tape', label: 'Касета 02 · Зайвий голос' },
  { id: 'tape-3', x: -8, y: 1.1, z: -50.5, type: 'tape', label: 'Касета 03 · Останній ефір' },
  { id: 'fuse-1', x: -8, y: 1.1, z: -26.5, type: 'fuse', label: 'Запобіжник 380В (Вентвузол)' },
  { id: 'fuse-2', x: 10.5, y: 1.1, z: -31.5, type: 'fuse', label: 'Запобіжник 380В (Серверна)' },
  { id: 'power', x: -11, y: 1.4, z: -16.5, type: 'power', label: 'Електрощит генератора' },
  { id: 'broadcast', x: 10, y: 1.2, z: -2, type: 'broadcast', label: 'Передати запис' },
  { id: 'exit', ...extraction, type: 'exit', label: 'Сісти у фургон · покинути станцію' },
];

export const lockers = [
  { id: 'locker-1', x: -3.4, y: 1.1, z: 3.5, ry: Math.PI, exitX: -3.4, exitZ: 2.1, label: 'Шафа в архіві' },
  { id: 'locker-2', x: -3.4, y: 1.1, z: -21.5, ry: Math.PI, exitX: -3.4, exitZ: -22.8, label: 'Шафа в генераторній' },
  { id: 'locker-3', x: 3.4, y: 1.1, z: -36.5, ry: 0, exitX: 3.4, exitZ: -35.2, label: 'Шафа в серверній' },
  { id: 'locker-4', x: -2.0, y: 1.1, z: -12.0, ry: Math.PI / 2, exitX: -1.0, exitZ: -12.0, label: 'Шафа в коридорі' },
  { id: 'locker-5', x: -11.0, y: 1.1, z: -46.5, ry: Math.PI / 2, exitX: -9.8, exitZ: -46.5, label: 'Шафа на техскладі' },
  { id: 'locker-6', x: 11.0, y: 1.1, z: -46.5, ry: -Math.PI / 2, exitX: 9.8, exitZ: -46.5, label: 'Шафа в диспетчерській' },
];

import { DEFAULT_PROPS } from './props.js';

export function propToObstacle(p) {
  if (!p || typeof p.x !== 'number' || typeof p.z !== 'number') return null;
  const rot = p.rotation || 0;
  const cos = Math.abs(Math.cos(rot));
  const sin = Math.abs(Math.sin(rot));

  switch (p.type) {
    case 'table': {
      const w = (p.w || 2.2) * 0.44;
      const d = (p.d || 1.2) * 0.44;
      const hx = w * cos + d * sin;
      const hz = w * sin + d * cos;
      return { id: p.id, type: 'table', minX: p.x - hx, maxX: p.x + hx, minZ: p.z - hz, maxZ: p.z + hz };
    }
    case 'shelf': {
      const w = (p.w || 2.1) * 0.44;
      const d = (p.d || 0.6) * 0.44;
      const hx = w * cos + d * sin;
      const hz = w * sin + d * cos;
      return { id: p.id, type: 'shelf', minX: p.x - hx, maxX: p.x + hx, minZ: p.z - hz, maxZ: p.z + hz };
    }
    case 'serverRack': {
      const w = (p.w || 0.85) * 0.44;
      const d = (p.d || 0.8) * 0.44;
      const hx = w * cos + d * sin;
      const hz = w * sin + d * cos;
      return { id: p.id, type: 'serverRack', minX: p.x - hx, maxX: p.x + hx, minZ: p.z - hz, maxZ: p.z + hz };
    }
    case 'crate': {
      const s = (p.size || 0.9) * 0.45;
      return { id: p.id, type: 'crate', minX: p.x - s, maxX: p.x + s, minZ: p.z - s, maxZ: p.z + s };
    }
    case 'barrel': {
      const r = 0.35;
      return { id: p.id, type: 'barrel', minX: p.x - r, maxX: p.x + r, minZ: p.z - r, maxZ: p.z + r };
    }
    case 'camera': {
      const r = 0.36;
      return { id: p.id, type: 'camera', minX: p.x - r, maxX: p.x + r, minZ: p.z - r, maxZ: p.z + r };
    }
    case 'locker': {
      return { id: p.id, type: 'locker', minX: p.x - 0.38, maxX: p.x + 0.38, minZ: p.z - 0.35, maxZ: p.z + 0.35 };
    }
    case 'guardBooth': {
      const w = (p.w || 2.4) * 0.48;
      const d = (p.d || 2.2) * 0.48;
      const hx = w * cos + d * sin;
      const hz = w * sin + d * cos;
      return { id: p.id, type: 'guardBooth', minX: p.x - hx, maxX: p.x + hx, minZ: p.z - hz, maxZ: p.z + hz };
    }
    case 'barrier': {
      const w = (p.w || 3.0) * 0.48;
      const d = (p.d || 0.4) * 0.48;
      const hx = w * cos + d * sin;
      const hz = w * sin + d * cos;
      return { id: p.id, type: 'barrier', minX: p.x - hx, maxX: p.x + hx, minZ: p.z - hz, maxZ: p.z + hz };
    }
    case 'floodlightTower': {
      const r = 0.6;
      return { id: p.id, type: 'floodlightTower', minX: p.x - r, maxX: p.x + r, minZ: p.z - r, maxZ: p.z + r };
    }
    case 'cableSpool': {
      const r = 0.55;
      return { id: p.id, type: 'cableSpool', minX: p.x - r, maxX: p.x + r, minZ: p.z - r, maxZ: p.z + r };
    }
    case 'abandonedCar': {
      const w = (p.w || 2.1) * 0.46;
      const d = (p.d || 4.2) * 0.46;
      const hx = w * cos + d * sin;
      const hz = w * sin + d * cos;
      return { id: p.id, type: 'abandonedCar', minX: p.x - hx, maxX: p.x + hx, minZ: p.z - hz, maxZ: p.z + hz };
    }
    case 'turnstile': {
      return [
        { id: `${p.id}_l`, type: 'turnstile', minX: p.x - 1.4, maxX: p.x - 0.55, minZ: p.z - 0.35, maxZ: p.z + 0.35 },
        { id: `${p.id}_r`, type: 'turnstile', minX: p.x + 0.55, maxX: p.x + 1.4, minZ: p.z - 0.35, maxZ: p.z + 0.35 },
      ];
    }
    case 'trashBin': {
      const r = 0.26;
      return { id: p.id, type: 'trashBin', minX: p.x - r, maxX: p.x + r, minZ: p.z - r, maxZ: p.z + r };
    }
    case 'cardCatalog': {
      const w = (p.w || 1.6) * 0.45;
      const d = (p.d || 0.7) * 0.45;
      const hx = w * cos + d * sin;
      const hz = w * sin + d * cos;
      return { id: p.id, type: 'cardCatalog', minX: p.x - hx, maxX: p.x + hx, minZ: p.z - hz, maxZ: p.z + hz };
    }
    case 'archivistDesk': {
      const w = (p.w || 2.2) * 0.45;
      const d = (p.d || 1.2) * 0.45;
      const hx = w * cos + d * sin;
      const hz = w * sin + d * cos;
      return { id: p.id, type: 'archivistDesk', minX: p.x - hx, maxX: p.x + hx, minZ: p.z - hz, maxZ: p.z + hz };
    }
    case 'vhsStack': {
      const w = (p.w || 0.75) * 0.45;
      const d = (p.d || 0.55) * 0.45;
      const hx = w * cos + d * sin;
      const hz = w * sin + d * cos;
      return { id: p.id, type: 'vhsStack', minX: p.x - hx, maxX: p.x + hx, minZ: p.z - hz, maxZ: p.z + hz };
    }
    case 'anchorDesk': {
      const w = (p.w || 3.4) * 0.45;
      const d = (p.d || 1.4) * 0.45;
      const hx = w * cos + d * sin;
      const hz = w * sin + d * cos;
      return { id: p.id, type: 'anchorDesk', minX: p.x - hx, maxX: p.x + hx, minZ: p.z - hz, maxZ: p.z + hz };
    }
    case 'chromakeyBackdrop': {
      const w = (p.w || 4.6) * 0.48;
      const d = (p.d || 0.3) * 0.48;
      const hx = w * cos + d * sin;
      const hz = w * sin + d * cos;
      return { id: p.id, type: 'chromakeyBackdrop', minX: p.x - hx, maxX: p.x + hx, minZ: p.z - hz, maxZ: p.z + hz };
    }
    case 'micStand': {
      const r = 0.22;
      return { id: p.id, type: 'micStand', minX: p.x - r, maxX: p.x + r, minZ: p.z - r, maxZ: p.z + r };
    }
    case 'dieselGenerator': {
      const w = (p.w || 3.2) * 0.46;
      const d = (p.d || 1.8) * 0.46;
      const hx = w * cos + d * sin;
      const hz = w * sin + d * cos;
      return { id: p.id, type: 'dieselGenerator', minX: p.x - hx, maxX: p.x + hx, minZ: p.z - hz, maxZ: p.z + hz };
    }
    case 'fuelCanister': {
      const r = 0.24;
      return { id: p.id, type: 'fuelCanister', minX: p.x - r, maxX: p.x + r, minZ: p.z - r, maxZ: p.z + r };
    }
    case 'fuelValve': {
      const r = 0.26;
      return { id: p.id, type: 'fuelValve', minX: p.x - r, maxX: p.x + r, minZ: p.z - r, maxZ: p.z + r };
    }
    case 'fusePanel': {
      const w = (p.w || 1.1) * 0.45;
      const d = (p.d || 0.35) * 0.45;
      const hx = w * cos + d * sin;
      const hz = w * sin + d * cos;
      return { id: p.id, type: 'fusePanel', minX: p.x - hx, maxX: p.x + hx, minZ: p.z - hz, maxZ: p.z + hz };
    }
    case 'airDuct': {
      const r = 0.52;
      return { id: p.id, type: 'airDuct', minX: p.x - r, maxX: p.x + r, minZ: p.z - r, maxZ: p.z + r };
    }
    case 'mixingConsole': {
      const w = (p.w || 2.6) * 0.46;
      const d = (p.d || 1.4) * 0.46;
      const hx = w * cos + d * sin;
      const hz = w * sin + d * cos;
      return { id: p.id, type: 'mixingConsole', minX: p.x - hx, maxX: p.x + hx, minZ: p.z - hz, maxZ: p.z + hz };
    }
    case 'monitorWall': {
      const w = (p.w || 2.2) * 0.45;
      const d = (p.d || 0.7) * 0.45;
      const hx = w * cos + d * sin;
      const hz = w * sin + d * cos;
      return { id: p.id, type: 'monitorWall', minX: p.x - hx, maxX: p.x + hx, minZ: p.z - hz, maxZ: p.z + hz };
    }
    default:
      return null;
  }
}

export const activeObstacles = [];

export function updateObstacles(props = DEFAULT_PROPS) {
  activeObstacles.length = 0;
  if (Array.isArray(props)) {
    for (const p of props) {
      const obs = propToObstacle(p);
      if (obs) {
        if (Array.isArray(obs)) activeObstacles.push(...obs);
        else activeObstacles.push(obs);
      }
    }
  }
}

// Initialize active obstacles with DEFAULT_PROPS
updateObstacles(DEFAULT_PROPS);

// Physical obstacles alias for backward compatibility
export const obstacles = activeObstacles;

export function isObstacle(x, z, radius = 0.22) {
  for (const o of [...activeObstacles, ...exteriorObstacles]) {
    if (x >= o.minX - radius && x <= o.maxX + radius && z >= o.minZ - radius && z <= o.maxZ + radius) {
      return true;
    }
  }
  return false;
}

export function isInsideWorld(x, z, radius = 0.22) {
  // Inside any room
  const inside = rooms.some(r => x >= r.minX + radius && x <= r.maxX - radius && z >= r.minZ + radius && z <= r.maxZ - radius);

  // Corridor doorways (doors at z = -1.5, -16.5, -31.5, -46.5)
  const corridorDoors = [-2.5, 2.5].some(dx =>
    Math.abs(x - dx) < radius + 0.35 && [-1.5, -16.5, -31.5, -46.5].some(dz => Math.abs(z - dz) < 1.3 - radius)
  );

  // Connecting loop doorways at partition wall z = -24 (connecting Generator to Vent, and Studio to Server)
  const loopDoors = [-10.5, 10.5].some(tx =>
    Math.abs(x - tx) < 1.3 - radius && Math.abs(z - (-24)) < radius + 0.35
  );

  const entrance = Math.abs(x) < 1.6 - radius && Math.abs(z - 6) < radius + 0.36;
  return inside || corridorDoors || loopDoors || entrance;
}

export function isWalkable(x, z, radius = 0.22, checkObstacles = true) {
  if (!Number.isFinite(x) || !Number.isFinite(z)) return false;
  if (checkObstacles && isObstacle(x, z, radius)) return false;
  return isInsideWorld(x, z, radius);
}

export function roomAt(x, z) {
  return rooms.find(r => x >= r.minX && x <= r.maxX && z >= r.minZ && z <= r.maxZ)?.name || 'ГОЛОВНИЙ КОРИДОР';
}
