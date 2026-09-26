# Explorer 1 Survey Report: World Geometry, Level Design, Props & Collisions

## 1. Observation

### 1.1 Project Structure & Build/Test Toolchain
- **Repository Root**: `C:\Users\noona\Documents\ChatGPT\Test`
- **Key Modules & Directories**:
  - `shared/world-data.js` (6,685 bytes): Core world spatial data, room boundaries, collision primitives, and query functions.
  - `shared/props.js` (6,056 bytes): `DEFAULT_PROPS` array (35 active station props) and `PROP_CATALOG` (9 prop types).
  - `shared/custom-map.json` (8,512 bytes): Server-persisted JSON representation of props authored via `builder.html`.
  - `shared/classes.js` (3,822 bytes): 4 character classes (Victor/Engineer, Vivian/Operator, Edgar/Journalist, Hazel/Dispatcher) with perks and stats.
  - `src/world.js` (66,296 bytes): Procedural PS1 materials, station structural geometry, dynamic prop mesh generation (`spawnPropMesh`), interactive objects, and material-based geometry merging (`BufferGeometryUtils.mergeGeometries`).
  - `src/exterior.js` (23,780 bytes): Courtyard ground, perimeter fence, lighting, foliage, crew van model (`createVan`), service shed, and static batching.
  - `src/main.js` (57,910 bytes): Client game loop (`tick`), pointer lock input, movement sliding physics (`isWalkable`), networking, HUD, and SVG blueprint map (`showMap`).
  - `src/builder.js` (20,061 bytes) & `builder.html` (10,131 bytes): 3D top-down level editor with raycast picking, 0.25m grid snapping, inspector, and `/api/map` API sync.
  - `src/models-preview.js` (14,319 bytes) & `models-preview.html` (10,022 bytes): Interactive 3D gallery of 14 character, vehicle, and prop models.
  - `server/game.js` (30,143 bytes): Authoritative game state, `collisionBoundaries`, `segmentWalkable` intersection tests, visibility graph waypoints, and monster A* pathfinding (`nextWaypoint`).
  - `server/index.js` (7,803 bytes): Express & Socket.IO server, `/api/map` endpoints (`GET`, `POST`, `POST /reset`).
- **Tool Commands & Verification**:
  - `npm test` executes `node --test server/*.test.js`:
    - Result: **19 passed, 0 failed** in 31.8s. Includes `server/game.test.js`, `server/interface.test.js`, and `server/world-regression.test.js`.
  - `npm run build` executes `vite build`:
    - Result: Exit code 0, 54 modules transformed, bundles `dist/index.html`, `dist/builder.html`, `dist/models-preview.html`.

---

### 1.2 World Data & Geometry Coordinates

#### A. Master Coordinate Space
The coordinate system is a unified 3D Cartesian space where `Y=0` is the ground/floor plane, `+X` points East, `-X` points West, `+Z` points South (towards the outdoor yard and front gates), and `-Z` points North (deep into the station).

#### B. Room Geometry (`shared/world-data.js:11-22`)
```javascript
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
```
- **Total Station Footprint**: Width = 24.0m (`X: -12` to `+12`), Length = 60.0m (`Z: -54` to `+6`).
- **Master Floor & Ceiling Slabs (`src/world.js:1041-1042`)**:
  - Floor: `box(root, 0, -0.15, -24, 25.5, 0.3, 62, floor);` (X: -12.75 to 12.75, Z: -55 to 7)
  - Ceiling: `box(root, 0, 3.58, -24, 25.5, 0.16, 62, ceiling);`
  - Interior wall height is 3.5m.
- **Corridor**:
  - Width: 5.0m (`X: -2.5` to `+2.5`). Length: 60.0m (`Z: -54` to `+6`).
  - Longitudinal corridor walls at `X = -2.5` and `X = +2.5`.
- **Rooms Breakdown**:
  - West Wing (`X: -12` to `-2.5`, Width = 9.5m):
    - *Архів* (Sector 1): `Z: -9` to `6` (Center approx `x: -7.25, z: -1.5`)
    - *Генераторна* (Sector 1): `Z: -24` to `-9` (Center approx `x: -7.25, z: -16.5`)
    - *Вентвузол* (Sector 2): `Z: -39` to `-24` (Center approx `x: -7.25, z: -31.5`)
    - *Техсклад* (Sector 2): `Z: -54` to `-39` (Center approx `x: -7.25, z: -46.5`)
  - East Wing (`X: 2.5` to `12`, Width = 9.5m):
    - *Апаратна* (Sector 1): `Z: -9` to `6` (Center approx `x: 7.25, z: -1.5`)
    - *Студія 04* (Sector 1): `Z: -24` to `-9` (Center approx `x: 7.25, z: -16.5`)
    - *Серверна* (Sector 2): `Z: -39` to `-24` (Center approx `x: 7.25, z: -31.5`)
    - *Диспетчерська* (Sector 2): `Z: -54` to `-39` (Center approx `x: 7.25, z: -46.5`)

#### C. Doorways and Portals (`shared/world-data.js:125-136`)
1. **Corridor Portals**:
   - 4 doorway pairs at `Z = -1.5, -16.5, -31.5, -46.5` connecting corridor (`X = -2.5` and `X = 2.5`) to each room.
   - Portal width along Z: 2.38m (checked via `Math.abs(z - dz) < 1.3 - radius`).
