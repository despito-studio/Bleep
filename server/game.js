import { randomInt } from 'node:crypto';
import { isWalkable, spawn, lockers, objects, rooms, exteriorObstacles, activeObstacles, updateObstacles } from '../shared/world-data.js';
import { CHARACTER_CLASSES, DEFAULT_CLASS, getClassData } from '../shared/classes.js';

const COLORS = ['#4e7048', '#a67d43', '#7a3e35', '#3d5975'];
const CODE_CHARS = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
// Client prompts and authoritative interactions use the same map coordinates.
const OBJECTIVES = Object.fromEntries(objects.map(({ id, x, z }) => [id, { x, z }]));

const WAYPOINTS = [
  // Open station entrance and courtyard route back to the crew's van.
  { x: 0, z: 5.4 }, { x: 0, z: 6.6 }, { x: 0, z: 10 },
  { x: 0, z: 17 }, { x: -5, z: 19 }, { ...OBJECTIVES.exit },
  // Коридорна вісь (від виходу до глибини)
  { x: 0, z: 4 }, { x: 0, z: -2 }, { x: 0, z: -17 }, { x: 0, z: -24.5 },
  { x: 0, z: -32 }, { x: 0, z: -39.5 }, { x: 0, z: -47 }, { x: 0, z: -52 },

  // Архів і Апаратна
  { x: -4, z: -2 }, { x: -8, z: -2 },
  { x: 4, z: -2 }, { x: 8, z: -2 },

  // Генераторна і Студія 04
  { x: -4, z: -17 }, { x: -8, z: -17 },
  { x: 4, z: -17 }, { x: 8, z: -17 },

  // Вентвузол і Серверна
  { x: -4, z: -32 }, { x: -8, z: -32 },
  { x: 4, z: -32 }, { x: 8, z: -32 },

  // Техсклад і Диспетчерська
  { x: -4, z: -47 }, { x: -8, z: -47 },
  { x: 4, z: -47 }, { x: 8, z: -47 },

  // Бічні обхідні тунелі (loops)
  { x: -10.25, z: -20 }, { x: -10.25, z: -24.5 }, { x: -10.25, z: -29 },
  { x: 10.25, z: -20 }, { x: 10.25, z: -24.5 }, { x: 10.25, z: -29 },
];

const distance = (a, b) => Math.hypot(a.x - b.x, a.z - b.z);
const failure = (error) => ({ ok: false, error });
let navigationCache = null;
let boundaryObstacles = [];
const boundaryCache = new Map();

function cleanName(name) {
  return (typeof name === 'string' ? name.replace(/[\u0000-\u001f\u007f<>]/g, '').trim() : '').slice(0, 20) || 'Оператор';
}

export function segmentWalkable(a, b, radius = 0.22, checkObstacles = true) {
  if (![a?.x, a?.z, b?.x, b?.z, radius].every(Number.isFinite) || radius < 0) return false;
  const boundaries = collisionBoundaries(radius, checkObstacles);
  const dx = b.x - a.x;
  const dz = b.z - a.z;
  const cuts = [0, 1];
  for (const x of boundaries.x) {
    const t = (x - a.x) / dx;
    if (t > 0 && t < 1) cuts.push(t);
  }
  for (const z of boundaries.z) {
    const t = (z - a.z) / dz;
    if (t > 0 && t < 1) cuts.push(t);
  }
  cuts.sort((left, right) => left - right);
  // Collision geometry is a union of axis-aligned rectangles. Testing each
  // boundary and interval midpoint catches arbitrarily thin corner clips that
  // regularly spaced samples can miss, regardless of segment length.
  for (let index = 0; index < cuts.length; index++) {
    const t = cuts[index];
    if (!isWalkable(a.x + (b.x - a.x) * t, a.z + (b.z - a.z) * t, radius, checkObstacles)) return false;
    if (index > 0) {
      const midpoint = (t + cuts[index - 1]) / 2;
      if (!isWalkable(a.x + dx * midpoint, a.z + dz * midpoint, radius, checkObstacles)) return false;
    }
  }
  return true;
}

