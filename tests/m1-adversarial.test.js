import test from 'node:test';
import assert from 'node:assert/strict';
import {
  rooms,
  courtyard,
  activeObstacles,
  exteriorObstacles,
  isObstacle,
  isWalkable,
  isInsideWorld,
  propToObstacle,
  updateObstacles
} from '../shared/world-data.js';
import { DEFAULT_PROPS } from '../shared/props.js';
import { segmentWalkable, nextWaypoint } from '../server/game.js';

test('M1-ADV-1: Prop obstacle registration and walkability rejection across all prop categories', () => {
  // Reset obstacles to default
  updateObstacles(DEFAULT_PROPS);

  // 1. Generator
  const genProp = DEFAULT_PROPS.find(p => p.id === 'diesel_gen_1');
  assert.ok(genProp, 'diesel_gen_1 must exist in DEFAULT_PROPS');
  assert.equal(isWalkable(genProp.x, genProp.z), false, 'diesel generator center must not be walkable');
  assert.equal(isWalkable(genProp.x + 0.3, genProp.z + 0.3), false, 'generator interior must not be walkable');
  assert.equal(isObstacle(genProp.x, genProp.z), true, 'generator center is registered obstacle');

  // 2. Crates (Indoor & Yard)
  const indoorCrate = DEFAULT_PROPS.find(p => p.id === 'crate_1');
  assert.ok(indoorCrate, 'crate_1 must exist in DEFAULT_PROPS');
  assert.equal(isWalkable(indoorCrate.x, indoorCrate.z), false, 'indoor crate center must not be walkable');
  assert.equal(isObstacle(indoorCrate.x, indoorCrate.z), true, 'indoor crate is registered obstacle');

  const yardCrate = DEFAULT_PROPS.find(p => p.id === 'crate_yard_1');
  assert.ok(yardCrate, 'crate_yard_1 must exist in DEFAULT_PROPS');
  assert.equal(isWalkable(yardCrate.x, yardCrate.z), false, 'yard crate center must not be walkable');
  assert.equal(isObstacle(yardCrate.x, yardCrate.z), true, 'yard crate is registered obstacle');

  // 3. Barrels (Indoor & Yard)
  const indoorBarrel = DEFAULT_PROPS.find(p => p.id === 'barrel_1');
  assert.ok(indoorBarrel, 'barrel_1 must exist in DEFAULT_PROPS');
  assert.equal(isWalkable(indoorBarrel.x, indoorBarrel.z), false, 'indoor barrel center must not be walkable');

  const yardBarrel = DEFAULT_PROPS.find(p => p.id === 'barrel_yard_1');
  assert.ok(yardBarrel, 'barrel_yard_1 must exist in DEFAULT_PROPS');
  assert.equal(isWalkable(yardBarrel.x, yardBarrel.z), false, 'yard barrel center must not be walkable');

  // 4. Vehicles (Courtyard abandoned car & exterior crew van)
  const carProp = DEFAULT_PROPS.find(p => p.id === 'wreckage_truck');
  assert.ok(carProp, 'wreckage_truck must exist in DEFAULT_PROPS');
  assert.equal(isWalkable(carProp.x, carProp.z), false, 'abandoned car center must not be walkable');
  assert.equal(isObstacle(carProp.x, carProp.z), true, 'abandoned car must be registered obstacle');

  const crewVan = exteriorObstacles.find(o => o.id === 'crew-van');
  assert.ok(crewVan, 'crew-van exterior obstacle must exist');
  const crewVanMidX = (crewVan.minX + crewVan.maxX) / 2;
  const crewVanMidZ = (crewVan.minZ + crewVan.maxZ) / 2;
  assert.equal(isWalkable(crewVanMidX, crewVanMidZ), false, 'crew van center must not be walkable');

  // 5. Guard booth
  const boothProp = DEFAULT_PROPS.find(p => p.id === 'guard_booth_1');
  assert.ok(boothProp, 'guard_booth_1 must exist in DEFAULT_PROPS');
  assert.equal(isWalkable(boothProp.x, boothProp.z), false, 'guard booth center must not be walkable');
  assert.equal(isObstacle(boothProp.x, boothProp.z), true, 'guard booth is registered obstacle');

  // 6. Server Racks
  for (let i = 1; i <= 6; i++) {
    const rack = DEFAULT_PROPS.find(p => p.id === `rack_${i}`);
    assert.ok(rack, `rack_${i} must exist in DEFAULT_PROPS`);
    assert.equal(isWalkable(rack.x, rack.z), false, `rack_${i} center must not be walkable`);
    assert.equal(isObstacle(rack.x, rack.z), true, `rack_${i} is registered obstacle`);
  }

  // 7. Barrier gate, Floodlight towers, Cable spools
  const barrier = DEFAULT_PROPS.find(p => p.id === 'barrier_gate_1');
  assert.ok(barrier, 'barrier_gate_1 must exist');
  assert.equal(isWalkable(barrier.x, barrier.z), false, 'barrier center must not be walkable');

  const tower = DEFAULT_PROPS.find(p => p.id === 'tower_nw');
  assert.ok(tower, 'tower_nw must exist');
  assert.equal(isWalkable(tower.x, tower.z), false, 'floodlight tower center must not be walkable');

  const spool = DEFAULT_PROPS.find(p => p.id === 'spool_1');
  assert.ok(spool, 'spool_1 must exist');
  assert.equal(isWalkable(spool.x, spool.z), false, 'cable spool center must not be walkable');
});