2. **Loop Portals (Lateral Bypasses)**:
   - Partition wall at `Z = -24` contains bypass loop doors at `X = -10.5` (connecting Generator to Vent) and `X = +10.5` (connecting Studio 04 to Server Room).
   - Doorway width along X: 2.6m (`Math.abs(x - tx) < 1.3 - radius`).
3. **Station Main Entrance**:
   - Located at `Z = 6`, centered at `X = 0`.
   - Width: 3.2m (`Math.abs(x) < 1.6 - radius`), height: 2.7m. Connects corridor directly to the outdoor yard without loading transitions.

#### D. Outdoor Courtyard (`shared/world-data.js:2` & `src/exterior.js:150-210`)
- **Courtyard Box**: `minX: -22, maxX: 22, minZ: 6, maxZ: 36` (Width = 44m, Depth = 30m).
- **Asphalt Ground**: `box(root, 0, -0.13, 21, 44, 0.25, 30, asphalt);`
- **Spawn Point**: `x: -5, z: 19` (in front of the van).
- **Extraction / Exit Point**: `x: -5.1, y: 1.2, z: 23` (van sliding door).
- **Exterior Obstacles (`shared/world-data.js:4-7`)**:
  - `crew-van`: `minX: -8.35, maxX: -5.65, minZ: 20.35, maxZ: 25.65` (Van model at `x: -7, z: 23`).
  - `service-shed`: `minX: 15.8, maxX: 20.8, minZ: 8.3, maxZ: 13.7` (380V shed at `x: 18.3, z: 11`).
- **Perimeter Fencing (`src/exterior.js:209-210`)**:
  - West fence: `(-22, 6)` to `(-22, 36)`.
  - East fence: `(22, 6)` to `(22, 36)`.
  - South fence with vehicle gate: `(-22, 36)` to `(-10.4, 36)` and `(-3.6, 36)` to `(22, 36)`.
  - Vehicle gate opening is at `X: -10.4` to `-3.6` at `Z: 36`.

---

### 1.3 3D Model Generation, Materials & PS1 Aesthetic

#### A. Procedural Canvas Textures (`src/world.js:17-47`, `src/exterior.js:14-36`)
All textures are procedurally baked onto 128×128 HTML5 canvases at initialization (zero downloaded images):
- **Palette**:
  - `plaster: '#706f57'`
  - `floor: '#424a41'` (with 32px tile grid lines and grime spatters)
  - `wood: '#503e29'` (with pseudo-bezier grain and panel gaps)
  - `metal: '#3d5148'` (with corner rivets and steel noise)
  - `rust: '#694032'` (corrosion speckles)
  - `fabric: '#696150'` (woven texture)
  - `blood: '#4d1210'` (crimson radial splatters)
  - `server: '#222926'`
- **Texture Sampling**:
  - `magFilter = THREE.NearestFilter`
  - `minFilter = THREE.NearestMipmapNearestFilter`
  - `wrapS = wrapT = THREE.RepeatWrapping`
  - This creates the authentic pixelated, non-filtered PS1/early PC hardware look.
- **Materials**:
  - `mat(kind, color, repeat)` generates cached `MeshStandardMaterial` with `flatShading: true, roughness: 0.94`.
  - `plain(color, roughness)` generates untextured flat-shaded materials.
  - `glow(color, intensity)` generates emissive materials (`emissiveIntensity: intensity`).
  - `label(parent, lines, ...)` draws 2D canvas text labels onto `MeshBasicMaterial` planes with scanline noise.

#### B. Static Geometry Merging (`src/world.js:1308-1330`)
```javascript
const dynamic = new Set([
  ...tapeObjects, ...fuseObjects, powerLever, powerLamp, broadcastBulb, exitLamp,
  ...fans, ...monitors.map(m => m.screen),
  ...lockers.map(l => interactables.get(l.id)),
]);
const batches = new Map(); root.updateMatrixWorld(true);
root.traverse(object => {
  if (!object.isMesh || Array.isArray(object.material)) return;
  let p = object; while (p && p !== root) { if (dynamic.has(p)) return; p = p.parent; }
  const key = object.material.uuid; if (!batches.has(key)) batches.set(key, []); batches.get(key).push(object);
});
for (const objects of batches.values()) {
  if (objects.length < 3) continue;
  const geometries = objects.map(o => o.geometry.clone().applyMatrix4(o.matrixWorld));
  const geometry = mergeGeometries(geometries, false);
  if (geometry) {
    const combined = new THREE.Mesh(geometry, objects[0].material);
    combined.receiveShadow = true; root.add(combined);
    for (const o of objects) { o.removeFromParent(); o.geometry.dispose(); }
  }
}
```
- This reduces hundreds of individual mesh nodes into ~8-12 batch draw calls.
- Dynamic objects (tapes, fuses, levers, interactive lockers, screens) are explicitly excluded from merging so they retain independent visibility and animation.

#### C. Existing Prop Implementations (`src/world.js:86-175, 1015-1032`)
The codebase currently implements only 9 prop types in `PROP_CATALOG`:
1. `table`: Wooden top with 4 metal tubular legs and cross brace.
2. `chair`: Fabric seat and backrest with thin metal legs.
3. `locker`: Metal locker with ventilation slits, door handle, and number badge; interactive hiding spot.
4. `shelf`: Rust-coated frame with 5 shelves holding VHS tape stacks.
5. `serverRack`: 2.5m tall metal rack with 5 server bays and alternating green/amber blinking LEDs.
6. `crate`: Wooden crate with rusted corner angle brackets.
7. `barrel`: Metal cylinder with 3 rusted rib rings.
8. `camera`: Heavy TK-04 TV camera with tripod, body, lens, viewfinder, and tally light.
9. `crt`: Analog CRT monitor with 4:3 screen displaying canvas text.