function collisionBoundaries(radius, checkObstacles) {
  if (boundaryObstacles.length !== activeObstacles.length || boundaryObstacles.some((obstacle, index) => obstacle !== activeObstacles[index])) {
    boundaryObstacles = [...activeObstacles];
    boundaryCache.clear();
  }
  const key = `${radius}:${checkObstacles}`;
  if (boundaryCache.has(key)) return boundaryCache.get(key);
  const x = new Set();
  const z = new Set();
  for (const room of rooms) {
    x.add(room.minX + radius).add(room.maxX - radius);
    z.add(room.minZ + radius).add(room.maxZ - radius);
  }
  // Bounds of the doorway rectangles used by shared isInsideWorld().
  for (const center of [-2.5, 2.5]) x.add(center - radius - 0.35).add(center + radius + 0.35);
  for (const center of [-1.5, -16.5, -31.5, -46.5]) z.add(center - 1.3 + radius).add(center + 1.3 - radius);
  for (const center of [-10.5, 10.5]) x.add(center - 1.3 + radius).add(center + 1.3 - radius);
  z.add(-24 - radius - 0.35).add(-24 + radius + 0.35);
  x.add(-1.6 + radius).add(1.6 - radius);
  z.add(6 - radius - 0.36).add(6 + radius + 0.36);
  if (checkObstacles) for (const obstacle of [...activeObstacles, ...exteriorObstacles]) {
    x.add(obstacle.minX - radius).add(obstacle.maxX + radius);
    z.add(obstacle.minZ - radius).add(obstacle.maxZ + radius);
  }
  const result = { x: [...x], z: [...z] };
  boundaryCache.set(key, result);
  return result;
}

function navigationGraph() {
  const signature = JSON.stringify(activeObstacles);
  if (navigationCache?.signature === signature) return navigationCache;
  const edges = WAYPOINTS.map(() => []);
  for (let a = 0; a < WAYPOINTS.length; a++) {
    for (let b = a + 1; b < WAYPOINTS.length; b++) {
      if (!segmentWalkable(WAYPOINTS[a], WAYPOINTS[b])) continue;
      const cost = distance(WAYPOINTS[a], WAYPOINTS[b]);
      edges[a].push({ index: b, cost });
      edges[b].push({ index: a, cost });
    }
  }
  navigationCache = { signature, edges };
  return navigationCache;
}

// A visibility graph gives the creature the same doorways and loop tunnels as players.
export function nextWaypoint(from, to) {
  if (segmentWalkable(from, to)) return to;
  const nodes = [from, ...WAYPOINTS, to];
  const graph = navigationGraph();
  const edges = nodes.map(() => []);
  for (let i = 0; i < WAYPOINTS.length; i++) {
    edges[i + 1] = graph.edges[i].map(({ index, cost }) => ({ index: index + 1, cost }));
    if (segmentWalkable(from, WAYPOINTS[i])) edges[0].push({ index: i + 1, cost: distance(from, WAYPOINTS[i]) });
    if (segmentWalkable(WAYPOINTS[i], to)) edges[i + 1].push({ index: nodes.length - 1, cost: distance(WAYPOINTS[i], to) });
  }
  const costs = nodes.map(() => Infinity);
  const previous = nodes.map(() => -1);
  const visited = new Set();
  costs[0] = 0;
  for (let count = 0; count < nodes.length; count++) {
    let current = -1;
    for (let i = 0; i < nodes.length; i++) {
      if (!visited.has(i) && (current === -1 || costs[i] < costs[current])) current = i;
    }
    if (current === -1 || !Number.isFinite(costs[current])) break;
    if (current === nodes.length - 1) break;
    visited.add(current);
    for (const { index, cost } of edges[current]) {
      if (visited.has(index)) continue;
      const proposed = costs[current] + cost;
      if (proposed < costs[index]) {
        costs[index] = proposed;
        previous[index] = current;
      }
    }
  }
  let step = nodes.length - 1;
  if (previous[step] < 0) return from;
  while (previous[step] > 0) step = previous[step];
  return nodes[step];
}

