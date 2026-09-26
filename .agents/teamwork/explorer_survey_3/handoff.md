# Handoff Report: Monster AI, 3-Phase Threat Architecture, Multiplayer Sync, Audio & Build Infrastructure

**Agent:** Explorer 3 (`explorer_survey_3`)  
**Mission:** Investigate existing monster AI logic (The Anchor / Announcer), audio systems, hiding mechanics, multiplayer network sync, and build/test infrastructure to map requirements for R3 revamp, multi-phase threat progression, and Creative mode.  
**Working Directory:** `C:\Users\noona\Documents\ChatGPT\Test\.agents\teamwork\explorer_survey_3`  
**Date:** 2026-09-26  

---

## 1. Observation

### 1.1 Monster AI Architecture (The Anchor / Announcer / Dictor)
* **Authoritative Location:** Server simulation in `server/game.js` (lines 627–688).
* **AI States & Properties:**
  * Initial room enemy definition (`server/game.js:261`):  
    `enemy: { x: 0, z: -48, active: false, stunned: false }`
  * Reset state (`server/game.js:313`):  
    `room.enemy = { x: 0, z: -48, active: false, stunned: false, heardRadio: null };`
  * Activation trigger (`server/game.js:521-525, 576-583`):  
    Currently activates either when `spawnCountdown` (35s) reaches 0 OR as soon as `tape-1` is picked up:
    ```javascript
    // Taking a tape instantly breaks the calm and accelerates spawn timer
    if (!room.creative) {
      if (room.spawnCountdown > 3) room.spawnCountdown = 3;
      room.enemy.active = true;
    }
    ```
  * Stun Condition (`server/game.js:634–642`):  
    Aiming at the creature with both flashlight and camera active within 9 meters, dot product > 0.9, and unobstructed line-of-sight:
    ```javascript
    enemy.stunned = activeStunners.some((player) => {
      if (!player.camera || !player.flashlight) return false;
      const d = distance(player, enemy);
      if (d > 9 || d < 0.02) return false;
      const horizontalAim = ((enemy.x - player.x) * -Math.sin(player.yaw) + (enemy.z - player.z) * -Math.cos(player.yaw));
      const heightDifference = -0.3;
      const dot = (horizontalAim * Math.cos(player.pitch) + heightDifference * Math.sin(player.pitch)) / Math.hypot(d, heightDifference);
      return dot > 0.9 && segmentWalkable(player, enemy, 0.05);
    });
    ```
  * Targeting & Navigation (`server/game.js:644–668`):
    * Target 1: `enemy.heardRadio` (if radio ping was within 18m and `room.elapsed < heardRadio.until`).
    * Target 2: Nearest visible, non-hiding living player (`visibleTargets`).
    * Target 3: If all players are hiding or down, cycles 4 static patrol points along the central corridor: `[{ x: 0, z: -2 }, { x: 0, z: -17 }, { x: 0, z: -32 }, { x: 0, z: -47 }]` every 6 seconds.
  * Speed (`server/game.js:662`):
    ```javascript
    const speed = room.broadcast ? 3.2 : 0.85 + room.tapes.size * 0.28;
    ```
    * 0 tapes: 0.85 m/s.
    * 1 tape: 1.13 m/s.
    * 2 tapes: 1.41 m/s.
    * 3 tapes: 1.69 m/s.
    * Post-broadcast nightmare: 3.2 m/s.
    * (For comparison: player walking is 2.6 m/s, sprint is 4.5 m/s or 5.175 m/s for journalist).
  * Attacks & Damage (`server/game.js:670–688`):
    * Proximity < 1.1m while unstunned accumulates `player.contact += dt`.
    * At `player.contact >= 2.0` (2s contact), player is downed (`player.down = true`), flashlight and camera are turned off, and team defeat is evaluated (`checkDefeat(room)`).
    * Revive mechanic (`server/game.js:493–506`): Teammate within 2m can interact with `revive:${target.id}`, resetting `down = false`, restoring `sanity = 40`, and granting 4s invulnerability.
  * Pathfinding Engine (`server/game.js:10–37, 49–159`):
    * Built on a 32-node visibility graph (`WAYPOINTS`) covering corridor, 8 room portals, side loop tunnels, and courtyard extraction path.
    * Dynamic obstacle clipping via `segmentWalkable(a, b, radius)` and Dijkstra search in `nextWaypoint(from, to)`.
    * Invalidation cache `navigationCache` keyed to `JSON.stringify(activeObstacles)`.