---

### 1.4 Collision, Physics & Spatial Queries

#### A. AABB Extrusion & Obstacle Mapping (`shared/world-data.js:46-92`)
Props in `props.js` are converted to 2D axis-aligned bounding boxes (AABBs) via `propToObstacle(p)`:
- Rotation is accounted for using half-extents:
  ```javascript
  const rot = p.rotation || 0;
  const cos = Math.abs(Math.cos(rot));
  const sin = Math.abs(Math.sin(rot));
  const hx = w * cos + d * sin;
  const hz = w * sin + d * cos;
  ```
- Resulting obstacle structure: `{ id, type, minX, maxX, minZ, maxZ }`.

#### B. Collision Test Functions (`shared/world-data.js:112-143`)
1. `isObstacle(x, z, radius = 0.22)`:
   - Tests if point `(x, z)` expanded by capsule radius `0.22` overlaps any AABB in `[...activeObstacles, ...exteriorObstacles]`.
2. `isInsideWorld(x, z, radius = 0.22)`:
   - Tests whether `(x, z)` is strictly within any room AABB (shrunk by `radius`), OR within corridor doorways (`x = ±2.5`), OR loop doors (`x = ±10.5, z = -24`), OR the entrance portal (`z = 6, |x| < 1.6`).
3. `isWalkable(x, z, radius = 0.22, checkObstacles = true)`:
   - Evaluates: finite coordinates AND `!isObstacle(x, z, radius)` AND `isInsideWorld(x, z, radius)`.

#### C. Continuous Collision Detection on Server (`server/game.js:49-105`)
- Function `segmentWalkable(a, b, radius = 0.22, checkObstacles = true)`:
  - Generates cut planes `collisionBoundaries(radius, checkObstacles)` along all room walls, portals, and obstacle edges.
  - Computes intersection parameter `t` along vector `(b - a)`.
  - Evaluates `isWalkable` at every boundary cut `t` AND at every interval midpoint `(t_k + t_{k-1}) / 2`.
  - Prevents high-speed tunneling or corner clipping through thin walls.
- Movement Anti-Cheat:
  - `GameManager.movePlayer` enforces `player.moveBudget` (regenerates at `dt * 6.5` m/s up to 2.5m).
  - Rejects movement if `distance(player, input) > moveBudget` or `!segmentWalkable(player, input)`.

#### D. Client Movement & Wall Sliding (`src/main.js:876-878`)
```javascript
const dx = (-Math.sin(yaw) * forward + Math.cos(yaw) * side) * speed;
const dz = (-Math.cos(yaw) * forward - Math.sin(yaw) * side) * speed;
if (isWalkable(localPos.x + dx, localPos.z)) localPos.x += dx;
if (isWalkable(localPos.x, localPos.z + dz)) localPos.z += dz;
```
- By decomposing movement into independent X and Z steps, the player smoothly slides along walls when moving diagonally into an obstacle.

#### E. Monster Navigation & Waypoints (`server/game.js:10-37, 107-159`)
- Graph of 38 fixed waypoints covering corridor, rooms, loop doorways, entrance, and yard.
- `navigationGraph()` generates a visibility graph between waypoints where `segmentWalkable(w_i, w_j)` is true, cached on `JSON.stringify(activeObstacles)`.
- `nextWaypoint(from, to)` computes shortest path via A* on the waypoint graph.
- **CRITICAL OBSERVATION**:
  - In `src/world.js:1174`:
    `box(root, -8, 0.49, -19, 3.0, 0.97, 1.58, mat('metal', 0x8e9f6b));`
    This diesel generator mesh is rendered directly in `world.js` and is **NOT** present in `activeObstacles` or `exteriorObstacles`!
  - In `src/exterior.js:195-198`: Barrels, crates, bin, and bench are rendered directly without obstacles in `exteriorObstacles`.
  - As a result, the player and monster currently walk straight through the diesel generator and yard props!
  - Furthermore, in `server/game.test.js:120`:
    `let location = { x: -8, z: -19 };`
    The test starts the monster navigation at `x = -8, z = -19`, which is the exact center of the uncollided generator mesh! If an obstacle is placed directly over `(-8, -19)`, the test's `assert.ok(isWalkable(location.x, location.z))` will fail unless the generator is placed with proper clearance or waypoints/test coordinates are aligned.

---

### 1.5 Level Editor (`builder.html`) & Preview Tools (`models-preview.html`)

#### A. Room Builder (`builder.html` & `src/builder.js`)
- Renders cutaway walls (1.2m height) so rooms can be viewed from top-down perspective.
- Visualizes station floor grid and outdoor courtyard (`x: 0, z: 21, w: 44, d: 30`).
- Left panel populates catalog buttons from `PROP_CATALOG` (`shared/props.js`).
- Raycast pointer interaction snaps props to 0.25m grid on the `Y=0` plane.
- Quick camera focus buttons: `courtyard`, `corridor`, `archive`, `apparatus`, `generator`, `studio`, `vent`, `server`, `storage`, `dispatch`.
- Persistence:
  - `GET /api/map`: Loads custom map from `shared/custom-map.json` or falls back to `DEFAULT_PROPS`.
  - `POST /api/map`: Writes `shared/custom-map.json` and invokes `game.syncCustomMap(props)`.
  - `POST /api/map/reset`: Deletes `custom-map.json` and resets server state to `DEFAULT_PROPS`.