export class GameManager {
  constructor({ now = () => Date.now(), onChange = () => {} } = {}) {
    this.rooms = new Map();
    this.playerRooms = new Map();
    this.now = now;
    this.onChange = onChange;
    this.lockers = [...lockers];
  }

  syncCustomMap(props) {
    if (!Array.isArray(props)) return;
    updateObstacles(props);
    const customLockers = props.filter(p => p.type === 'locker').map(p => ({
      id: p.id,
      x: p.x,
      y: 1.1,
      z: p.z,
      ry: p.rotation || 0,
      exitX: p.exitX ?? Number((p.x + Math.sin(p.rotation || 0) * 1.3).toFixed(2)),
      exitZ: p.exitZ ?? Number((p.z + Math.cos(p.rotation || 0) * 1.3).toFixed(2)),
      label: p.label || 'Шафа',
    }));
    if (customLockers.length > 0) {
      this.lockers = customLockers;
    } else {
      this.lockers = [...lockers];
    }
  }

  roomFor(playerId) {
    return this.rooms.get(this.playerRooms.get(playerId));
  }

  state(room) {
    return {
      code: room.code,
      hostId: room.hostId,
      phase: room.phase,
      solo: room.solo,
      creative: Boolean(room.creative),
      paused: room.paused,
      players: [...room.players.values()].map(({ id, name, color, characterClass, x, z, yaw, pitch, flashlight, camera, sprinting, down, hiding, sanity }) => ({
        id, name, color, characterClass: characterClass || 'engineer', x, z, yaw, pitch, flashlight, camera, sprinting, down, hiding, sanity: Math.round(sanity ?? 100),
      })),
      tapes: [...room.tapes],
      fuses: [...(room.fuses || [])],
      power: room.power,
      broadcast: room.broadcast,
      spawnCountdown: Math.ceil(room.spawnCountdown || 0),
      enemy: { ...room.enemy, bloody: Boolean(room.broadcast) },
      radioTransmission: room.radioTransmission || null,
      elapsed: room.elapsed,
      scare: room.scare,
      message: room.message,
    };
  }

  publish(room) {
    this.onChange(room.code, this.state(room));
  }

  makePlayer(id, name, room, requestedClass) {
    const defaultClasses = ['engineer', 'operator', 'journalist', 'dispatcher'];
    const slot = room.players.size;
    const charClass = (requestedClass && CHARACTER_CLASSES[requestedClass]) ? requestedClass : defaultClasses[slot % defaultClasses.length];
    const classData = getClassData(charClass);
    return {
      id, name: cleanName(name), color: classData.color, characterClass: charClass,
      x: spawn.x + (slot % 2 ? 0.42 : -0.42), z: spawn.z + (slot >= 2 ? 0.42 : 0),
      yaw: 0, pitch: 0, flashlight: true, camera: false, sprinting: false, down: false,
      hiding: null, sanity: 100,
      moveBudget: 0.7, moveUpdated: this.now(), contact: 0, invulnerableUntil: (room.elapsed || 0) + 8,
    };
  }

  setPlayerClass(playerId, input = {}) {
    const room = this.roomFor(playerId);
    if (!room) return failure('Спочатку приєднайтеся до кімнати.');
    const player = room.players.get(playerId);
    if (!player) return failure('Гравця не знайдено.');
    const classId = input.characterClass;
    if (CHARACTER_CLASSES[classId]) {
      player.characterClass = classId;
      player.color = CHARACTER_CLASSES[classId].color;
      this.publish(room);
      return { ok: true, characterClass: classId };
    }
    return failure('Невідомий клас персонажа.');
  }