test('M1-ADV-2: Point (-8, -19) remains strictly walkable and monster pathfinding succeeds', () => {
  updateObstacles(DEFAULT_PROPS);

  const p = { x: -8, z: -19 };
  assert.equal(isWalkable(p.x, p.z, 0.22), true, 'point (-8, -19) must be walkable with radius 0.22');
  assert.equal(isObstacle(p.x, p.z, 0.22), false, 'point (-8, -19) must not collide with any obstacle');
  assert.equal(isInsideWorld(p.x, p.z, 0.22), true, 'point (-8, -19) must be inside world');

  // Test creature route from (-8, -19) to (8, -3) across doorways
  const target = { x: 8, z: -3 };
  assert.equal(segmentWalkable(p, target), false, 'direct ray through walls to (8, -3) must be blocked');

  // Navigation step test via nextWaypoint
  const initialWp = nextWaypoint(p, target);
  assert.ok(initialWp, 'nextWaypoint from start must exist');

  // Step-by-step navigation simulation along the path
  let current = { ...p };
  for (let step = 0; step < 1000 && Math.hypot(current.x - target.x, current.z - target.z) > 0.2; step++) {
    const nextWp = nextWaypoint(current, target);
    const dist = Math.hypot(nextWp.x - current.x, nextWp.z - current.z);
    assert.ok(dist > 0, 'waypoint distance must be positive');
    const stepSize = Math.min(dist, 0.15);
    const next = {
      x: current.x + (nextWp.x - current.x) / dist * stepSize,
      z: current.z + (nextWp.z - current.z) / dist * stepSize,
    };
    assert.ok(segmentWalkable(current, next), `step ${step} segmentWalkable from (${current.x.toFixed(2)}, ${current.z.toFixed(2)}) to (${next.x.toFixed(2)}, ${next.z.toFixed(2)})`);
    assert.ok(isWalkable(next.x, next.z), `step ${step} next point must be walkable`);
    current = next;
  }
  assert.ok(Math.hypot(current.x - target.x, current.z - target.z) <= 0.2, 'monster successfully reached goal');

  // Stress test clearance in neighborhood around (-8, -19)
  const angles = [0, Math.PI / 4, Math.PI / 2, (3 * Math.PI) / 4, Math.PI, (5 * Math.PI) / 4, (3 * Math.PI) / 2, (7 * Math.PI) / 4];
  for (const angle of angles) {
    // 0.25m steps in open directions towards doorway (z > -19)
    if (Math.sin(angle) >= 0) {
      const neighbor = { x: p.x + 0.25 * Math.cos(angle), z: p.y ? p.y : p.z + 0.25 * Math.sin(angle) };
      assert.equal(isWalkable(neighbor.x, neighbor.z, 0.22), true, `point at angle ${angle} from (-8, -19) must be walkable`);
    }
  }
});

