# Handoff Report: Explorer 2 (Quest, Stage Flow, Items, Minigame & Map)

## 1. Observation

### 1.1 Existing Interaction & Inventory Systems
- **Interaction Selection (`src/main.js:630–684`)**:
  - `chooseInteraction()` computes forward ray vector: `forwardX = -Math.sin(yaw)`, `forwardZ = -Math.cos(yaw)`.
  - Proximity check is set to `dist = 2.45` meters, with dot product requirement: `dot > 0.4 || d < 0.9`.
  - Currently evaluates:
    1. Lockers (`activeLockers`, `dist < 2.0`)
    2. Collectibles & objectives from `objects` (`shared/world-data.js:24–33`): `tape-1`, `tape-2`, `tape-3`, `fuse-1`, `fuse-2`, `power`, `broadcast`, `exit`.
    3. Downed teammates for revive (`Math.hypot(p.x - localPos.x, p.z - localPos.z) < 2.2`).
- **Interaction Execution (`src/main.js:686–725`)**:
  - Bound to `KeyE` (`src/main.js:766: if(e.code==='KeyE')interact();`).
  - Calls `const res = await emit('game:interact', { id: target.id });`.
  - Updates local player position for lockers, plays audio feedback (`audio.play('locker')`, `audio.play('pickup')`, `audio.play('power')`), and displays toast notifications (`toast(...)`).
- **Server-Side Validation (`server/game.js:443–557`)**:
  - `interact(playerId, input)` validates:
    - Player alive (`!player.down`).
    - Distance to objective: `distance(player, objective) <= 2.5`.
    - Line of sight / walkability: `segmentWalkable(player, objective, 0.04, false)`.
  - Authoritative inventory state currently only tracks:
    ```javascript
    tapes: new Set(), // tape-1, tape-2, tape-3
    fuses: new Set(), // fuse-1, fuse-2
    power: false,
    broadcast: false
    ```
- **HUD Inventory Markup (`src/interface.js:23`)**:
  ```html
  <div id="inventory" class="inventory">
    <span class="inventory-group" title="Касети"><i class="tape-icon"></i><i class="tape-icon"></i><i class="tape-icon"></i></span>
    <span class="inventory-group fuses-group" title="Запобіжники"><i class="fuse-icon"></i><i class="fuse-icon"></i></span>
  </div>
  ```

### 1.2 Door Implementation & Collision Geometry
- **3D Door Meshes (`src/world.js:1085–1093`)**:
  ```javascript
  // Doorway portals at z = -1.5, -16.5, -31.5, -46.5
  for (const z of [-1.5, -16.5, -31.5, -46.5]) {
    box(root, cx, 3.13, z, 0.24, 0.74, 2.4, wall);
    for (const edge of [-1, 1]) box(root, cx, 1.4, z + edge * 1.19, 0.4, 2.8, 0.16, trim);
    box(root, cx, 2.79, z, 0.4, 0.14, 2.5, trim);
    const door = box(root, cx + side * 0.92, 1.35, z - 1.03, 1.7, 2.65, 0.09, mat('wood', 0x949a7b));
    door.rotation.y = side * 0.06;
    box(root, cx + side * 0.3, 1.2, z - 0.96, 0.12, 0.06, 0.09, rusty);
  }
  ```
  Doors are currently static aesthetic meshes swung slightly open. They are not interactive objects and do not possess unique IDs or dynamic transform states.
- **Walkability / Doorways (`shared/world-data.js:125–136`)**:
  ```javascript
  // Corridor doorways (doors at z = -1.5, -16.5, -31.5, -46.5)
  const corridorDoors = [-2.5, 2.5].some(dx =>
    Math.abs(x - dx) < radius + 0.35 && [-1.5, -16.5, -31.5, -46.5].some(dz => Math.abs(z - dz) < 1.3 - radius)
  );
  // Connecting loop doorways at partition wall z = -24
  const loopDoors = [-10.5, 10.5].some(tx =>
    Math.abs(x - tx) < 1.3 - radius && Math.abs(z - (-24)) < radius + 0.35
  );
  ```
  All doorways are permanently wide open to collision testing.