  createRoom(playerId, input = {}) {
    if (this.rooms.size >= 128) return failure('Сервер зайнятий. Спробуйте пізніше.');
    this.leaveRoom(playerId);
    let code;
    do { code = Array.from({ length: 5 }, () => CODE_CHARS[randomInt(CODE_CHARS.length)]).join(''); } while (this.rooms.has(code));
    const isCreative = input.creative === true;
    const room = {
      code, hostId: playerId, solo: input.solo === true, creative: isCreative, phase: 'lobby', paused: false,
      players: new Map(), tapes: new Set(), fuses: new Set(), power: false, broadcast: false,
      spawnCountdown: isCreative ? Infinity : 35,
      enemy: { x: 0, z: -48, active: false, stunned: false },
      elapsed: 0, scare: 0, message: isCreative ? 'РЕЖИМ ДОСЛІДЖЕННЯ: Істота пасивна. Досліджуйте станцію.' : 'Сигнал установлено. Чекаємо на операторів.',
      lastTick: this.now(), nextScare: 35,
    };
    room.players.set(playerId, this.makePlayer(playerId, input.name, room, input.characterClass));
    this.rooms.set(code, room);
    this.playerRooms.set(playerId, code);
    this.publish(room);
    return { ok: true, code, playerId };
  }

  joinRoom(playerId, input = {}) {
    const code = typeof input.code === 'string' ? input.code.trim().toUpperCase() : '';
    const room = this.rooms.get(code);
    if (!room) return failure('Кімнату не знайдено. Перевірте код.');
    if (room.players.has(playerId)) return { ok: true, code, playerId };
    if (room.solo) return failure('Це одиночний сеанс.');
    if (room.players.size >= 4) return failure('У кімнаті вже 4 оператори.');
    if (room.phase === 'won' || room.phase === 'lost') return failure('Цей сеанс завершено. Зачекайте нового.');
    this.leaveRoom(playerId);
    room.players.set(playerId, this.makePlayer(playerId, input.name, room, input.characterClass));
    this.playerRooms.set(playerId, code);
    room.paused = false;
    room.message = `${room.players.get(playerId).name} приєднався до ефіру.`;
    this.publish(room);
    return { ok: true, code, playerId };
  }

  leaveRoom(playerId) {
    const room = this.roomFor(playerId);
    this.playerRooms.delete(playerId);
    if (!room) return { ok: true };
    room.players.delete(playerId);
    if (!room.players.size) {
      this.rooms.delete(room.code);
      return { ok: true };
    }
    if (room.hostId === playerId) room.hostId = room.players.keys().next().value;
    room.message = 'Один із сигналів зник. Залишайтеся разом.';
    this.checkDefeat(room);
    this.publish(room);
    return { ok: true };
  }

  resetRound(room) {
    room.phase = 'playing';
    room.paused = false;
    room.tapes.clear();
    room.fuses.clear();
    room.power = false;
    room.broadcast = false;
    room.spawnCountdown = room.creative ? Infinity : 35;
    room.enemy = { x: 0, z: -48, active: false, stunned: false, heardRadio: null };
    room.radioTransmission = null;
    room.elapsed = 0;
    room.scare = 0;
    room.nextScare = 35;
    room.lastTick = this.now();
    room.message = room.creative
      ? 'РЕЖИМ ДОСЛІДЖЕННЯ: Ви вільні досліджувати станцію без загрози.'
      : 'Група прибула. До прориву сигналу 35 сек. Увійдіть до станції та знайдіть першу касету.';
    let index = 0;
    for (const player of room.players.values()) {
      Object.assign(player, {
        x: spawn.x + (index % 2 ? 0.42 : -0.42), z: spawn.z + (index >= 2 ? 0.42 : 0),
        yaw: 0, pitch: 0, down: false, flashlight: true, camera: false, sprinting: false,
        hiding: null, sanity: 100,
        contact: 0, moveBudget: 0.7, moveUpdated: this.now(), invulnerableUntil: 8,
      });
      index++;
    }
    this.publish(room);
  }

