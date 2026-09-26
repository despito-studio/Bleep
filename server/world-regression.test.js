import test from 'node:test';
import assert from 'node:assert/strict';
import * as THREE from 'three';
import { createWorld, createCharacter } from '../src/world.js';

// Canvas drawing is incidental to this test. Use real Three.js geometry,
// materials, scene graph and world update code without needing a GPU.
function canvasDocument() {
  return {
    createElement(tag) {
      assert.equal(tag, 'canvas');
      const context = new Proxy({
        getImageData: (_x, _y, width, height) => ({ data: new Uint8ClampedArray(width * height * 4) }),
        measureText: (text) => ({ width: String(text).length * 8 }),
        createLinearGradient: () => ({ addColorStop() {} }),
        createRadialGradient: () => ({ addColorStop() {} }),
      }, { get: (object, key) => object[key] ?? (() => {}) });
      return { width: 1, height: 1, getContext: () => context };
    },
  };
}

test('real station scene stays valid and updates through power, broadcast, escape and restart', () => {
  const priorDocument = globalThis.document;
  globalThis.document = canvasDocument();
  try {
    const scene = new THREE.Scene();
    const world = createWorld(scene);
    const normal = createCharacter('#272c24', true, false);
    const nightmare = createCharacter('#4a1815', true, true);
    scene.add(normal, nightmare);
    const initial = { tapes: [], fuses: [], power: false, broadcast: false, flicker: true };
    const powered = { ...initial, tapes: ['tape-1', 'tape-2', 'tape-3'], fuses: ['fuse-1', 'fuse-2'], power: true };
    const broadcasting = { ...powered, broadcast: true };
    for (const state of [initial, powered, broadcasting, initial]) {
      assert.doesNotThrow(() => world.setState(state), `world state broadcast=${state.broadcast}`);
      for (let frame = 0; frame < 60; frame++) {
        assert.doesNotThrow(() => world.update(frame / 60, 1 / 60, state));
        normal.userData.animate(frame / 60, true);
        nightmare.userData.animate(frame / 60, true);
      }
    }
    let meshCount = 0;
    scene.traverse((object) => {
      if (!object.isMesh) return;
      meshCount++;
      for (const material of Array.isArray(object.material) ? object.material : [object.material]) {
        assert.equal(material?.isMaterial, true, `mesh ${object.name || object.uuid} must have a Three.js material`);
      }
      const position = object.geometry.getAttribute('position');
      if (position) for (const value of position.array) assert.ok(Number.isFinite(value), `mesh ${object.name || object.uuid} has finite geometry`);
    });
    assert.ok(meshCount > 30, 'exercise the actual constructed station and animated characters');
  } finally {
    if (priorDocument === undefined) delete globalThis.document;
    else globalThis.document = priorDocument;
  }
});
