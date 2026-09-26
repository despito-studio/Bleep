# Project: Blackpine Broadcast // BUREAU–9

## Architecture
- **Engine**: Three.js (r180) procedural PS1/low-poly rendering, Web Audio API procedural synthesis, Vite build pipeline.
- **Server**: Node.js, Express, Socket.IO authoritative 12 Hz simulation (`server/game.js`), 1-4 player co-op.
- **Collision & Spatial Partitioning**:
  - `shared/world-data.js`: AABB obstacles (`activeObstacles`), `segmentWalkable` intersection solver, room boundary definitions.
  - Dynamic door collision: closed/locked doors register bounding box obstacles into the collision grid to physically block players and monster Dijkstra pathfinding.
- **Threat AI**: 3-phase state machine (Phase 1: Dormant/Shadow; Phase 2: Active Hunt with sound/light sensing; Phase 3: Nightmare pursuit; Creative Mode: passive).
- **Tools**: `builder.html` (level editor) & `models-preview.html` (3D asset showcase) hooked into `PROP_CATALOG` in `shared/props.js`.

## Feature Inventory
| # | Feature | Description | Milestone | Source |
|---|---------|-------------|-----------|--------|
| 1 | Courtyard Guard Booth & Barrier | 3D security CP shack and barrier gate at courtyard entrance | M1 | ORIGINAL_REQUEST §R1 |
| 2 | Spotlight Towers & Cable Spools | Procedural floodlight towers and heavy wooden cable spools in yard | M1 | ORIGINAL_REQUEST §R1 |
| 3 | Barrels & Crates with Collisions | Yard and indoor metal drums and wooden crates with impassable AABB obstacles | M1 | ORIGINAL_REQUEST §R1 |
| 4 | Perimeter Fence & Barbed Wire | Enclosing courtyard fence with barbed wire overhang | M1 | ORIGINAL_REQUEST §R1 |
| 5 | Abandoned Transport in Courtyard | Decaying truck/car wreckage in yard | M1 | ORIGINAL_REQUEST §R1 |
| 6 | Main Hall Turnstiles & Board of Honor | Lobby entrance turnstiles and employee portrait board | M1 | ORIGINAL_REQUEST §R1 |
| 7 | Broadcast Schedule Board & Fire Hose Panel | Corridor wall schedule grid and emergency fire hose cabinet | M1 | ORIGINAL_REQUEST §R1 |
| 8 | Flickering Fluorescent Lights & Wire Trays | Ceiling cable trays and humming/flickering tube lamps | M1 | ORIGINAL_REQUEST §R1 |
| 9 | Debris, Trash Bins & Broken Crockery | Scatter props in rooms and corridor | M1 | ORIGINAL_REQUEST §R1 |
| 10 | Archive Racks & Betacam/VHS Storage | High industrial shelving with tape boxes and archivist desk with lamp | M1 | ORIGINAL_REQUEST §R1 |
| 11 | Studio 04 Chromakey, Cameras & Mic Stands | Filming pavilion with green backdrop, heavy TK-04 cameras on tripods | M1 | ORIGINAL_REQUEST §R1 |
| 12 | Generator Room Diesel Engine & 380V Panel | Massive diesel unit with fuel pipes and ceramic fuse slots | M1 | ORIGINAL_REQUEST §R1 |
| 13 | Ventilation Ducts, Grates & 90s Server Racks | Air ducts, ventilation grates, and server towers with blinking LEDs | M1 | ORIGINAL_REQUEST §R1 |
| 14 | Control Room Consoles & Video Monitors | Broadcast control consoles with faders, audio monitors, acoustic panels | M1 | ORIGINAL_REQUEST §R1 |
| 15 | Procedural Low-Poly PS1 Geometry & Palette | 128x128 canvas textures, flat shading, NearestFilter, retro palette | M1 | ORIGINAL_REQUEST §R1 |
| 16 | Solid Collision Grid & Generator Obstacle Fix | Update `propToObstacle()` in `world-data.js` and fix generator walk-through bug | M1 | ORIGINAL_REQUEST §R1 |
| 17 | 60 FPS Geometry Merging Optimization | Batch static props by material UUID into minimal draw calls | M1 | ORIGINAL_REQUEST §R1 |
| 18 | Builder & Models Preview Tool Support | Expand `PROP_CATALOG` in `props.js` for `builder.html` and `models-preview.html` | M1 | ORIGINAL_REQUEST §R4 |
| 19 | Replicated Door Locking States & Dynamic Obstacles | Server-authoritative magnetic door locks blocking player/monster movement | M2 | ORIGINAL_REQUEST §R2/R4 |
| 20 | Guard & Engineer Keycards Network Sync | Keycard items with group replication and reader authorization | M2 | ORIGINAL_REQUEST §R2/R4 |
| 21 | Interactive SVG Station Map (`M`) Upgrade | Dynamic map showing live player dots, door lock statuses, and subsystem LEDs | M2 | ORIGINAL_REQUEST §R4 |
| 22 | Multiplayer Inventory & Quest Synchronization | Full replication of keys, canister, fuses, valves across 1-4 players | M2 | ORIGINAL_REQUEST §R4 |
| 23 | Quest Stage 1: Recon & Access (0-8 min) | Courtyard CP search, Archive unlock, Tape 1, Power Schematic note | M3 | ORIGINAL_REQUEST §R2 |
| 24 | Quest Stage 2: Power Unit Startup (8-18 min) | Fuel canister, vent pressure valve, 2x 380V fuses, generator start sequence | M3 | ORIGINAL_REQUEST §R2 |
| 25 | Quest Stage 3: Signal Calibration (18-25 min) | Tapes 2 & 3, 96.4 MHz calibration minigame in Apparatus room, transmission | M3 | ORIGINAL_REQUEST §R2 |
| 26 | Quest Stage 4: Evacuation (25-30 min) | Alarm wail, gate unlock, emergency lighting, 60m sprint to Bureau van | M3 | ORIGINAL_REQUEST §R2 |
| 27 | Atmospheric Audio & Sound Effects | Diesel engine roar, steam valve hiss, alarm siren, radio static cues | M3 | ORIGINAL_REQUEST §R2 |
| 28 | Monster Phase 1: Recon/Shadow (Power OFF) | Dormant monster, vent crawling sounds, prop clatters, shadows, locker hiding | M4 | ORIGINAL_REQUEST §R3 |
| 29 | Monster Phase 2: Active Hunt (Power ON) | Generator wake-up, corridor & room search, sprint hearing (14m), flashlight attraction | M4 | ORIGINAL_REQUEST §R3 |
| 30 | Monster Phase 3: Nightmare Pursuit | Bloody transformation, 3.2 m/s sprint, relentless chase to courtyard van | M4 | ORIGINAL_REQUEST §R3 |
| 31 | Monster Creative Mode Passivity | 100% passive monster, zero damage, full quest exploration enabled | M4 | ORIGINAL_REQUEST §R3 |
| 32 | End-to-End Opaque-box Test Suite Validation | 100% pass across Tiers 1-4 test cases | M5 | ORIGINAL_REQUEST Acceptance |
| 33 | Adversarial Coverage Hardening | Tier 5 white-box stress testing and boundary hardening | M5 | ORIGINAL_REQUEST Acceptance |