  startGame(playerId) {
    const room = this.roomFor(playerId);
    if (!room) return failure('Спочатку створіть кімнату.');
    if (room.hostId !== playerId) return failure('Сеанс починає ведучий.');
    if (room.phase === 'playing') {
      this.publish(room);
      return { ok: true };
    }
    if (room.phase !== 'lobby') return failure('Сеанс уже завершено.');
    this.resetRound(room);
    return { ok: true };
  }

  restartGame(playerId) {
    const room = this.roomFor(playerId);
    if (!room || room.hostId !== playerId) return failure('Сеанс перезапускає ведучий.');
    this.resetRound(room);
    return { ok: true };
  }

  pauseGame(playerId, input = {}) {
    const room = this.roomFor(playerId);
    if (!room || room.phase !== 'playing') return failure('Немає активного сеансу.');
    if (room.players.size !== 1) return failure('Спільний ефір не можна призупинити.');
    if (typeof input.paused !== 'boolean') return failure('Неправильний стан паузи.');
    room.paused = input.paused;
    room.lastTick = this.now();
    this.publish(room);
    return { ok: true };
  }

  movePlayer(playerId, input = {}) {
    const room = this.roomFor(playerId);
    if (!room || room.phase !== 'playing' || room.paused) return false;
    const player = room.players.get(playerId);
    if (player.down || !Number.isFinite(input.x) || !Number.isFinite(input.z) || !Number.isFinite(input.yaw)) return false;
    if (input.pitch !== undefined && !Number.isFinite(input.pitch)) return false;
    if (Math.abs(input.x) > 100 || Math.abs(input.z) > 100 || Math.abs(input.yaw) > 1e6) return false;

    // If player is hiding in a locker
    if (player.hiding) {
      const locker = this.lockers.find(l => l.id === player.hiding);
      if (locker) {
        if (distance(input, locker) > 1.2) {
          player.hiding = null;
        } else {
          // Keep player safely inside locker
          input.x = locker.x;
          input.z = locker.z;
        }
      }
    }

    const currentTime = this.now();
    const dt = Math.max(0, (currentTime - player.moveUpdated) / 1000);
    player.moveBudget = Math.min(2.5, player.moveBudget + dt * 6.5);
    player.moveUpdated = currentTime;
    const delta = distance(player, input);
    player.yaw = Math.atan2(Math.sin(input.yaw), Math.cos(input.yaw));
    player.pitch = Math.max(-1.35, Math.min(1.35, input.pitch ?? 0));
    player.flashlight = input.flashlight !== false && !player.hiding;
    player.camera = input.camera === true && !player.hiding;
    player.sprinting = input.sprinting === true && !player.hiding;

    if (delta > player.moveBudget + 0.001 || !segmentWalkable(player, input)) {
      return false;
    }
    player.moveBudget = Math.max(0, player.moveBudget - delta);
    player.x = input.x;
    player.z = input.z;
    return true;
  }

  radioPing(playerId, input = {}) {
    const room = this.roomFor(playerId);
    if (!room || room.phase !== 'playing' || room.paused) return failure('Ефір не активний.');
    const player = room.players.get(playerId);
    if (!player || player.down || player.hiding) return failure('Немає змоги вийти в ефір.');
    const phraseIndex = Math.max(0, Math.min(3, Number(input.phrase) || 0));
    const phrases = [
      '«Я тут! До мене!»',
      '«ОБЕРЕЖНО! ДИКТОР ПОРУЧ!»',
      '«Касету знайдено! Перевірте наступну зону!»',
      '«ПОТРІБНА ДОПОМОГА! МЕНЕ ЗАБЛОКОВАНО!»',
    ];
    const phrase = phrases[phraseIndex];
    room.radioTransmission = { playerId, name: player.name, x: player.x, z: player.z, phrase, phraseIndex, time: this.now() };

    // Monster hearing mechanic:
    let heard = false;
    if (room.enemy.active) {
      const dist = distance(player, room.enemy);
      if (dist < 18) {
        heard = true;
        room.enemy.stunned = false;
        room.enemy.heardRadio = { x: player.x, z: player.z, until: room.elapsed + 9 };
        room.scare++;
        room.message = `РАЦІЯ [${player.name}]: ${phrase} — ⚠ ДИКТОР ПОЧУВ РАЦІЮ!`;
      } else {
        room.message = `РАЦІЯ [${player.name}]: ${phrase}`;
      }
    } else {
      room.message = `РАЦІЯ [${player.name}]: ${phrase}`;
    }
    this.publish(room);
    return { ok: true, heard };
  }