- **Dynamic Obstacle & Navigation System (`server/game.js:107–121`, `shared/world-data.js:94–105`)**:
  - `activeObstacles` stores 2D AABB bounding boxes (`{ id, minX, maxX, minZ, maxZ }`).
  - Monster navigation graph `navigationGraph()` computes visibility graph between `WAYPOINTS` and recalculates whenever `signature = JSON.stringify(activeObstacles)` changes.
  - `segmentWalkable(a, b)` automatically splits paths on boundary cuts and verifies `!isObstacle(...)`.

### 1.3 Map Screen System (`KeyM`)
- **Map Modal (`src/main.js:425–458`)**:
  - Triggered by `KeyM` or clicking Plan button.
  - Calls `openPanel('map')` and injects static SVG (`viewBox="0 -80 540 700"`).
  - Displays room name via `roomAt(localPos.x, localPos.z)`, static room outlines, and fixed text labels for `① КАСЕТА`, `⚡ ЩИТОК 380В`, etc.
  - **Does NOT** show real-time player position, teammate positions, door lock indicators, or subsystem statuses.

### 1.4 Pre-existing Puzzle & Minigame CSS
- **Styling (`src/style.css:7`)**:
  ```css
  .minigame{text-align:center}.timing-track{height:45px;position:relative;background:#202d20;border:1px solid #a8b79077;margin:28px 0}.timing-zone{position:absolute;top:0;bottom:0;left:62%;width:17%;background:#c9aa62a0}.timing-needle{position:absolute;top:-4px;bottom:-4px;width:3px;background:#e6ebdb;left:0;box-shadow:0 0 10px #dfe8c866}.timing-track:before{content:'СИНХРОНІЗАЦІЯ ЧАСТОТИ';position:absolute;top:-18px;left:0;font-size:8px;letter-spacing:1px;color:#728266}
  ```
  Pre-styled scaffolding already exists in CSS for a frequency synchronization minigame.
- **Arthur Cole Clue (`src/main.js:384–387`, `DESIGN.md:9–11`)**:
  - Senior sound engineer Arthur Cole documented the anomaly on 3 cassettes.
  - Broadcast frequency is 96.4 MHz.

---

## 2. Logic Chain

### 2.1 Door Mechanics & Collision Integration
1. **From Observation 1.2**, doorways at `x = ±2.5` (`z = -1.5, -16.5, -31.5, -46.5`) and loop doorways at `z = -24` (`x = ±10.5`) allow passage whenever `isInsideWorld` is true.
2. In `isWalkable`, `!isObstacle(x, z, radius) && isInsideWorld(x, z, radius)` determines walkability.
3. Therefore, adding a door bounding box obstacle into collision checking for closed doors will immediately block both player movement (`movePlayer` checks `segmentWalkable`) and creature pathfinding (`navigationGraph` checks `segmentWalkable`).
4. Removing the obstacle or marking the door `open: true` re-enables passage without requiring changes to room perimeter boundaries.
5. In multi-room co-op, door states must be stored per room (`room.doors`) rather than mutating a single global array, preventing state bleed between distinct game sessions.

### 2.2 4-Stage Quest Flow (Extending Game Time to 20–30 Minutes)
Based on R2 from `ORIGINAL_REQUEST.md` and Acceptance Criteria line 62:
`Ключ-картка охорони → Картка інженера → Паливо + Вентиль + Запобіжники → Запуск живлення → Калібрування сигналу → Фінальний прорив до фургона.`

