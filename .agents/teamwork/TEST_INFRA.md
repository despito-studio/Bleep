# TEST_INFRA — Blackpine Broadcast // BUREAU-9 E2E Test Infrastructure

## 1. Test Philosophy: Opaque-Box & Requirement-Driven

The End-to-End (E2E) Test Suite for **Blackpine Broadcast // BUREAU-9** is built upon strict **opaque-box** (black-box) and **requirement-driven** testing principles:
1. **Opaque-Box Contract Testing**: Tests interact solely with public system boundaries:
   - Authoritative game engine API (`GameManager`, `createRoom`, `joinRoom`, `movePlayer`, `interact`, `radioPing`, `tick`).
   - Network wire state serialization (`GameManager.state(room)` broadcast over Socket.IO).
   - Spatial physics & collision queries (`isWalkable`, `isObstacle`, `segmentWalkable`, `propToObstacle`).
   - World data geometry, room partitioning, and navigation visibility graphs (`rooms`, `activeObstacles`, `nextWaypoint`).
   - Client interface and HUD DOM generation (`interfaceMarkup`).
   - Procedural 3D scene graph verification via headless canvas shims (`createWorld`, `BufferGeometryUtils.mergeGeometries`).
2. **Authoritative Source of Truth**: Expected outputs and behaviors are strictly derived from:
   - `ORIGINAL_REQUEST.md`: Specifications R1 (Level Design & Collisions), R2 (4-Stage 20-30 min Quest Progression), R3 (3-Phase Monster AI Threat), and R4 (Multiplayer Sync & Editor Support).
   - `PROJECT.md`: 33-feature inventory, milestone schedule, interface contracts, collision definitions, and layout standards.
3. **Headless & Deterministic Execution**:
   - Zero external binary or browser dependencies: runs completely inside Node.js using `node:test` and `node:assert/strict`.
   - Time advancement is controlled deterministically via mocked clocks (`now: () => timestamp`), ensuring repeatable physics ticks, movement rates, and monster pathfinding evaluations.
4. **Adversarial & Boundary Rigor**:
   - Every feature is subjected to negative inputs, prototype pollution attacks (`__proto__`, `constructor`), out-of-bounds coordinates, non-finite values (`NaN`, `Infinity`), and disconnected network states.

---

## 2. Test Architecture & Directory Layout

```
C:\Users\noona\Documents\ChatGPT\Test\
├── tests\
│   └── e2e\
│       ├── test-helpers.js          # Shared test fixtures, room setup, mock clients, path helpers
│       ├── tier1-features.test.js    # Tier 1: 33 features × >=5 tests = 165+ isolated feature tests
│       ├── tier2-boundaries.test.js  # Tier 2: 33 features × >=5 tests = 165+ boundary/negative tests
│       ├── tier3-interactions.test.js# Tier 3: Pairwise feature interactions, co-op sync, state coupling
│       └── tier4-scenarios.test.js   # Tier 4: Complete 0-30 min end-to-end gameplay scenarios
├── server\
│   ├── game.js                      # Authoritative engine & simulation
│   └── index.js                     # Express & Socket.IO network server
├── shared\
│   ├── world-data.js                # Geometry bounds, collision detection, obstacles
│   ├── props.js                     # Prop definitions & catalog
│   └── classes.js                   # Character classes & perk data
└── src\
    ├── interface.js                 # HUD markup & UI modal generators
    └── world.js                     # Procedural 3D geometry & static batching
```

### Test Harness Infrastructure (`tests/e2e/test-helpers.js`)
- `setupTestGame(options)`: Instantiates isolated `GameManager` with controlled mock clock (`now`) and helper methods:
  - `advance(ms)`: Increments simulated clock and steps server simulation `tick()`.
  - `place(playerId, x, z)`: Places player at coordinates for targeted interaction/collision testing.
  - `walkTo(target)`: Uses `nextWaypoint()` to realistically walk player across the station waypoint graph while respecting movement rate budgets.
- `createTestCanvasDocument()`: Headless DOM canvas mock enabling Three.js scene creation, texture generation, and material batching verification without GPU acceleration.
- `createLiveSocketCluster()`: Spins up ephemeral HTTP + Socket.IO server on dynamic port for multi-client co-op testing.

---

## 3. Four-Tier Testing Strategy

### Tier 1: Feature Coverage (>=5 tests per feature, 165+ tests)
Verifies the isolated primary behavior (happy path) for all 33 features:
- Standard inputs and expected state transitions.
- Functional validation of all rooms, props, items, monster states, network packets, and UI elements.

### Tier 2: Boundary & Corner Cases (>=5 tests per feature, 165+ tests)
Subjecting all 33 features to extreme, boundary, and invalid conditions:
- Numerical limits, non-finite values, out-of-bounds coordinates.
- Proximity thresholds (exact radius cutoffs for interactions and line-of-sight).
- Negative permissions (interacting without required prerequisites, keys, or fuses).
- Error handling, injection defense, and state integrity maintenance.

### Tier 3: Cross-Feature Combinations (>=15 tests)
Verifies complex multi-system synergies and interactions:
- Co-op synchronization: Shared keycards, simultaneous pickups, and inventory replication.
- Spatial dynamics: Closed doors registering dynamic collision obstacles that block monster Dijkstra search.
- Combat/Defense synergies: Locker hiding while monster patrols; camera + flashlight stun coordination.
- Map syncing: Custom prop editor mutations updating collision grids and invalidating pathfinding graphs in real-time.