#### B. Model Archive Preview (`models-preview.html` & `src/models-preview.js`)
- Displays 14 interactive model showcases:
  - 4 Bureau-9 survivors: Victor (01), Vivian (02), Edgar (03), Hazel (04).
  - 2 Stalkers: The Anchor (05), Bloody Anchor (06).
  - Vehicles & Scenery: Crew Van (07), Courtyard (08), Facade (09), Full Station (14).
  - Props: Camera TK-04 (10), VHS Master Tape (11), 380V Fuse (12), CRT Monitor (13).
- Uses `createProp(kind)` from `src/world.js:945-954`. Currently only handles `tape`, `fuse`, `monitor`, `archive`, `locker`, `camera`.

---

### 1.6 Gap Analysis per R1 & R4 (Original Request)

| Area | Required per R1 / R4 | Current Status | Collision Needed? |
|---|---|---|---|
| **Courtyard** | КПП / будка охорони (Guard booth) | ❌ Missing | Yes (`minX/maxX/minZ/maxZ`) |
| **Courtyard** | Шлагбаум (Barrier gate) | ❌ Missing | Yes |
| **Courtyard** | Прожекторні вежі (Searchlight floodlight towers) | ❌ Only 2 low posts | Yes (tower bases) |
| **Courtyard** | Кабельні бухти (Cable spools / wooden drums) | ❌ Missing | Yes |
| **Courtyard** | Розкидані бочки, ящики (Scattered barrels/crates) | ⚠️ Only 3 barrels, 2 crates hardcoded in `exterior.js`, no collision | Yes (must be in `activeObstacles`) |
| **Courtyard** | Сліди шин (Tire tracks decal) | ⚠️ Basic lines | No (ground decal) |
| **Courtyard** | Огорожа з колючим дротом (Barbed wire top) | ⚠️ Basic wire fence | Fence blocks `isInsideWorld` |
| **Courtyard** | Покинутий транспорт (Abandoned station vehicle) | ❌ Missing (only player van exists) | Yes |
| **Main Corridor** | Вхідна зона з турнікетами (Turnstiles) | ❌ Missing | Yes (with central walkable lane) |
| **Main Corridor** | Дошка пошани працівників (Honor board) | ❌ Missing | No (wall hanging) |
| **Main Corridor** | Розклад сітки мовлення (Broadcast schedule) | ❌ Missing | No (wall hanging) |
| **Main Corridor** | Пожежний щиток (Fire equipment board) | ⚠️ Only small extinguishers | Thin wall collision |
| **Main Corridor** | Стельові лотки з проводами (Cable trays) | ⚠️ Sagging cables only | No (overhead at Y > 3.0) |
| **Main Corridor** | Смітники (Trash cans) | ❌ Missing in corridor | Yes |
| **Main Corridor** | Розбитий посуд, сміття (Broken dishes/clutter) | ❌ Missing | No (floor decals/small debris) |
| **Archive** | Високі металеві стелажі (Tall archive racks) | ⚠️ Only low shelves | Yes |
| **Archive** | Коробки з касетами Betacam/VHS | ❌ Missing as prop stack | Yes |
| **Archive** | Каталожні шафи (Card catalog cabinets) | ❌ Missing | Yes |
| **Archive** | Стіл архіваріуса з настільною лампою | ⚠️ Table exists, lamp model missing | Table has collision |
| **Studio 04** | Хромакей / куліси (Chroma backdrop/curtains) | ⚠️ Only rear striped wall | Thin wall collision |
| **Studio 04** | Важкі телекамери на триногах | ⚠️ 2 exist | Yes |
| **Studio 04** | Мікрофонні стійки (Mic boom stands) | ❌ Missing | Small radius collision |
| **Studio 04** | Дикторський стіл (Curved news anchor desk) | ⚠️ Generic rectangular table | Yes |
| **Studio 04** | Освітлювальні софіти (Ceiling lighting truss) | ❌ Missing | No (overhead at Y > 3.0) |
| **Generator Room** | Масивний дизель-генератор (Diesel generator unit) | ⚠️ Box at `(-8, -19)` has NO collision | **YES (Critical Fix)** |
| **Generator Room** | Вентилі паливної лінії (Fuel line valves) | ❌ Missing (Quest Stage 2 item) | Yes / Interactable |
| **Generator Room** | Каністри з соляркою (Diesel jerrycans) | ❌ Missing (Quest Stage 2 item) | Yes / Interactable |
| **Generator Room** | Високовольтний щит 380В з гніздами під 2 запобіжники | ⚠️ Generic lever box; no visual fuse slots | Yes |
| **Vent Room** | Повітропроводи та решітки (Air ducts & grates) | ⚠️ Only wall fan | Duct columns: Yes |
| **Server Room** | 90s серверні стійки з миготливими діодами | ⚠️ 6 basic racks | Yes |
| **Server Room** | Перфокарти, папери, кабелі на підлозі | ❌ Missing | No (floor decals) |
| **Apparatus Room** | Пульти прямого ефіру з фейдерами (Mixing console) | ⚠️ Generic table | Yes |
| **Apparatus Room** | Осцилографи та монітори відеоконтролю | ⚠️ Single CRT only | Yes (sits on console) |
| **Apparatus / Dispatch** | Звукоізоляційні панелі (Acoustic foam panels) | ❌ Missing | Thin wall decor |
| **R4 Requirements** | Blueprint map (`M`) updates for door/subsystem states | ⚠️ Static SVG only | Updates to `showMap()` |
| **R4 Requirements** | `builder.html` and `models-preview.html` support new props | ⚠️ Only 9 catalog types | Update catalog & preview list |

