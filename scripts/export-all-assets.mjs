import fs from 'node:fs';
import path from 'node:path';
import * as THREE from 'three';

// 1. Mock minimal canvas & document for headless Three.js procedural texture generation
function canvasDocument() {
  return {
    createElement: () => {
      const context = new Proxy({
        createLinearGradient: () => ({ addColorStop() {} }),
        createRadialGradient: () => ({ addColorStop() {} }),
        getImageData: () => ({ data: new Uint8ClampedArray(128 * 128 * 4) }),
      }, { get: (object, key) => object[key] ?? (() => {}) });
      return { width: 128, height: 128, getContext: () => context };
    },
  };
}
globalThis.document = canvasDocument();

// 2. Import exporters and project models
const { OBJExporter } = await import('three/addons/exporters/OBJExporter.js');
const { createCharacter, createProp, createWorld } = await import('../src/world.js');
const { createExterior, createVan } = await import('../src/exterior.js');

const forUnityProject = process.argv.includes('--unity-project');
const outputDir = forUnityProject
  ? path.resolve('DeadAirUnityProject/Assets/Resources/Models')
  : path.resolve('unity_assets');
fs.mkdirSync(outputDir, { recursive: true });

const exporter = new OBJExporter();

console.log('🚀 Starting batch export of all Dead Air // Bureau-9 3D assets to Unity format...');

const assets = [
  // Characters
  { name: 'monster_the_anchor', get: () => createCharacter('#272c24', true, false) },
  { name: 'monster_bloody_anchor', get: () => createCharacter('#4a1815', true, true) },
  { name: 'character_engineer_victor', get: () => createCharacter('#1e423f', false, false, 'engineer') },
  { name: 'character_operator_vivian', get: () => createCharacter('#4a1824', false, false, 'operator') },
  { name: 'character_journalist_edgar', get: () => createCharacter('#242726', false, false, 'journalist') },
  { name: 'character_dispatcher_hazel', get: () => createCharacter('#d95328', false, false, 'dispatcher') },

  // Transport & Environments
  { name: 'bureau9_recon_van', get: () => {
    const root = new THREE.Group();
    const v = createVan(root);
    v.door.position.set(1.235, 0, 1.64);
    return root;
  }},
  { name: 'station_facade_and_mast', get: () => {
    const scene = new THREE.Scene();
    const ext = createExterior(scene);
    ext.setArrival(1);
    return ext.root;
  }},
  { name: 'station_interior_complex', get: () => {
    const scene = new THREE.Scene();
    const w = createWorld(scene);
    return w.root;
  }},

  // Props & Equipment
  { name: 'prop_tk04_studio_camera', get: () => createProp('camera') },
  { name: 'prop_arthur_cole_cassette', get: () => createProp('tape') },
  { name: 'prop_ceramic_fuse_380v', get: () => createProp('fuse') },
  { name: 'prop_crt_broadcast_monitor', get: () => createProp('monitor') },
  { name: 'prop_checkpoint_guard_booth', get: () => createProp('guardBooth') },
  { name: 'prop_gate_barrier', get: () => createProp('barrier') },
  { name: 'prop_floodlight_tower', get: () => createProp('floodlightTower') },
  { name: 'prop_high_voltage_cable_spool', get: () => createProp('cableSpool') },
  { name: 'prop_abandoned_gaz66_truck', get: () => createProp('abandonedCar') },
  { name: 'prop_security_turnstile', get: () => createProp('turnstile') },
  { name: 'prop_archivist_banker_desk', get: () => createProp('archivistDesk') },
  { name: 'prop_studio04_anchor_desk', get: () => createProp('anchorDesk') },
  { name: 'prop_diesel_generator_dg200', get: () => createProp('dieselGenerator') },
  { name: 'prop_high_voltage_fuse_panel', get: () => createProp('fusePanel') },
  { name: 'prop_mixing_console_96mhz', get: () => createProp('mixingConsole') },
  { name: 'prop_mainframe_server_rack_90s', get: () => createProp('serverRack') },
  { name: 'prop_archive_card_catalog', get: () => createProp('cardCatalog') },
  { name: 'prop_ventilation_duct', get: () => createProp('airDuct') },
  { name: 'prop_3x3_crt_monitor_wall', get: () => createProp('monitorWall') },
  { name: 'prop_fire_safety_board', get: () => createProp('fireBoard') },
];

let totalBytes = 0;
for (let i = 0; i < assets.length; i++) {
  const item = assets[i];
  try {
    const mesh = item.get();
    // OBJExporter consumes matrixWorld; update parents and every child first.
    mesh.updateMatrixWorld(true);
    const materials = [];
    if (forUnityProject) {
      mesh.traverse((part) => {
        if (!part.isMesh || !part.material) return;
        const source = Array.isArray(part.material) ? part.material[0] : part.material;
        const name = `DA_${String(materials.length).padStart(4, '0')}`;
        source.name = name;
        part.name = `${item.name}_${name}`;
        materials.push({ name, source });
      });
    }
    const objText = `${forUnityProject ? `mtllib ${item.name}.mtl\n` : ''}${exporter.parse(mesh)}`;
    const filePath = path.join(outputDir, `${item.name}.obj`);
    fs.writeFileSync(filePath, objText, 'utf8');
    if (forUnityProject) {
      const mtl = materials.map(({ name, source }) => {
        const color = source.color || new THREE.Color(0xffffff);
        const emissive = source.emissive || new THREE.Color(0x000000);
        return [
          `newmtl ${name}`,
          `Ka ${color.r.toFixed(5)} ${color.g.toFixed(5)} ${color.b.toFixed(5)}`,
          `Kd ${color.r.toFixed(5)} ${color.g.toFixed(5)} ${color.b.toFixed(5)}`,
          `Ke ${emissive.r.toFixed(5)} ${emissive.g.toFixed(5)} ${emissive.b.toFixed(5)}`,
          'Ks 0.12 0.12 0.12', 'Ns 18',
          `d ${(source.opacity ?? 1).toFixed(3)}`, 'illum 2', '',
        ].join('\n');
      }).join('\n');
      fs.writeFileSync(path.join(outputDir, `${item.name}.mtl`), mtl);
    }
    totalBytes += Buffer.byteLength(objText, 'utf8');
    console.log(`[${i + 1}/${assets.length}] ✓ Exported ${item.name}.obj (${(objText.length / 1024).toFixed(1)} KB)`);
  } catch (err) {
    console.error(`[${i + 1}/${assets.length}] ✗ Failed exporting ${item.name}:`, err.message);
  }
}

console.log(`\n🎉 BATCH EXPORT COMPLETE!`);
console.log(`📁 Target directory: ${outputDir}`);
console.log(`📊 Exported ${assets.length} models, total size: ${(totalBytes / (1024 * 1024)).toFixed(2)} MB`);
console.log(`ℹ️ These .obj files are ready to be drag-and-dropped into your Unity project's 'Assets/' folder!`);