test('M1-ADV-3: Corridor path (0, 4) -> (0, -4) through turnstiles remains unobstructed in center aisle', () => {
  updateObstacles(DEFAULT_PROPS);

  const start = { x: 0, z: 4 };
  const end = { x: 0, z: -4 };

  // Center line traversal
  assert.equal(segmentWalkable(start, end, 0.22), true, 'straight path (0, 4) -> (0, -4) must be walkable');

  // Center aisle lateral offsets (|x| <= 0.2)
  for (const xOffset of [-0.2, -0.1, 0.0, 0.1, 0.2]) {
    const s = { x: xOffset, z: 4 };
    const e = { x: xOffset, z: -4 };
    assert.equal(segmentWalkable(s, e, 0.22), true, `corridor segment at lateral offset x=${xOffset} must be walkable`);
  }

  // Verify turnstile pedestals physically block lateral paths (|x| >= 0.6)
  const turnstileProp = DEFAULT_PROPS.find(p => p.type === 'turnstile');
  assert.ok(turnstileProp, 'turnstile prop must exist');

  // Left pedestal: x in [-1.4, -0.55], z in [p.z - 0.35, p.z + 0.35]
  const leftPedestalX = turnstileProp.x - 0.975;
  const rightPedestalX = turnstileProp.x + 0.975;
  const pedestalZ = turnstileProp.z;

  assert.equal(isWalkable(leftPedestalX, pedestalZ, 0.22), false, 'left turnstile pedestal must be impassable');
  assert.equal(isWalkable(rightPedestalX, pedestalZ, 0.22), false, 'right turnstile pedestal must be impassable');

  // Rays trying to penetrate pedestals
  assert.equal(
    segmentWalkable({ x: leftPedestalX, z: pedestalZ + 1.5 }, { x: leftPedestalX, z: pedestalZ - 1.5 }, 0.22),
    false,
    'trajectory through left turnstile pedestal must be blocked'
  );
  assert.equal(
    segmentWalkable({ x: rightPedestalX, z: pedestalZ + 1.5 }, { x: rightPedestalX, z: pedestalZ - 1.5 }, 0.22),
    false,
    'trajectory through right turnstile pedestal must be blocked'
  );
});

test('M1-ADV-4: segmentWalkable correctly detects ray intersections with prop obstacles', () => {
  updateObstacles(DEFAULT_PROPS);

  // 1. Ray through Diesel Generator
  const genProp = DEFAULT_PROPS.find(p => p.id === 'diesel_gen_1');
  const genRayStart = { x: genProp.x, z: genProp.z + 2.0 };
  const genRayEnd = { x: genProp.x, z: genProp.z - 2.0 };
  assert.equal(segmentWalkable(genRayStart, genRayEnd, 0.22), false, 'ray through generator must be blocked');

  // Diagonal ray through generator
  assert.equal(
    segmentWalkable({ x: genProp.x - 2.0, z: genProp.z - 1.5 }, { x: genProp.x + 2.0, z: genProp.z + 1.5 }, 0.22),
    false,
    'diagonal ray through generator must be blocked'
  );

  // 2. Ray through Guard Booth in Courtyard
  const boothProp = DEFAULT_PROPS.find(p => p.id === 'guard_booth_1');
  assert.equal(
    segmentWalkable({ x: boothProp.x - 2.0, z: boothProp.z }, { x: boothProp.x + 2.0, z: boothProp.z }, 0.22),
    false,
    'ray through guard booth must be blocked'
  );

  // 3. Ray through Abandoned Car in Courtyard
  const carProp = DEFAULT_PROPS.find(p => p.id === 'wreckage_truck');
  assert.equal(
    segmentWalkable({ x: carProp.x, z: carProp.z - 3.0 }, { x: carProp.x, z: carProp.z + 3.0 }, 0.22),
    false,
    'ray through abandoned car must be blocked'
  );

  // 4. Ray through Server Racks
  const rack = DEFAULT_PROPS.find(p => p.id === 'rack_1');
  assert.equal(
    segmentWalkable({ x: rack.x - 1.0, z: rack.z }, { x: rack.x + 1.0, z: rack.z }, 0.22),
    false,
    'ray through server rack must be blocked'
  );

  // 5. Ray through Crate
  const crate = DEFAULT_PROPS.find(p => p.id === 'crate_1');
  assert.equal(
    segmentWalkable({ x: crate.x - 1.0, z: crate.z }, { x: crate.x + 1.0, z: crate.z }, 0.22),
    false,
    'ray through crate must be blocked'
  );

  // 6. Ray through Barrel
  const barrel = DEFAULT_PROPS.find(p => p.id === 'barrel_1');
  assert.equal(
    segmentWalkable({ x: barrel.x - 1.0, z: barrel.z }, { x: barrel.x + 1.0, z: barrel.z }, 0.22),
    false,
    'ray through barrel must be blocked'
  );

  // 7. Ray through Barrier Gate
  const barrier = DEFAULT_PROPS.find(p => p.id === 'barrier_gate_1');
  assert.equal(
    segmentWalkable({ x: barrier.x, z: barrier.z - 1.0 }, { x: barrier.x, z: barrier.z + 1.0 }, 0.22),
    false,
    'ray through barrier gate must be blocked'
  );

  // 8. Ray with checkObstacles = false should ignore props but respect walls
  assert.equal(
    segmentWalkable(genRayStart, genRayEnd, 0.05, false),
    true,
    'ray through generator with checkObstacles=false should succeed inside the room'
  );

  // 9. Ray through wall should still fail even with checkObstacles=false
  assert.equal(
    segmentWalkable({ x: 0, z: -10 }, { x: -6, z: -10 }, 0.05, false),
    false,
    'ray across room wall must fail even when checkObstacles=false'
  );
});