## Milestones
| # | Name | Scope | Dependencies | Status |
|---|------|-------|-------------|--------|
| M1 | Level Design, 3D Atmospheric Props & Physical Collisions | Features 1–18: Procedural props, station & yard visual overhaul, solid collision obstacles, 60 FPS batching, builder & preview sync | None | IN_PROGRESS (worker 1752d7e6-a9d2-4930-ac47-e0afbad150ef) |
| M2 | Door Locking Mechanics, Inventory & Multiplayer Quest Sync | Features 19–22: Authoritative door locks, dynamic obstacle registration, keycards, multiplayer inventory sync, interactive map HUD (`M`) | None (parallel with M1) | PLANNED |
| M3 | Multi-Stage Quest Flow & 20–30 Min Gameplay Loop | Features 23–27: 4-stage quest logic (Stages 1-4), fuel, valve, 380V fuses, 96.4 MHz calibration minigame, evacuation sequence, audio SFX | M1, M2 | PLANNED |
| M4 | The Anchor Monster 3-Phase Threat AI & Creative Mode | Features 28–31: Dormant shadow phase, active hunting with sprint/flashlight sensing, nightmare pursuit, Creative mode passivity | M2, M3 | PLANNED |
| M5 | Final Integration, 100% E2E Test Suite Pass & Adversarial Hardening | Features 32–33: Phase 1 pass 100% of Tiers 1-4 E2E tests from TEST_READY.md; Phase 2 adversarial coverage hardening | M1, M2, M3, M4, TEST_READY.md | PLANNED |

## Interface Contracts
### Client ↔ Server Network Protocol (`server/game.js` ↔ `src/main.js`)
- `room.doors`: `{ [doorId: string]: { locked: boolean, requiredKey: string | null, open: boolean } }`
- `room.quest`: `{ stage: number, fuelAdded: boolean, valveOpened: boolean, fusesInstalled: number, power: boolean, calibrated: boolean, broadcast: boolean, evacuated: boolean }`
- `room.inventory`: `{ keycards: Set<string>, canister: boolean, fuses: Set<string>, tapes: Set<string> }`
- `game:interact`: payload `{ id: string }` -> server validates player distance (`<= 2.5m`), line-of-sight (`segmentWalkable`), inventory requirements, updates state, emits updated `room:state`.
- `game:calibrate`: payload `{ frequency: number }` -> server checks proximity to broadcast console and tolerance `Math.abs(freq - 96.4) < 0.15`.

### Collision System Contract (`shared/world-data.js`)
- `activeObstacles`: Array of `{ id: string, minX: number, maxX: number, minZ: number, maxZ: number }`
- `propToObstacle(prop)`: Returns AABB object for any collision-enabled prop type, maintaining safe player navigability.
- `isWalkable(x, z, radius, obstacles)`: Evaluates room bounds and obstacle AABBs with sliding collision response.
- `segmentWalkable(a, b, radius)`: Line-of-sight and path traversal verification for player movement, monster A* pathfinding, and interaction raycasts.

## Code Layout
- `shared/world-data.js`: World geometry bounds, rooms, waypoints, obstacle collision definitions, spatial queries.
- `shared/props.js`: `PROP_CATALOG` definitions and default prop placement list.
- `src/world.js`: Station interior 3D meshes, procedural canvas textures, prop generators, static mesh batching.
- `src/exterior.js`: Courtyard terrain, lighting, fences, checkpoint booth, vehicle models, yard props.
- `src/main.js`: Client game loop, input handling, interaction raycasting, camera/flashlight, audio cues, HUD, SVG map modal.
- `src/audio.js`: Web Audio procedural synthesizer, sound effects, 3D spatial cues, ambient hums, alarms.
- `src/interface.js`: HUD markup, inventory badges, objective markers, dialog/toast UI, minigame overlays.
- `src/builder.js` & `builder.html`: 3D visual level editor.
- `src/models-preview.js` & `models-preview.html`: 3D asset viewer and testbench.
- `server/game.js`: Authoritative game engine, multiplayer room simulation, monster AI state machine, quest state.
- `server/index.js`: Express & Socket.IO server setup.
- `tests/e2e/`: Opaque-box E2E test suite (Tiers 1-4).