---

## 2. Logic Chain

1. **Collision Integrity Logic**:
   - *Observation*: `propToObstacle(p)` in `shared/world-data.js` only checks 7 types (`table`, `shelf`, `serverRack`, `crate`, `barrel`, `camera`, `locker`). Any prop type outside this set returns `null` and has zero physical presence.
   - *Observation*: The generator at `x = -8, z = -19` in `src/world.js:1174` is rendered via raw Three.js boxes without an entry in `world-data.js` or `activeObstacles`.
   - *Observation*: `server/game.test.js:120` initializes navigation at `{ x: -8, z: -19 }`.
   - *Deduction*: Adding collision to the generator must either place the generator footprint offset from `(-8, -19)` (e.g. `minZ: -21.5, maxZ: -19.5` or `minX: -11.0, maxX: -8.5`) or adjust the test spawn coordinate so the monster does not start inside the collision AABB.
   - *Deduction*: Every new prop introduced must have a dedicated branch in `propToObstacle` calculating its rotated AABB.

2. **Monster Navigation & Waypoint Preservation Logic**:
   - *Observation*: `server/game.js` builds its visibility graph between `WAYPOINTS` using `segmentWalkable()`.
   - *Observation*: If an obstacle intersects a line between waypoints (e.g., in the corridor at `x = 0`), the creature's pathfinding graph becomes disconnected or routed improperly.
   - *Deduction*: Corridor props (such as turnstiles or trash bins) must NOT block `x = 0`. The central corridor axis `X = 0` must remain completely clear for straight navigation from `Z = 4` to `Z = -52`. Turnstiles must be placed at `X = ±1.0` with an open 1.2m gate at `X = 0`.

3. **60 FPS Performance Preservation Logic**:
   - *Observation*: `createWorld` in `src/world.js:1308-1330` batches all static meshes sharing the same material into a single `BufferGeometry` via `mergeGeometries`.
   - *Observation*: Dynamic point lights in WebGL shaders incur high multi-pass or fragment shader overhead if too many lights overlap.
   - *Deduction*: Tripling the prop count will NOT degrade frame rate if:
     1. Props reuse existing palette materials (`metal`, `wood`, `rust`, `plaster`, `fabric`).
     2. Static props are NOT placed in the `dynamic` Set, allowing `mergeGeometries` to automatically merge them into single draw calls.
     3. Emissive materials (`MeshStandardMaterial.emissive` / `MeshBasicMaterial`) are used for illuminated indicators, dials, and screens instead of spawning real Three.js `PointLight` sources.
     4. Dynamic shadow maps remain strictly limited to the player's flashlight and exterior directional sunlight.

4. **Level Editor & Preview Tool Compatibility Logic**:
   - *Observation*: `builder.js` renders the courtyard floor and grid and queries `/api/map`.
   - *Observation*: `builder.js` reads `PROP_CATALOG` from `shared/props.js` to build its UI palette.
   - *Observation*: `models-preview.html` switches between models using keys defined in `models-preview.js:info` and calls `createProp(key)`.
   - *Deduction*: Expanding `PROP_CATALOG` in `shared/props.js` and `createProp` in `src/world.js` guarantees that `builder.html` and `models-preview.html` automatically support every newly added prop with zero architectural rework.

---

## 3. Caveats

1. **No Source Modifications in this Phase**: In compliance with the Teamwork Explorer identity constraints, this investigation is strictly read-only; no code files outside `.agents/teamwork/explorer_survey_1/` have been altered.
2. **Headless Canvas Mocking**: Unit tests in `server/world-regression.test.js` mock `document.createElement('canvas')` using a proxy. Any new procedural textures added must not call unmocked canvas API methods (standard 2D context drawing methods like `fillRect`, `arc`, `stroke`, `getImageData` are fully supported).
3. **Saved Custom Map Sensitivity**: If `shared/custom-map.json` exists on disk, `server/index.js` and `src/main.js` prioritize it over `DEFAULT_PROPS`. When updating `DEFAULT_PROPS`, `shared/custom-map.json` must either be updated to reflect the new props or reset via `POST /api/map/reset`.

---

## 4. Conclusion & Technical Recommendations

### 4.1 Recommended New Prop Types (`shared/props.js` & `PROP_CATALOG`)

The following 18 new prop types should be added to `PROP_CATALOG` with dimensions, icons, and collision profiles:

```javascript
export const PROP_CATALOG = [
  // Existing props (9)
  { type: 'table', name: 'Стіл / Верстак', icon: '🪵', defaultW: 2.2, defaultD: 1.2 },
  { type: 'chair', name: 'Стілець оператора', icon: '🪑' },
  { type: 'locker', name: 'Шафа (Сховок)', icon: '🚪' },
  { type: 'shelf', name: 'Стелаж архіву', icon: '🗄' },
  { type: 'serverRack', name: 'Серверна стійка', icon: '🖧' },
  { type: 'crate', name: 'Дерев’яний ящик', icon: '📦', defaultSize: 0.9 },
  { type: 'barrel', name: 'Металева бочка', icon: '🛢' },
  { type: 'crt', name: 'Монітор ЕЛП', icon: '🖥' },
  { type: 'camera', name: 'Камера ТК–04', icon: '📹' },

  // New Yard Props
  { type: 'guardBooth', name: 'КПП / Будка охорони', icon: '🏠', defaultW: 2.4, defaultD: 2.2 },
  { type: 'barrier', name: 'Шлагбаум зі стійкою', icon: '🚧', defaultW: 3.2, defaultD: 0.4 },
  { type: 'floodlightTower', name: 'Прожекторна вишка', icon: '🗼', defaultW: 1.2, defaultD: 1.2 },
  { type: 'cableSpool', name: 'Кабельна котушка', icon: '🪵', defaultW: 1.2, defaultD: 1.2 },
  { type: 'abandonedCar', name: 'Покинутий транспорт', icon: '🚗', defaultW: 2.1, defaultD: 4.2 },

  // New Hall / Corridor Props
  { type: 'turnstile', name: 'Турнікети входу', icon: '🛑', defaultW: 2.8, defaultD: 0.8 },
  { type: 'fireBoard', name: 'Пожежний щиток', icon: '🧯', defaultW: 1.4, defaultD: 0.25 },
  { type: 'trashBin', name: 'Смітник коридорний', icon: '🗑', defaultW: 0.5, defaultD: 0.5 },

  // New Studio & Archive Props
  { type: 'cardCatalog', name: 'Каталожна шафа архіву', icon: '🗃', defaultW: 1.6, defaultD: 0.7 },
  { type: 'archivistDesk', name: 'Стіл з лампою', icon: '💼', defaultW: 2.2, defaultD: 1.2 },
  { type: 'anchorDesk', name: 'Дикторський стіл новин', icon: '🎙', defaultW: 3.4, defaultD: 1.8 },
  { type: 'chromakeyBackdrop', name: 'Хромакей / Куліси', icon: '🟩', defaultW: 4.6, defaultD: 0.3 },
  { type: 'micStand', name: 'Мікрофонна стійка', icon: '🎤', defaultW: 0.4, defaultD: 0.4 },

  // New Industrial / Generator / Vent / Apparatus Props
  { type: 'dieselGenerator', name: 'Дизель-генератор', icon: '⚡', defaultW: 3.2, defaultD: 1.8 },
  { type: 'fuelCanister', name: 'Каністра солярки', icon: '🛢', defaultW: 0.5, defaultD: 0.35 },
  { type: 'fuelValve', name: 'Аварійний вентиль', icon: '🔴', defaultW: 0.6, defaultD: 0.5 },
  { type: 'fusePanel', name: 'Щит 380В з гніздами', icon: '🔌', defaultW: 1.2, defaultD: 0.4 },
  { type: 'mixingConsole', name: 'Пульт ефіру з фейдерами', icon: '🎛', defaultW: 2.6, defaultD: 1.4 },
  { type: 'monitorWall', name: 'Стійка відеоконтролю', icon: '📺', defaultW: 2.2, defaultD: 0.9 },
  { type: 'airDuct', name: 'Венткороб вертикальний', icon: '🏭', defaultW: 1.1, defaultD: 1.1 },
];
```

---

### 4.2 Recommended Collision Calculation (`shared/world-data.js:propToObstacle`)

Extend `propToObstacle(p)` with rotated AABB boxes:

```javascript
case 'guardBooth': {
  const w = (p.w || 2.4) * 0.48;
  const d = (p.d || 2.2) * 0.48;
  return { id: p.id, type: 'guardBooth', minX: p.x - (w*cos + d*sin), maxX: p.x + (w*cos + d*sin), minZ: p.z - (w*sin + d*cos), maxZ: p.z + (w*sin + d*cos) };
}
case 'abandonedCar': {
  const w = (p.w || 2.1) * 0.46;
  const d = (p.d || 4.2) * 0.46;
  return { id: p.id, type: 'abandonedCar', minX: p.x - (w*cos + d*sin), maxX: p.x + (w*cos + d*sin), minZ: p.z - (w*sin + d*cos), maxZ: p.z + (w*sin + d*cos) };
}
case 'dieselGenerator': {
  const w = (p.w || 3.2) * 0.46;
  const d = (p.d || 1.8) * 0.46;
  return { id: p.id, type: 'dieselGenerator', minX: p.x - (w*cos + d*sin), maxX: p.x + (w*cos + d*sin), minZ: p.z - (w*sin + d*cos), maxZ: p.z + (w*sin + d*cos) };
}
case 'cableSpool': {
  const r = 0.55;
  return { id: p.id, type: 'cableSpool', minX: p.x - r, maxX: p.x + r, minZ: p.z - r, maxZ: p.z + r };
}
case 'barrier': {
  const w = 1.6 * cos + 0.2 * sin;
  const d = 1.6 * sin + 0.2 * cos;
  return { id: p.id, type: 'barrier', minX: p.x - w, maxX: p.x + w, minZ: p.z - d, maxZ: p.z + d };
}
case 'cardCatalog': {
  const w = (p.w || 1.6) * 0.45;
  const d = (p.d || 0.7) * 0.45;
  return { id: p.id, type: 'cardCatalog', minX: p.x - (w*cos + d*sin), maxX: p.x + (w*cos + d*sin), minZ: p.z - (w*sin + d*cos), maxZ: p.z + (w*sin + d*cos) };
}
case 'mixingConsole': {
  const w = (p.w || 2.6) * 0.46;
  const d = (p.d || 1.4) * 0.46;
  return { id: p.id, type: 'mixingConsole', minX: p.x - (w*cos + d*sin), maxX: p.x + (w*cos + d*sin), minZ: p.z - (w*sin + d*cos), maxZ: p.z + (w*sin + d*cos) };
}
case 'anchorDesk': {
  const w = (p.w || 3.4) * 0.46;
  const d = (p.d || 1.8) * 0.46;
  return { id: p.id, type: 'anchorDesk', minX: p.x - (w*cos + d*sin), maxX: p.x + (w*cos + d*sin), minZ: p.z - (w*sin + d*cos), maxZ: p.z + (w*sin + d*cos) };
}
case 'monitorWall': {
  const w = (p.w || 2.2) * 0.45;
  const d = (p.d || 0.9) * 0.45;
  return { id: p.id, type: 'monitorWall', minX: p.x - (w*cos + d*sin), maxX: p.x + (w*cos + d*sin), minZ: p.z - (w*sin + d*cos), maxZ: p.z + (w*sin + d*cos) };
}
case 'airDuct': {
  const r = 0.52;
  return { id: p.id, type: 'airDuct', minX: p.x - r, maxX: p.x + r, minZ: p.z - r, maxZ: p.z + r };
}
case 'turnstile': {
  // Turnstiles span side columns with central walkable aisle
  const w = 1.35;
  const d = 0.35;
  return { id: p.id, type: 'turnstile', minX: p.x - w, maxX: p.x + w, minZ: p.z - d, maxZ: p.z + d };
}
case 'fuelCanister': {
  const r = 0.26;
  return { id: p.id, type: 'fuelCanister', minX: p.x - r, maxX: p.x + r, minZ: p.z - r, maxZ: p.z + r };
}
case 'fuelValve': {
  const r = 0.3;
  return { id: p.id, type: 'fuelValve', minX: p.x - r, maxX: p.x + r, minZ: p.z - r, maxZ: p.z + r };
}
```

---

### 4.3 Detailed Room Layout & Placement Recommendations

1. **Outdoor Courtyard (`Z: 6` to `36`)**:
   - **Guard Booth (КПП)**: Position at `x: -12.5, z: 32.0` (near the main gate at `z = 36`). Holds the Security Keycard required for Stage 1.
   - **Barrier Gate (Шлагбаум)**: Across vehicle lane at `x: -7.0, z: 34.5`.
   - **Searchlight Tower (Прожекторна вишка)**: North-west yard corner at `x: -18.5, z: 28.0` with low-poly lattice structure and angle spotlight.
   - **Cable Spools (Котушки)**: Clustered at `x: 14.5, z: 18.0` and `x: 16.0, z: 19.2`.
   - **Abandoned Vehicle (Покинутий ГАЗ/УАЗ)**: Parked diagonally at `x: 8.5, z: 22.0, rotation: 0.45` with flat tires, shattered glass material, and rust.
   - **Courtyard Barrels & Crates**: Add explicit entries into `DEFAULT_PROPS` so they possess collisions instead of being ghost meshes.

2. **Main Entrance & Corridor (`X: -2.5` to `2.5`, `Z: -54` to `6`)**:
   - **Turnstiles**: Place at `x: 0, z: 4.2` with two waist-high stainless tripod turnstiles at `x: -1.2` and `x: +1.2`, leaving an unobstructed passage through `X = 0` so monster pathfinding is never impeded.
   - **Wall of Honor (Дошка пошани)**: West corridor wall at `x: -2.36, y: 1.8, z: 1.5, ry: Math.PI / 2` with photos of station workers.
   - **Broadcast Schedule Grid (Сітка мовлення)**: East corridor wall at `x: 2.36, y: 1.8, z: 1.5, ry: -Math.PI / 2`.
   - **Fire Equipment Board (Пожежний щиток)**: At `x: -2.36, y: 1.6, z: -20.0` with fire axe, shovel, and bucket models.
   - **Ceiling Cable Trays**: Overhead metal trays suspended at `y: 3.1` running along the corridor axis.
   - **Trash Bins & Clutter**: Corner bins at `x: -2.0, z: -8.0` and `x: 2.0, z: -38.0`.

3. **Archive Room (`X: -12` to `-2.5`, `Z: -9` to `6`)**:
   - **Archivist Desk & Lamp**: At `x: -8.0, z: -3.0` with green banker's desk lamp mesh (`glow(0x66cc44, 0.8)`).
   - **Tall Metal Archive Racks**: Double-sided shelving rows at `x: -11.0, z: 1.5` and `x: -6.5, z: 2.5`.
   - **Card Catalog**: Wooden 16-drawer index unit at `x: -11.2, z: -6.5`.
   - **Betacam / VHS Cassette Stacks**: Stacked boxes on shelves and floor.

4. **Studio 04 (`X: 2.5` to `12`, `Z: -24` to `-9`)**:
   - **News Anchor Desk**: Curved presenter table at `x: 8.0, z: -16.5` with station 04 logo badge.
   - **Chroma Backdrop / Curtains**: Deep green backdrop at `z = -22.5` spanning `x: 4.5` to `11.5`.
   - **Studio TV Cameras (TK-04)**: 2 cameras positioned on tripods at `x: 5.5, z: -13.5` and `x: 10.5, z: -13.5` aiming at anchor desk.
   - **Microphone Boom Stands**: Standing mics at `x: 6.8, z: -16.0`.
   - **Overhead Lighting Grid**: Square aluminum truss with 6 low-poly spotlight cans under the ceiling (`y = 3.2`).

