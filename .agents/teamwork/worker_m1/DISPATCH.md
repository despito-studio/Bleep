## 2026-09-26T08:19:27Z
You are Worker M1 (Level Design, 3D Atmospheric Props & Physical Collisions).
Your working directory is: C:\Users\noona\Documents\ChatGPT\Test\.agents\teamwork\worker_m1

MANDATORY FIRST STEP: Read the authoritative user request at:
C:\Users\noona\Documents\ChatGPT\Test\.agents\teamwork\ORIGINAL_REQUEST.md

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

Also read:
- Master Project Specification: C:\Users\noona\Documents\ChatGPT\Test\.agents\teamwork\orchestrator_1\PROJECT.md
- Explorer 1 Survey Report: C:\Users\noona\Documents\ChatGPT\Test\.agents\teamwork\explorer_survey_1\handoff.md

Your exclusive write files:
- shared/props.js
- shared/world-data.js (updating propToObstacle and obstacle bounding boxes)
- src/world.js
- src/exterior.js
- src/builder.js
- builder.html
- src/models-preview.js
- models-preview.html
- shared/custom-map.json

Your Scope (Milestone 1 — Features 1 to 18):
1. **Procedural PS1 Props & Level Design Overhaul (R1)**:
   - Courtyard: Add Checkpoint/Guard booth (`guard-booth`), barrier gate, floodlight towers (`spotlight-tower`), cable spools, scattered barrels (`barrel`), crates (`crate`), barbed wire perimeter fence, abandoned transport vehicle.
   - Lobby & Corridor: Entrance turnstiles, employee board of honor, broadcast schedule wall board, fire hose panel, flickering fluorescent lights with 50Hz audio hum, ceiling cable trays, trash bins, broken crockery debris.
   - Service Rooms & Studios:
     - Archive: High industrial metal shelving, Betacam/VHS tape boxes, catalog cabinets, archivist desk with lamp.
     - Studio 04: Green chromakey backdrop, heavy studio cameras on tripods, mic stands, anchor desk, ceiling spotlights.
     - Generator Room: Massive diesel generator, fuel line pipes & valves, diesel canisters, 380V panel with ceramic fuse slots.
     - Vent Room & Server: Air ducts, ventilation grates, 90s server towers with blinking green/amber LEDs, punch cards, cable bundles.
     - Apparatus & Control: Broadcast consoles with audio faders, oscilloscopes, CCTV monitor banks, soundproofing acoustic wall panels.
2. **Solid Collisions (R1 Acceptance Criteria)**:
   - Update `propToObstacle()` in `shared/world-data.js` so ALL new and existing props (barrels, crates, generator, vehicles, fences, etc.) have impassable bounding boxes in `activeObstacles`.
   - Ensure player and monster cannot walk through props or the generator.
   - CRITICAL: Position generator bounding box carefully so existing `server/game.test.js` navigation test at `(-8, -19)` remains valid and passing!
3. **60 FPS Performance Optimization**:
   - Use static geometry batching (`mergeGeometries` by material UUID) in `src/world.js` and `src/exterior.js` so draw calls remain low (~10-15 calls).
   - Reuse palette standard materials (128x128 nearest-filtered procedural canvas textures).
4. **Tool Support (R4)**:
   - Add all 18+ new prop types to `PROP_CATALOG` in `shared/props.js`.
   - Update `src/builder.js` / `builder.html` and `src/models-preview.js` / `models-preview.html` so all new props can be placed in the editor and viewed in the 3D gallery.
5. **Verification**:
   - Run `npm test` and verify ALL tests pass with exit code 0.
   - Run `npm run build` and verify Vite build succeeds with exit code 0.