  interact(playerId, input = {}) {
    const room = this.roomFor(playerId);
    if (!room || room.phase !== 'playing' || room.paused) return failure('Ефір не активний.');
    const player = room.players.get(playerId);
    if (player.down) return failure('Потрібна допомога іншого оператора.');
    if (typeof input.id !== 'string') return failure('Невідомий предмет.');
    const id = input.id;

    // Hiding in lockers
    const lockerTarget = this.lockers.find(l => l.id === id);
    if (lockerTarget) {
      if (player.hiding === lockerTarget.id) {
        // Exit locker to safe exit point
        player.hiding = null;
        player.x = lockerTarget.exitX ?? lockerTarget.x;
        player.z = lockerTarget.exitZ ?? lockerTarget.z;
        player.moveBudget = 0.7;
        player.moveUpdated = this.now();
        room.message = `${player.name} вийшов із шафи.`;
        this.publish(room);
        return { ok: true, hiding: false, exitX: player.x, exitZ: player.z };
      } else {
        if (distance(player, lockerTarget) > 2.2) return failure('Підійдіть ближче до шафи.');
        player.hiding = lockerTarget.id;
        player.x = lockerTarget.x;
        player.z = lockerTarget.z;
        player.moveBudget = 1.0;
        player.moveUpdated = this.now();
        player.camera = false;
        player.flashlight = false;
        player.sprinting = false;
        room.message = `${player.name} сховався в шафу.`;
        this.publish(room);
        return { ok: true, hiding: true, lockerId: lockerTarget.id, x: lockerTarget.x, z: lockerTarget.z };
      }
    }
    if (id === 'exit-locker') {
      if (player.hiding) {
        const locker = this.lockers.find(l => l.id === player.hiding);
        player.hiding = null;
        player.x = locker?.exitX ?? locker?.x ?? player.x;
        player.z = locker?.exitZ ?? locker?.z ?? player.z;
        player.moveBudget = 1.0;
        player.moveUpdated = this.now();
        this.publish(room);
        return { ok: true, hiding: false, exitX: player.x, exitZ: player.z };
      }
      return { ok: true };
    }

    if (id.startsWith('revive:')) {
      const target = room.players.get(id.slice(7));
      if (!target || !target.down) return failure('Оператор не потребує допомоги.');
      if (distance(player, target) > 2 || !segmentWalkable(player, target, 0.05, false)) return failure('Підійдіть ближче до оператора.');
      target.down = false;
      target.contact = 0;
      target.moveBudget = 1.0;
      target.moveUpdated = this.now();
      target.invulnerableUntil = room.elapsed + 4;
      target.sanity = Math.max(target.sanity, 40);
      room.message = `${target.name} знову в ефірі.`;
      this.publish(room);
      return { ok: true };
    }

    const objective = Object.hasOwn(OBJECTIVES, id) ? OBJECTIVES[id] : undefined;
    if (!objective) return failure('Невідомий предмет.');
    if (distance(player, objective) > 2.5 || !segmentWalkable(player, objective, 0.04, false)) return failure('Підійдіть ближче.');
    if (id.startsWith('fuse-')) {
      if (room.fuses.has(id)) return { ok: true };
      room.fuses.add(id);
      room.scare++;
      room.message = `ЗНАЙДЕНО ЗАПОБІЖНИК 380В (${room.fuses.size}/2). Встановіть його в електрощит генератора.`;
    } else if (id.startsWith('tape-')) {
      if (room.tapes.has(id)) return { ok: true };
      room.tapes.add(id);
      room.scare++;
      // Taking a tape instantly breaks the calm and accelerates spawn timer
      if (!room.creative) {
        if (room.spawnCountdown > 3) room.spawnCountdown = 3;
        room.enemy.active = true;
      }
      room.message = room.tapes.size === 1
        ? 'КАСЕТА 01: «Якщо він дивиться — знімай. Камера тримає його тут».'
        : room.tapes.size === 2
          ? 'КАСЕТА 02: «Голос у радіо — не твій напарник. Він чує ваші рації».'
          : 'КАСЕТА 03: «Знайдіть 2 запобіжники. Увімкніть живлення. Передайте запис».';
    } else if (id === 'power') {
      if (room.power) return { ok: true };
      if (room.fuses.size < 2) return failure(`Потрібні 2 запобіжники 380В (знайдено: ${room.fuses.size}/2). Перевірте вентвузол та серверну.`);
      room.power = true;
      room.scare++;
      room.message = 'Живлення відновлено! Обидва запобіжники 380В встановлено. Передавач готовий!';
    } else if (id === 'broadcast') {
      if (room.tapes.size < 3) return failure('Для передачі потрібні всі три касети.');
      if (!room.power) return failure('Спочатку відновіть живлення в генераторній.');
      if (room.broadcast) return { ok: true };
      room.broadcast = true;
      room.scare += 2;
      room.enemy.active = !room.creative;
      room.enemy.x = 0;
      room.enemy.z = -22;
      player.moveBudget = 2.0;
      player.moveUpdated = this.now();
      player.invulnerableUntil = room.elapsed + 5;
      room.message = 'ЗАПИС У ЕФІРІ. ДИКТОР ЗБОЖЕВІВ — ПОВЕРТАЙТЕСЯ ДО ФУРГОНА У ДВОРІ!';
    } else if (id === 'exit') {
      if (!room.broadcast) return failure('Завершіть роботу на станції: знайдіть записи та передайте сигнал перед від’їздом.');
      room.phase = 'won';
      room.enemy.active = false;
      room.message = 'Ви вийшли. Але на записі чути на один голос більше.';
    }
    this.publish(room);
    return { ok: true };
  }