```
   [START: Arrival at Courtyard Van (z=19)]
                     │
                     ▼
  ┌────────────────────────────────────────────────────────┐
  │ STAGE 1: Recon & Access (0–8 min)                      │
  │ 1. Search Courtyard CP / Guard booth (x=16, z=10)      │
  │    ──> Pick up [keycard-guard]                         │
  │ 2. Enter Station through open entrance (z=6)           │
  │    ──> Magnetic doors are locked (red card readers)    │
  │ 3. Unlock Archive door with Guard Keycard              │
  │    ──> Retrieve [tape-1] (Cassette 01)                 │
  │    ──> Read [note-power] (Power Circuit Lore Note)     │
  │    ──> Pick up [keycard-engineer] (Engineer Keycard)   │
  └────────────────────────┬───────────────────────────────┘
                           │
                           ▼
  ┌────────────────────────────────────────────────────────┐
  │ STAGE 2: Power Unit Start (8–18 min)                   │
  │ 1. Search Tech Storage / Yard Shed for [canister-fuel] │
  │    ──> Deliver fuel to Generator tank (x=-10.5, z=-17) │
  │ 2. Unlock & enter Vent Room (x=-8, z=-32)              │
  │    ──> Open emergency pressure [valve-vent] (hiss sound)│
  │    ──> Pick up [fuse-1] (380V Fuse #1)                 │
  │ 3. Unlock Server Room with Engineer Keycard (x=10.5)   │
  │    ──> Pick up [fuse-2] (380V Fuse #2)                 │
  │ 4. Install fuses into Generator 380V Panel             │
  │ 5. Pull Main Knife Switch:                             │
  │    ──> Roar of diesel engine + amber warning strobes   │
  │    ──> Station lights energize, monitors turn on       │
  │    ──> Creature wakes up & initiates corridor patrols! │
  └────────────────────────┬───────────────────────────────┘
                           │
                           ▼
  ┌────────────────────────────────────────────────────────┐
  │ STAGE 3: Signal Sync & Bureau Call (18–25 min)         │
  │ 1. Infiltrate Studio 04 (x=8, z=-16.5)                 │
  │    ──> Retrieve [tape-2] (Cassette 02)                 │
  │ 2. Infiltrate Reserve Tech Storage (x=-8, z=-50.5)     │
  │    ──> Retrieve [tape-3] (Cassette 03)                 │
  │ 3. Apparatus Room (x=10, z=-2) Broadcast Console:      │
  │    ──> Open 96.4 MHz Calibration Minigame (Faders/Dial)│
  │    ──> Match frequency to Arthur Cole's dossier        │
  │    ──> Signal locked!                                  │
  │ 4. Press Master Broadcast Trigger:                     │
  │    ──> Transmit tapes to BUREAU-9 satellite            │
  └────────────────────────┬───────────────────────────────┘
                           │
                           ▼
  ┌────────────────────────────────────────────────────────┐
  │ STAGE 4: Evacuation (25–30 min)                        │
  │ 1. Alarm siren wails, all corridor lights go BLOOD-RED │
  │ 2. Emergency signal unlocks Yard Security Gate         │
  │ 3. Monster transforms into Nightmare Form (speed 3.2m/s)│
  │ 4. High-stakes sprint down 60m corridor to courtyard   │
  │ 5. Reach BUREAU-9 van at [exit] (x=-5.1, z=23)         │
  │ 6. Extraction complete! Win screen & mission debrief.  │
  └────────────────────────────────────────────────────────┘
```

### 2.3 Map Screen Upgrades (R4 Requirement)
1. **From Observation 1.3**, the current map uses a static SVG.
2. In `showMap()`, we can transform the player's 3D coordinates `(localPos.x, localPos.z)` into SVG coordinates:
   - World $X \in [-12, 12] \implies \text{SVG } x \in [30, 510]$.
   - World $Z \in [6, -54] \implies \text{SVG } y \in [30, 590]$ (corridor), and $Z \in [6, 36] \implies \text{SVG } y \in [15, -70]$ (courtyard).