### 1.2 3D Monster Models & Animation
* **File:** `src/world.js` (lines 487–548) and `src/models-preview.js` (lines 13–14, 80–81).
* Proportions: 2.80m height, elongated asymmetric limbs, distorted head, claw hands.
* Bloody variant: Glowing red chest torso (`glow(0x780e0e, 1.2)`), glowing bloody claws, glowing red eyes (`glow(0xff1808, 3.5)` vs standard `0xb0cfaa, 1.2`).
* Animation: Head twitching frequency (`Math.sin(time * 19)`), limb oscillation at `speedMult = bloody ? 4.8 : 2.8`.
* Hallucination Phantom Model (`src/main.js:918–932`):
  * Spawns when `sanity < 40%` and `Math.random() < 0.005`, vanishes when approached (< 3m) or illuminated with flashlight + camera.

### 1.3 Audio System & Atmospheric Triggers
* **File:** `src/audio.js` (485 lines).
* Procedural Web Audio synthesizer (zero external audio asset downloads).
* Master compressor, convolver reverb impulse (1.4s decay), 50Hz electrical hum (scales with power from 0.7 to 1.45).
* Ambient air noise, radio bandpass noise, 71Hz dread sub-bass oscillator.
* Sanity-driven tinnitus oscillator at 5200Hz when sanity < 55%.
* 3D Spatial Audio:
  * Monster footsteps / bone clicks / radio static (`_monsterCue`) panned using relative angle `(dx*rightX + dz*rightZ) / dist` when monster is within 16m (`src/audio.js:272–276` and `src/main.js:956–964`).
* Sounds implemented: `click`, `step`, `pickup`, `power`, `error`, `scare`, `screamer`, `radio`, `locker`, `alarm`, `win`, `lose`.

### 1.4 Hiding Mechanics (Lockers & Wardrobes)
* **Files:** `shared/world-data.js` (lines 35–42), `server/game.js` (lines 374–386, 451–491), `src/main.js` (lines 635, 641–648, 691–711).
* Lockers defined: 6 default lockers positioned across Archive, Generator, Server, Corridor, Tech Storage, Dispatch.
* Safe entry/exit: Player enters locker (`distance < 2.2m`), camera and flashlight are forcefully disabled, sprint is cleared.
* Detection immunity: `visibleTargets = living.filter(p => !p.hiding)`. While inside a locker, creature cannot target, stun, or damage the player.
* Sanity recovery in locker: `player.sanity = Math.min(100, player.sanity + dt * 2.5)` (`server/game.js:601`).
* Audio muffling: `ambience` volume reduced to 35% while hiding (`src/audio.js:227`).
* Safe exit coordinates: Each locker defines explicit `exitX, exitZ` so player never clips into walls on exiting.

### 1.5 Multiplayer Networking & Room State Synchronization
* **Files:** `server/index.js` (179 lines) and `server/game.js`.
* Transport: Socket.IO over HTTP server, 12 Hz tick (`setInterval(() => game.tick(), 1000 / 12)`).
* Room lifecycle: 5-char code, 1–4 players, host migration on disconnect, solo mode pause enforcement.
* Replicated state schema (`GameManager.state(room)`):
  * `code`, `hostId`, `phase`, `solo`, `creative`, `paused`
  * `players`: array of `{ id, name, color, characterClass, x, z, yaw, pitch, flashlight, camera, sprinting, down, hiding, sanity }`
  * Objectives: `tapes` (Set), `fuses` (Set), `power` (boolean), `broadcast` (boolean)
  * Monster: `enemy: { x, z, active, stunned, bloody }`
  * Events: `radioTransmission`, `spawnCountdown`, `elapsed`, `scare`, `message`
* Network events:
  * Client -> Server: `room:create`, `room:join`, `room:leave`, `game:start`, `game:restart`, `game:pause`, `player:move`, `player:class`, `player:radio`, `game:interact`.
  * Server -> Client: `room:state` (emitted to all player sockets in the room on state change and tick).