  checkDefeat(room) {
    if (room.phase === 'playing' && room.players.size && [...room.players.values()].every((p) => p.down)) {
      room.phase = 'lost';
      room.paused = false;
      room.message = 'Сигнал утрачено. Тепер ваші голоси належать станції.';
    }
  }

  tick() {
    const currentTime = this.now();
    for (const room of this.rooms.values()) {
      const elapsed = Math.max(0, (currentTime - room.lastTick) / 1000);
      room.lastTick = currentTime;
      if (room.phase !== 'playing' || room.paused) continue;
      room.elapsed += elapsed;

      // Spawn timer countdown
      if (!room.creative && room.spawnCountdown > 0) {
        room.spawnCountdown = Math.max(0, room.spawnCountdown - elapsed);
        if (room.spawnCountdown === 0 && !room.enemy.active) {
          room.enemy.active = true;
          room.scare++;
          room.message = 'СИГНАЛ ПРОРВАНО. «Диктор» зайшов у коридор. Ховайтеся в шафах!';
        }
      }

      if (room.elapsed >= room.nextScare) {
        room.nextScare += 60;
        room.scare++;
        room.message = room.tapes.size ? 'У рації чути ваше дихання. І ще чиєсь.' : 'Станцію мали закрити опівночі. Чому лампа запису горить?';
      }

      const dt = Math.min(elapsed, 0.25);

      // Update sanity for all living players
      for (const player of room.players.values()) {
        if (player.down) continue;
        if (room.creative) {
          player.sanity = 100;
          continue;
        }
        if (player.hiding) {
          player.sanity = Math.min(100, player.sanity + dt * 2.5);
          continue;
        }
        let drain = 0;
        let recover = 0;
        if (!player.flashlight) drain += 1.4;
        if (room.enemy.active) {
          const d = distance(player, room.enemy);
          if (d < 5) drain += 5.0;
          else if (d < 11) drain += 2.0;
        }
        if (player.camera && room.enemy.stunned) drain += 2.5;
        if (player.sprinting) drain += 0.9;

        const hasTeammate = [...room.players.values()].some(p => p.id !== player.id && !p.down && distance(player, p) < 4);
        if (hasTeammate) recover += 2.2;
        if (player.flashlight && !player.sprinting) recover += 0.6;

        player.sanity = Math.max(0, Math.min(100, player.sanity - drain * dt + recover * dt));
      }

      if (room.enemy.active) this.updateEnemy(room, dt);
      this.publish(room);
    }
  }