3. We can overlay:
   - Live player blip (gold circle with heading arrow based on `yaw`).
   - Teammate blips (colored according to player class).
   - Door icons colored by status:
     - 🔴 Red line / 🔒 locked icon with key requirements (`[ОХОРОНА]`, `[ІНЖЕНЕР]`).
     - 🟢 Green dashed opening / 🔓 unlocked icon.
   - Live subsystem status dashboard:
     - Magnetic locks: [Active / Guard Keycard Used]
     - Engineer access: [Locked / Engineer Keycard Used]
     - Generator fuel: [Empty / 100% Fueled]
     - Vent pressure valve: [Overpressure / Released]
     - 380V electrical fuses: [0/2, 1/2, 2/2 Installed]
     - Generator state: [Offline / Running]
     - Frequency calibration: [Desynced / 96.4 MHz Locked]
     - Evacuation gate: [Locked / Emergency Open]

---

## 3. Caveats

1. **Procedural 3D Models vs Custom Map Props**: Custom map props (`shared/custom-map.json`) override `DEFAULT_PROPS` when present. New interactive items (card readers, valve, canister, notes) should be added to base world generation (`src/world.js`) and registered in `dynamic` set so they are not merged into static scenery batches (`mergeGeometries`).
2. **Door Open/Close State**: Having doors swing open and stay open once unlocked provides smooth gameplay and prevents players from being trapped by door collisions during high-speed monster pursuits.
3. **Loop Doorways**: The side loop tunnels at $z = -24$ connect Generator with Vent, and Studio 04 with Server. To avoid sequence breaking, the loop doorways should feature locked security grates requiring the Engineer Keycard or remain locked until Stage 2 power restoration.

---

## 4. Conclusion

### 4.1 Authoritative State Representation Schema

#### Server Room State (`server/game.js`):
```javascript
{
  code: string,
  hostId: string,
  phase: 'lobby' | 'playing' | 'won' | 'lost',
  solo: boolean,
  creative: boolean,
  paused: boolean,
  stage: 1 | 2 | 3 | 4,

  // Shared Group Inventory & Quest Progression
  keycards: Set<'keycard-guard' | 'keycard-engineer'>,
  fuelCanister: boolean,      // In group inventory
  fuelAdded: boolean,         // Poured into generator tank
  ventValveOpen: boolean,     // Pressure valve released in vent room
  fuses: Set<'fuse-1' | 'fuse-2'>,
  power: boolean,             // Generator energized
  tapes: Set<'tape-1' | 'tape-2' | 'tape-3'>,
  calibrated: boolean,        // 96.4 MHz locked
  broadcast: boolean,         // Final broadcast initiated
  notesRead: Set<string>,     // 'note-power', 'note-dossier'

  // Authoritative Door States & Collisions
  doors: {
    'door-archive':    { locked: true, requiredKey: 'keycard-guard',    open: false, x: -2.5, z: -1.5 },
    'door-apparatus':  { locked: true, requiredKey: 'keycard-guard',    open: false, x: 2.5,  z: -1.5 },
    'door-generator':  { locked: true, requiredKey: 'keycard-engineer', open: false, x: -2.5, z: -16.5 },
    'door-studio':     { locked: true, requiredKey: 'keycard-guard',    open: false, x: 2.5,  z: -16.5 },
    'door-vent':       { locked: true, requiredKey: 'keycard-guard',    open: false, x: -2.5, z: -31.5 },
    'door-server':     { locked: true, requiredKey: 'keycard-engineer', open: false, x: 2.5,  z: -31.5 },
    'door-tech':       { locked: true, requiredKey: 'keycard-guard',    open: false, x: -2.5, z: -46.5 },
    'door-dispatcher': { locked: true, requiredKey: 'keycard-guard',    open: false, x: 2.5,  z: -46.5 },
    'gate-loop-left':  { locked: true, requiredKey: 'keycard-engineer', open: false, x: -10.5, z: -24 },
    'gate-loop-right': { locked: true, requiredKey: 'keycard-engineer', open: false, x: 10.5,  z: -24 },
    'gate-yard':       { locked: true, requiredKey: 'evacuation',       open: false, x: 0,    z: 6 },
  },

  // Monster & Simulation
  spawnCountdown: number,
  enemy: { x: number, z: number, active: boolean, stunned: boolean, heardRadio: object | null },
  elapsed: number,
  scare: number,
  message: string,
}
```