* Movement security: Rate-budget check (`dt * 6.5 m/s` accumulator, max 2.5m buffer) and `segmentWalkable` collision verification. Rejects teleports and non-finite values.

### 1.6 Build Pipeline and Test Infrastructure
* **File:** `package.json`:
  * Scripts:
    * `"dev": "node server/index.js"`
    * `"build": "vite build"`
    * `"start": "node server/index.js --production"`
    * `"test": "node --test server/*.test.js"`
  * Dependencies: `express ^5.1.0`, `socket.io ^4.8.1`, `socket.io-client ^4.8.1`, `three ^0.180.0`, `vite ^7.1.7`. Type: `module` (ESM).
* **Test Suite Verification:**
  * Verified command: `npm test`
  * Result: **19 tests passed, 0 failed**, exit code 0.
  * Test files:
    1. `server/game.test.js` (15 tests: room caps, host migration, solo pause, authoritative movement, objective gates, creature navigation, camera+flashlight stun, co-op revives, locker hiding & radio pings, creative mode, live Socket.IO client co-op, character classes, arrival grace, complete round traversal to courtyard, custom map invalidation).
    2. `server/interface.test.js` (3 tests: interfaceMarkup control generation, modal dismiss handlers, style.css overlay z-index).
    3. `server/world-regression.test.js` (1 test: headless canvas shim, Three.js scene creation, character animation, finite geometry, mesh count > 30).
* **Build Verification:**
  * Verified command: `npm run build`
  * Result: **Vite 7.3.6 build succeeded with exit code 0** in 29.82s.
  * Outputs in `dist/`: `index.html`, `models-preview.html`, `builder.html`, and bundled JS/CSS chunks (`main`, `world`, `exterior`, `models`, `builder`, `three`).

---

## 2. Logic Chain & Gap Analysis for R3 & Quest Architecture

### 2.1 Gap Analysis: Monster Threat Evolution (R3) vs Current Code
1. **Premature Activation (Phase 1 Conflict):**
   * *Observation:* In `server/game.js:260, 521–525`, `spawnCountdown` is 35 seconds, and picking up `tape-1` instantly forces `room.enemy.active = true`.
   * *R3 Requirement:* In Phase 1 (Recon/Shadow, 0–8 min before power), the monster must remain sleeping/dormant/ambient. It should NOT actively pursue players down corridors.
   * *Deduction:* The monster activation must be de-coupled from `tape-1` and 35s timer, and instead tied directly to the **Generator Power Activation** (`power = true`).
2. **Missing Ambient / Ventilation Effects in Phase 1:**
   * *Observation:* `src/audio.js` has generic knocks and noise filters, but no dedicated ventilation crawling sounds, falling prop sound effects, or distant radio whisper triggers.
   * *R3 Requirement:* Phase 1 relies on acoustic dread: ventilation sounds, falling props, radio whispers, rare silhouettes in the darkness, teaching players to use lockers and the camera safely.
   * *Deduction:* Phase 1 needs timed ambient events:
     * Audio cues: Vent rattle / crawling sound (`_ventRattle`), falling prop clatter (`_propClatter`), radio static whisper (`_radioWhisper`).
     * Visuals: Distant shadow silhouette briefly flickering at the end of the long corridor or behind glass windows that vanishes when approached or lit by flashlight.
3. **Phase 2 (Hunting) Sensory Inputs:**
   * *Observation:* Current `updateEnemy()` only detects: (a) radio ping within 18m, (b) nearest non-hiding player, (c) stun via camera+flashlight. It does NOT react to sprinting noise or flashlight beam.
   * *R3 Requirement:* "Активно патрулює коридори, перевіряє кімнати, реагує на біг, світло ліхтаря та переговори по рації Q."
   * *Deduction:*
     * **Hearing Running:** When any player has `sprinting === true` within hearing radius (~14m) and is not hiding, update `enemy.heardNoise = { x: player.x, z: player.z, until: room.elapsed + 5 }`.
     * **Flashlight Detection:** If a player has `flashlight === true` in the same corridor/room within line of sight (even without camera stun), creature turns towards the light source if within 16m.
     * **Room Searches:** Instead of only patrolling 4 points on the corridor axis `x = 0`, creature's idle patrol should branch into room entry waypoints (e.g. Studio 04, Archive, Generator, Server) when no direct target is spotted.