5. **Generator Room (`X: -12` to `-2.5`, `Z: -24` to `-9`)**:
   - **Heavy Industrial Diesel Generator**: Model with engine block, radiator fins, exhaust stack, and flywheel. Position centered at `x: -8.0, z: -20.5` (clear of `z = -17` and `z = -19` waypoint clearance).
   - **Fuel Canister (Каністра солярки)**: Quest pickup object at `x: -10.5, z: -12.5`.
   - **Pressure Relief Valve (Вентиль тиску)**: Interactive valve on pipe at `x: -11.6, z: -22.0`.
   - **High-Voltage 380V Fuse Box**: Panel at `x: -11.75, z: -16.5` with 2 visible empty slots that display ceramic fuses when inserted.

6. **Vent Room & Server Room (`Z: -39` to `-24`)**:
   - **Vent Room**: Industrial vertical ventilation duct at `x: -8.5, z: -35.0`, wall louvers, emergency pressure valve.
   - **Server Room**: Add reel-to-reel magnetic tape drive cabinet (`tapeDrive`), paper punchcard clutter, serpentine cable bundles on floor.

7. **Apparatus Room & Dispatcher (`Z: -54` to `-39` and `Z: -9` to `6`)**:
   - **Mixing Console**: Multi-track audio/video mixing console at `x: 10.0, z: -2.0` with analog faders and illuminated VU meters.
   - **Oscilloscope**: Dual-trace cathode ray oscilloscope with green waveform line.
   - **Monitor Wall Rack**: 3×3 matrix of mini CRT monitors in Dispatcher room (`x: 8.5, z: -48.0`).
   - **Acoustic Panels**: Dark textured foam wedges along the studio and apparatus partition walls.

---

### 4.4 Technical Guidelines for 60 FPS Performance

1. **Draw Call Minimization**:
   - Continue leveraging `BufferGeometryUtils.mergeGeometries(geometries, false)` for all static additions.
   - Restrict new props to shared material instances (`mat('metal')`, `mat('wood')`, `mat('rust')`, `mat('plaster')`, `plain(...)`).
   - All newly added static props will automatically be collapsed into the station's existing ~10 draw call batches.
2. **Lighting Budget**:
   - **Do NOT** add individual Three.js `PointLight` or `SpotLight` objects to decorative props.
   - Use `MeshStandardMaterial({ emissive, emissiveIntensity })` or `MeshBasicMaterial` for LEDs, screens, dials, and small lamps.
   - Reserve dynamic lights exclusively for:
     - Player flashlight (1 SpotLight, castShadow = true).
     - Courtyard directional moonlight (1 DirectionalLight, castShadow = true).
     - Corridor practicals (9 PointLights, no shadow casting).
     - Room practicals (1 PointLight per room, no shadow casting).
3. **Polygon Budget**:
   - Box primitives: 1 segment per axis (12 triangles).
   - Cylinders (pipes, barrels, drums, posts): 8 to 12 radial segments.
   - Cones / Spheres: 8 to 10 segments.
   - Target scene polygon count: < 25,000 triangles total (trivial load for any modern GPU).
4. **Collision Grid & Query Optimization**:
   - `isObstacle()` performs a linear loop over `[...activeObstacles, ...exteriorObstacles]`.
   - With ~70 props, an array iteration of 70 AABB comparisons takes less than **0.005 milliseconds** per physics step in V8, completely negligible compared to the 16.6ms frame budget.
   - For `server/game.js`, `navigationGraph()` is only rebuilt when `activeObstacles` signature changes, guaranteeing zero server CPU overhead during active gameplay.

---

## 5. Verification Method

To verify these findings and ensure zero regressions during future implementation:

1. **Automated Unit & Server Tests**:
   ```bash
   npm test
   ```
   - Must pass all 19 tests with exit code 0.
   - Special attention to:
     - `the creature navigates around room walls via open doorways`: Ensures `WAYPOINTS` visibility graph has valid paths and does not collide with new props.
     - `real station scene stays valid and updates through power, broadcast, escape and restart`: Tests headless Three.js scene creation and geometry merging.
2. **Production Bundle Verification**:
   ```bash
   npm run build
   ```
   - Must build without bundling errors for all three entry points (`index.html`, `builder.html`, `models-preview.html`).
3. **Level Editor Verification**:
   - Open `/builder.html` in browser.
   - Confirm all newly added prop types appear in the left catalog palette with icons and labels.
   - Confirm props can be placed in both station rooms and the outdoor courtyard.
   - Confirm saving via `POST /api/map` writes to `shared/custom-map.json`.
4. **Model Archive Verification**:
   - Open `/models-preview.html` in browser.
   - Confirm new props are selectable in the sidebar and render cleanly in the 3D viewport.
5. **Invalidation Conditions**:
   - Any prop placed overlapping `X = 0` in the corridor that breaks monster waypoint navigation.
   - Any prop placed at `x = -8, z = -19` that causes `server/game.test.js` line 120 navigation test to fail.
   - Spawning unbatched Three.js `PointLight` objects exceeding 25 total lights in the scene.