### Tier 4: Real-World Application Scenarios (>=5 scenarios)
Complete end-to-end 0-30 minute simulated playthroughs exercising the entire game lifecycle:
1. **Scenario 1 — Complete 4-Player Co-op Broadcast & Escape**:
   - 4 players arrive at van -> search yard -> enter station -> retrieve Tape 1 -> collect fuses from Vent and Server rooms -> restore power -> collect Tapes 2 & 3 -> calibrate and broadcast -> survive Phase 3 nightmare chase -> extract at van.
2. **Scenario 2 — Solo Hardcore Infiltration & Locker Evasion**:
   - Single player navigating dark corridors -> evading monster via lockers -> managing sanity and sprint stamina -> stuns -> power restoration -> escape.
3. **Scenario 3 — Creative Mode Complete Safe Station Survey**:
   - Passive monster -> zero damage -> unlimited exploration of all 9 station sectors and courtyard -> puzzle completion without threat.
4. **Scenario 4 — Disaster & Teammate Revive Under Pressure**:
   - Downed player under monster attack -> teammate distraction -> medical revive -> temporary invulnerability sprint to safety.
5. **Scenario 5 — Host Disconnection & Dynamic Session Migration**:
   - Mid-mission host dropout -> socket reassignment -> session preservation -> quest continuation without data loss.

---

## 4. 33-Feature Mapping Matrix

| Feature # | Feature Name | Tier 1 Suite | Tier 2 Suite | Tier 3 Suite | Tier 4 Suite |
|---|---|---|---|---|---|
| F1 | Courtyard Guard Booth & Barrier | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 1 |
| F2 | Spotlight Towers & Cable Spools | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 3 |
| F3 | Barrels & Crates with Collisions | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 1, 2 |
| F4 | Perimeter Fence & Barbed Wire | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 1, 3 |
| F5 | Abandoned Transport in Courtyard | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 1, 2, 4 |
| F6 | Main Hall Turnstiles & Board of Honor | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 1, 2 |
| F7 | Broadcast Schedule Board & Fire Hose Panel | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 2 |
| F8 | Flickering Fluorescent Lights & Wire Trays | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 1, 2 |
| F9 | Debris, Trash Bins & Broken Crockery | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 3 |
| F10 | Archive Racks & Betacam/VHS Storage | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 1, 2 |
| F11 | Studio 04 Chromakey, Cameras & Mic Stands | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 1, 3 |
| F12 | Generator Room Diesel Engine & 380V Panel | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 1, 2, 4 |
| F13 | Ventilation Ducts, Grates & 90s Server Racks | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 1, 2 |
| F14 | Control Room Consoles & Video Monitors | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 1, 2 |
| F15 | Procedural Low-Poly PS1 Geometry & Palette | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 3 |
| F16 | Solid Collision Grid & Generator Obstacle Fix | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 1, 2 |
| F17 | 60 FPS Geometry Merging Optimization | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 3 |
| F18 | Builder & Models Preview Tool Support | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 3 |
| F19 | Replicated Door Locking States & Obstacles | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 1 |
| F20 | Guard & Engineer Keycards Network Sync | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 1 |
| F21 | Interactive SVG Station Map (`M`) Upgrade | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 2, 3 |
| F22 | Multiplayer Inventory & Quest Synchronization | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 1, 5 |
| F23 | Quest Stage 1: Recon & Access (0-8 min) | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 1, 2 |
| F24 | Quest Stage 2: Power Unit Startup (8-18 min) | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 1, 2, 4 |
| F25 | Quest Stage 3: Signal Calibration (18-25 min) | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 1, 2 |
| F26 | Quest Stage 4: Evacuation (25-30 min) | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 1, 2, 4 |
| F27 | Atmospheric Audio & Sound Effects | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 2, 4 |
| F28 | Monster Phase 1: Recon/Shadow (Power OFF) | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 1, 2 |
| F29 | Monster Phase 2: Active Hunt (Power ON) | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 1, 2, 4 |
| F30 | Monster Phase 3: Nightmare Pursuit | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 1, 2 |
| F31 | Monster Creative Mode Passivity | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 3 |
| F32 | End-to-End Opaque-box Test Suite Validation | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 1, 5 |
| F33 | Adversarial Coverage Hardening | ✅ 5 tests | ✅ 5 tests | ✅ Covered | ✅ Scenario 4, 5 |

---

## 5. Execution Instructions

### Running the E2E Test Suite
To run all 4 tiers of E2E tests:
```powershell
node --test tests/e2e/*.test.js
```

To run individual tiers:
```powershell
node --test tests/e2e/tier1-features.test.js
node --test tests/e2e/tier2-boundaries.test.js
node --test tests/e2e/tier3-interactions.test.js
node --test tests/e2e/tier4-scenarios.test.js
```

### Running All Project Tests
To run both unit/regression and E2E suites:
```powershell
npm test
node --test tests/e2e/*.test.js
```

### Success & Pass Criteria
- Exit code 0 across all test files.
- Zero assertion failures or uncaught exceptions.
- Test execution completes in < 15 seconds.
- 100% compliance with progressive testability and opaque-box contracts.