4. **Phase 3 (Nightmare after Broadcast):**
   * *Observation:* The bloody model and 3.2 m/s speed are implemented in `server/game.js:662` and `src/main.js:908`.
   * *Deduction:*
     * Relentless chase needs to ensure pathfinding directly follows fleeing players out the front doors into the courtyard towards `objects.exit` (the van).
     * Camera stun during Phase 3 should either have reduced duration or require tighter focus, heightening the urgency of the final sprint.
5. **Creative Mode (Режим дослідження):**
   * *Observation:* Already implemented in `server/game.js:256, 596, 671` and `src/main.js:869, 873`.
   * *Deduction:* Creative mode is fully functional: monster stays passive, damage is disabled, sanity remains 100%, sprint stamina drain is removed. All new quest mechanics (keys, valves, fuel, calibration) must ensure they are accessible and testable under Creative mode.

### 2.2 Gap Analysis: Multiplayer State Synchronization (R4)
1. **Current Replicated State vs R2 Quest Objects:**
   * *Observation:* `server/game.js:205–208` only tracks `tapes` (Set), `fuses` (Set), `power` (boolean), `broadcast` (boolean).
   * *R2 & R4 Requirements:*
     * Stage 1: Keycards (Guard Keycard `keycard-guard` from courtyard guard shack / locker room, Engineer Keycard `keycard-engineer`).
     * Stage 2: Fuel canister (`fuel-canister`), emergency pressure valve (`valve-pressure` in vent room), 2 high-voltage fuses (`fuse-1`, `fuse-2`), generator starter.
     * Stage 3: Frequency calibration 96.4 MHz (`calibrated: boolean` or `frequency: number`).
     * Stage 4: Evacuation to van.
     * Door States: Station magnetic doors (`doors: { [id: string]: boolean }`).
   * *Deduction:*
     * Add `keycards: [...(room.keycards || [])]` to server room state.
     * Add `fuel: Boolean(room.fuel)`, `valve: Boolean(room.valve)`, `calibrated: Boolean(room.calibrated)` to server room state.
     * Add `doors: { ...room.doors }` to sync locked vs unlocked magnetic doors across all 1–4 clients.
     * Update `showMap()` in `src/main.js` to render live door lock states (red for locked, green for unlocked) and subsystem statuses.

### 2.3 Build and Testing Infrastructure Verification
* *Observation:* Node test runner runs with `node --test server/*.test.js`.
* *Deduction:*
  * All new test suites for Monster AI phases, keycard pickup, fuel/valve/generator sequencing, and door synchronization should be added either into `server/game.test.js` or separate files matching `server/*.test.js` (e.g. `server/quest.test.js`, `server/monster.test.js`).
  * Running `npm test` will automatically pick up and execute any new `server/*.test.js` file.

---

## 3. Caveats

1. **Headless Execution Environment:**
   * Automated tests run in Node.js where `window`, `document`, WebGL, and Web Audio are not natively present.
   * `server/world-regression.test.js` demonstrates the correct pattern: a lightweight canvas mock (`canvasDocument()`) allows verifying Three.js scene graphs, materials, and geometry without requiring GPU acceleration.
   * Any client-side tests must follow this pattern or be unit-tested via exported pure functions.
2. **Network Tick Rate vs Client Interpolation:**
   * Server simulation runs at 12 Hz (`dt = 0.083s`).
   * Client renders at 60 FPS (`requestAnimationFrame`) with vector lerping (`position.lerp(..., dt * 12)`).
   * Fast monster movements (3.2 m/s in Phase 3) require adequate waypoint density to prevent visually corner-cutting around doorways.
3. **Backward Compatibility:**
   * `builder.html` and `models-preview.html` rely on exports from `src/world.js` and `shared/props.js`. Any new prop types must be added to `PROP_CATALOG` and `propToObstacle()` to prevent breaking the editor or preview tools.

---

## 4. Conclusion & Recommended Implementation Plan