  updateEnemy(room, dt) {
    const enemy = room.enemy;
    const living = [...room.players.values()].filter((player) => !player.down);
    if (!living.length) return this.checkDefeat(room);

    // Stun check: only non-hiding active players can stun
    const activeStunners = living.filter(p => !p.hiding);
    enemy.stunned = activeStunners.some((player) => {
      if (!player.camera || !player.flashlight) return false;
      const d = distance(player, enemy);
      if (d > 9 || d < 0.02) return false;
      const horizontalAim = ((enemy.x - player.x) * -Math.sin(player.yaw) + (enemy.z - player.z) * -Math.cos(player.yaw));
      const heightDifference = -0.3;
      const dot = (horizontalAim * Math.cos(player.pitch) + heightDifference * Math.sin(player.pitch)) / Math.hypot(d, heightDifference);
      return dot > 0.9 && segmentWalkable(player, enemy, 0.05);
    });

    // Target selection:
    const visibleTargets = living.filter(p => !p.hiding);
    let target = null;
    if (enemy.heardRadio && room.elapsed < enemy.heardRadio.until) {
      target = enemy.heardRadio;
    } else if (visibleTargets.length > 0) {
      target = visibleTargets.reduce((nearest, player) => distance(player, enemy) < distance(nearest, enemy) ? player : nearest);
    } else {
      // All living players are currently in lockers! Enemy patrols central points
      const patrolNodes = [{ x: 0, z: -2 }, { x: 0, z: -17 }, { x: 0, z: -32 }, { x: 0, z: -47 }];
      const idx = Math.floor(room.elapsed / 6) % patrolNodes.length;
      target = patrolNodes[idx];
    }

    if (!enemy.stunned && target) {
      const waypoint = nextWaypoint(enemy, target);
      const d = distance(enemy, waypoint);
      // Speed: 3.2 m/s after broadcast (bloody nightmare sprint!), or 0.85 + tapes * 0.28
      const speed = room.broadcast ? 3.2 : 0.85 + room.tapes.size * 0.28;
      const step = Math.min(d, speed * dt);
      if (d > 0.01) {
        const next = { x: enemy.x + (waypoint.x - enemy.x) / d * step, z: enemy.z + (waypoint.z - enemy.z) / d * step };
        if (segmentWalkable(enemy, next)) Object.assign(enemy, next);
      }
    }

    // Damage check: only affects non-hiding players! Disabled in creative mode
    if (room.creative) return;
    for (const player of visibleTargets) {
      if (!enemy.stunned && distance(player, enemy) < 1.1 && room.elapsed >= (player.invulnerableUntil || 0) && segmentWalkable(player, enemy, 0.05)) {
        player.contact += dt;
        if (player.contact >= 2) {
          player.down = true;
          player.camera = false;
          player.flashlight = false;
          player.sprinting = false;
          room.scare++;
          room.message = `${player.name} зник із кадру. Підійдіть і натисніть E, щоб допомогти.`;
        }
      } else {
        player.contact = 0;
      }
    }
    this.checkDefeat(room);
  }
}