#### Network Wire State (`state(room)` broadcast to clients):
Serializes `keycards: [...room.keycards]`, `doors: room.doors`, `stage: room.stage`, `fuelCanister: room.fuelCanister`, `fuelAdded: room.fuelAdded`, `ventValveOpen: room.ventValveOpen`, `calibrated: room.calibrated`, alongside existing fields.

### 4.2 New Interactive Objects for `objects` (`shared/world-data.js`):
1. `{ id: 'keycard-guard', x: 16.5, y: 1.1, z: 10.0, type: 'keycard', label: 'Картка вахтера (Рівень 1)' }` (Courtyard CP)
2. `{ id: 'keycard-engineer', x: -7.5, y: 1.1, z: -3.2, type: 'keycard', label: 'Картка інженера (Рівень 2)' }` (Archive Desk)
3. `{ id: 'note-power', x: -8.0, y: 1.1, z: -2.2, type: 'note', label: 'Записка чергового інженера: Схема живлення 380В' }`
4. `{ id: 'canister-fuel', x: -8.0, y: 0.5, z: -48.0, type: 'fuel', label: 'Каністра дизельного пального' }` (Tech Storage)
5. `{ id: 'valve-vent', x: -7.25, y: 1.5, z: -32.0, type: 'valve', label: 'Аварійний вентиль тиску вентвузла' }` (Vent Room)
6. `{ id: 'minigame-radio', x: 10.0, y: 1.2, z: -2.0, type: 'console', label: 'Калібрування частоти передавача (96.4 MHz)' }` (Apparatus Room)

### 4.3 Calibration Minigame Specification
- **Interface**: Modal overlay triggered from Apparatus console.
- **Sliders**:
  - `Coarse Tuning`: 80.0 to 120.0 MHz (step 0.5)
  - `Fine Tuning`: -1.0 to +1.0 MHz (step 0.05)
- **Feedback**:
  - Audio: Dynamic oscillator static noise that cleans up to a harmonic tone near 96.4 MHz.
  - Visual: Oscilloscope canvas drawing noisy sine wave, collapsing to pure green sine wave at 96.4 MHz.
  - Lock Button: Becomes enabled when $|f - 96.4| \le 0.05$. Clicking sends `socket.emit('game:interact', { id: 'minigame-radio', frequency: 96.4 })`.

---

## 5. Verification Method

### 5.1 Automated Unit & Build Verification
1. Run existing unit test suite:
   ```powershell
   npm test
   ```
   **Expected**: 19 of 19 tests pass with code 0 (`node --test server/*.test.js`).
2. Run client build:
   ```powershell
   npm run build
   ```
   **Expected**: Vite build succeeds with 0 errors (`vite build`).

### 5.2 Specific Regression Checks for Implementation
- `server/game.test.js:77-117` (`objective gates require proximity, all tapes, restored power, and a broadcast`):
  Ensure that adding door gates and multi-stage steps either maintains backward compatibility or is updated with the new progression chain.
- `server/game.test.js:119-134` (`the creature navigates around room walls via open doorways`):
  Verify monster pathfinding when doors are open vs closed.

### 5.3 Invalidation Conditions
- If locked doors block player movement without providing prompt to use keycard.
- If monster can phase through locked magnetic doors before they are unlocked.
- If multiplayer co-op desynchronizes group keycards or generator fuel state.
- If `npm test` fails due to unhandled object IDs in authoritative interaction handler.