test('M1-ADV-5: Boundary conditions, invalid values, and corner grazing stress tests', () => {
  updateObstacles(DEFAULT_PROPS);

  // Invalid numbers
  assert.equal(segmentWalkable({ x: NaN, z: 0 }, { x: 0, z: 0 }), false, 'NaN x rejects');
  assert.equal(segmentWalkable({ x: 0, z: Infinity }, { x: 0, z: 0 }), false, 'Infinity z rejects');
  assert.equal(segmentWalkable({ x: 0, z: 0 }, { x: 0, z: 0 }, -0.1), false, 'negative radius rejects');
  assert.equal(isWalkable(NaN, 0), false, 'NaN isWalkable rejects');
  assert.equal(isWalkable(0, Infinity), false, 'Infinity isWalkable rejects');

  // Out of bounds / void queries
  assert.equal(isWalkable(100, 100), false, 'outside world isWalkable rejects');
  assert.equal(isWalkable(-50, -50), false, 'outside world isWalkable rejects');
  assert.equal(segmentWalkable({ x: 100, z: 100 }, { x: 101, z: 101 }), false, 'out-of-bounds ray rejects');

  // Identical start & end point
  assert.equal(segmentWalkable({ x: 0, z: 0 }, { x: 0, z: 0 }, 0.22), true, 'point ray at valid location passes');
  const genProp = DEFAULT_PROPS.find(p => p.id === 'diesel_gen_1');
  assert.equal(segmentWalkable({ x: genProp.x, z: genProp.z }, { x: genProp.x, z: genProp.z }, 0.22), false, 'point ray inside obstacle fails');

  // Grazing edge tests for obstacles
  for (const obs of activeObstacles) {
    // Center must always be blocked
    const midX = (obs.minX + obs.maxX) / 2;
    const midZ = (obs.minZ + obs.maxZ) / 2;
    assert.equal(isObstacle(midX, midZ, 0.0), true, `obstacle ${obs.id} center must be an obstacle`);

    // Crossing ray through midX, midZ from minX-0.5 to maxX+0.5
    const rayA = { x: obs.minX - 0.5, z: midZ };
    const rayB = { x: obs.maxX + 0.5, z: midZ };
    assert.equal(segmentWalkable(rayA, rayB, 0.05), false, `segmentWalkable across ${obs.id} must be blocked`);
  }
});