### 4.1 Monster Threat Progression (R3 Architecture)
Introduce an explicit 3-phase threat model inside `server/game.js`:

```
┌────────────────────────────────────────────────────────────────────────┐
│ Phase 1: RECON / SHADOW (0–8 min, Power OFF)                          │
│ • State: threatPhase = 'recon', enemy.active = false                   │
│ • Audio: Vent crawls, falling prop clatter, distant radio whispers     │
│ • Visuals: Rare corridor silhouettes; safe locker hiding practice      │
│ • Objective: Guard keycard → Engineer keycard → Tape 1 → Schematics    │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │ Generator Started (Fuel+Valve+Fuses)
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│ Phase 2: ACTIVE HUNTING (8–25 min, Power ON)                          │
│ • State: threatPhase = 'hunting', enemy.active = true                  │
│ • AI: Patrols corridors & searches inside rooms                        │
│ • Senses: Hearing sprint footsteps (14m), flashlight glare, radio Q    │
│ • Defense: Camera + Flashlight Stun (2-3s), Locker hiding             │
│ • Objective: Tapes 2 & 3 → Frequency 96.4 MHz calibration              │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │ Broadcast Started (96.4 MHz)
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│ Phase 3: NIGHTMARE ESCAPE (25–30 min, Broadcast ON)                   │
│ • State: threatPhase = 'nightmare', enemy.bloody = true, speed = 3.2   │
│ • AI: Relentless sprint through red emergency lights into courtyard    │
│ • Visuals: Bloody model, glowing red eyes, 4.8x twitch animation       │
│ • Objective: Sprint through front entrance to Bureau-9 van in yard     │
└────────────────────────────────────────────────────────────────────────┘
```

### 4.2 Multiplayer Sync Extensions (R4 Architecture)
Extend `server/game.js` room state:
1. `keycards`: `Set<string>` (e.g., `'guard'`, `'engineer'`).
2. `fuel`: `boolean` (found in courtyard shed / generator room).
3. `valve`: `boolean` (opened in vent room).
4. `doors`: `Record<string, boolean>` (`archiveDoor`, `studioDoor`, `generatorDoor`, `serverDoor`, `dispatchDoor`).
5. `calibration`: `{ frequency: number, complete: boolean }`.
6. Client HUD map (`showMap()` in `src/main.js`) dynamically renders door colors (red = locked, green = unlocked) and subsystem LEDs.

### 4.3 Test & Verification Expansion
* Add a dedicated test file `server/threat-evolution.test.js` or integrate into `server/game.test.js` to assert:
  * Creature remains inactive in Phase 1 before generator start.
  * Generator activation switches monster to Phase 2 active hunting.
  * Sprinting attracts creature within hearing range.
  * Broadcast switches monster to Phase 3 bloody nightmare sprint.
  * Creative mode remains completely passive and safe through all phases.

---

## 5. Verification Method

### 5.1 Test Execution
Run the complete Node test suite:
```powershell
npm test
```
*Expected Result:* All tests pass with code 0 (currently 19 tests, will expand with new quest and monster phase tests).

### 5.2 Build Execution
Run the Vite production bundler:
```powershell
npm run build
```
*Expected Result:* Clean compilation of `dist/index.html`, `dist/models-preview.html`, `dist/builder.html` and assets with code 0.

### 5.3 File Inspections
Inspect the core implementation files to verify layout compliance:
* Monster AI & Network State: `server/game.js`
* Server Socket Handlers & API: `server/index.js`
* Audio Synthesis Engine: `src/audio.js`
* Client Game Loop & Controls: `src/main.js`
* 3D Models & Scenery: `src/world.js`
* Map Layout & Physics Obstacles: `shared/world-data.js`
* Prop Definitions & Catalog: `shared/props.js`
* Unit & Regression Tests: `server/*.test.js`

### 5.4 Invalidation Conditions
This analysis is invalidated if:
1. `npm test` fails or exits with non-zero code.
2. `npm run build` fails or fails to bundle Three.js chunks.
3. Lockers or obstacles are placed without matching bounding boxes in `propToObstacle()`.
4. Monster pathfinding nodes (`WAYPOINTS`) fail to connect across doorways or the courtyard.
