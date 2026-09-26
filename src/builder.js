import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { DEFAULT_PROPS, PROP_CATALOG } from '../shared/props.js';
import { spawnPropMesh } from './world.js';
import { rooms } from '../shared/world-data.js';

const $ = id => document.getElementById(id);
const canvas = $('builder-canvas');
const viewportWrap = $('viewport-wrap');

let renderer, scene, camera, controls;
let propsList = [];
const meshMap = new Map(); // prop.id -> THREE.Group
let selectedProp = null;
let selectedMesh = null;
let selectionBox = null;
let isDragging = false;
const dragPlane = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0);
const raycaster = new THREE.Raycaster();
const mouse = new THREE.Vector2();
const dragOffset = new THREE.Vector3();
const intersectionPoint = new THREE.Vector3();

function toast(message, duration = 3500) {
  const el = $('toast');
  el.textContent = message;
  el.hidden = false;
  setTimeout(() => { el.hidden = true; }, duration);
}

// Initialize 3D Engine
function initEngine() {
  renderer = new THREE.WebGLRenderer({ canvas, antialias: true, powerPreference: 'high-performance' });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio, 1.5));
  renderer.setSize(viewportWrap.clientWidth, viewportWrap.clientHeight);
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.35;
  renderer.shadowMap.enabled = true;

  scene = new THREE.Scene();
  scene.background = new THREE.Color('#0c110d');

  camera = new THREE.PerspectiveCamera(45, viewportWrap.clientWidth / viewportWrap.clientHeight, 0.1, 300);
  camera.position.set(0, 38, 16);

  controls = new OrbitControls(camera, renderer.domElement);
  controls.enableDamping = true;
  controls.dampingFactor = 0.08;
  controls.target.set(0, 0, -24);
  controls.maxPolarAngle = Math.PI * 0.46; // keep slightly above floor
  controls.minDistance = 2;
  controls.maxDistance = 140;

  // Studio Lighting for Editor
  const ambient = new THREE.HemisphereLight(0xbad2bc, 0x242820, 1.4);
  scene.add(ambient);
  const dirLight = new THREE.DirectionalLight(0xffeedd, 2.2);
  dirLight.position.set(20, 45, 25);
  dirLight.castShadow = true;
  dirLight.shadow.mapSize.set(1024, 1024);
  scene.add(dirLight);

  const fillLight = new THREE.DirectionalLight(0x7da48d, 1.2);
  fillLight.position.set(-20, 30, -30);
  scene.add(fillLight);

  // Selection Wireframe Box
  const boxGeom = new THREE.BoxGeometry(1, 1, 1);
  const edges = new THREE.EdgesGeometry(boxGeom);
  selectionBox = new THREE.LineSegments(edges, new THREE.LineBasicMaterial({ color: 0xe6b82e, linewidth: 2 }));
  selectionBox.visible = false;
  scene.add(selectionBox);

  buildStationArchitecture();
  populateCatalogUI();
  setupEventListeners();
  loadMapData();

  window.addEventListener('resize', onResize);
  requestAnimationFrame(tick);
}

// Build Low-Cutaway Station Floor and Walls
function buildStationArchitecture() {
  const stationGroup = new THREE.Group();
  stationGroup.name = 'ARCHITECTURE';
  scene.add(stationGroup);

  // Station master floor
  const floorMat = new THREE.MeshStandardMaterial({ color: 0x222a24, roughness: 0.85 });
  const floor = new THREE.Mesh(new THREE.BoxGeometry(25.5, 0.2, 62), floorMat);
  floor.position.set(0, -0.1, -24);
  floor.receiveShadow = true;
  stationGroup.add(floor);

  // Grid Helper on floor
  const grid = new THREE.GridHelper(64, 64, 0x4a6345, 0x1f2b20);
  grid.position.set(0, 0.01, -24);
  stationGroup.add(grid);

  // Low-cutaway walls (1.2m height so interior is always visible from top-down!)
  const wallMat = new THREE.MeshStandardMaterial({ color: 0x3d4a3e, roughness: 0.9, transparent: true, opacity: 0.65 });
  const wallCapMat = new THREE.MeshBasicMaterial({ color: 0x5a755c });

  function wall(x, z, w, d) {
    const m = new THREE.Mesh(new THREE.BoxGeometry(w, 1.2, d), wallMat);
    m.position.set(x, 0.6, z);
    stationGroup.add(m);
    const cap = new THREE.Mesh(new THREE.BoxGeometry(w + 0.02, 0.05, d + 0.02), wallCapMat);
    cap.position.set(x, 1.22, z);
    stationGroup.add(cap);
  }

  // Outer perimeter walls with entrance doorway left open at x=0, z=6
  wall(-7.5, 6, 10.5, 0.3);
  wall(7.5, 6, 10.5, 0.3);
  wall(0, -54, 25.5, 0.3);
  wall(-12, -24, 0.3, 60);
  wall(12, -24, 0.3, 60);

  // Room dividing partition walls
  for (const side of [-1, 1]) {
    wall(side * 7.25, -9, 9.5, 0.25);
    wall(side * 5.875, -24, 6.75, 0.25);
    wall(side * 11.875, -24, 0.5, 0.25);
    wall(side * 7.25, -39, 9.5, 0.25);

    // Corridor longitudinal walls
    const cx = side * 2.5;
    wall(cx, 2.85, 0.2, 6.3);
    wall(cx, -9.0, 0.2, 12.6);
    wall(cx, -24.0, 0.2, 12.6);
    wall(cx, -39.0, 0.2, 12.6);
    wall(cx, -50.85, 0.2, 6.3);
  }

  // --- Courtyard (Exterior) Architecture ---
  const courtyardMat = new THREE.MeshStandardMaterial({ color: 0x161e18, roughness: 0.95 });
  const courtyardFloor = new THREE.Mesh(new THREE.BoxGeometry(44, 0.2, 30), courtyardMat);
  courtyardFloor.position.set(0, -0.1, 21);
  courtyardFloor.receiveShadow = true;
  stationGroup.add(courtyardFloor);

  const courtyardGrid = new THREE.GridHelper(44, 22, 0x2d3f2c, 0x141f16);
  courtyardGrid.position.set(0, 0.01, 21);
  stationGroup.add(courtyardGrid);

  // Perimeter Fences
  const fenceMat = new THREE.MeshStandardMaterial({ color: 0x415042, roughness: 0.7 });
  function fence(x, z, w, d) {
    const m = new THREE.Mesh(new THREE.BoxGeometry(w, 0.9, d), fenceMat);
    m.position.set(x, 0.45, z);
    stationGroup.add(m);
  }
  fence(-22, 21, 0.25, 30);
  fence(22, 21, 0.25, 30);
  fence(-16.2, 36, 11.6, 0.25);
  fence(9.2, 36, 25.6, 0.25); // Gate gap at x=-7, z=36

  // Van Marker & Collision Representation
  const vanMat = new THREE.MeshStandardMaterial({ color: 0x8a927e, roughness: 0.75 });
  const vanMesh = new THREE.Mesh(new THREE.BoxGeometry(2.3, 1.8, 5.0), vanMat);
  vanMesh.position.set(-7, 0.9, 23);
  stationGroup.add(vanMesh);

  // Service Shed (380V) Representation
  const shedMat = new THREE.MeshStandardMaterial({ color: 0x51634f, roughness: 0.85 });
  const shedMesh = new THREE.Mesh(new THREE.BoxGeometry(5.0, 2.2, 5.4), shedMat);
  shedMesh.position.set(18.3, 1.1, 11);
  stationGroup.add(shedMesh);

  // Room & Zone name badges on the floor
  const roomCenters = [
    { name: 'ДВІР / ЕВАКУАЦІЯ', x: 0, z: 21 },
    { name: 'ФУРГОН BUREAU–9', x: -7, z: 23 },
    { name: 'САРАЙ 380V', x: 18.3, z: 11 },
    { name: 'ГОЛОВНИЙ КОРИДОР', x: 0, z: -24 },
    { name: 'АРХІВ', x: -7.25, z: -1.5 },
    { name: 'АПАРАТНА', x: 7.25, z: -1.5 },
    { name: 'ГЕНЕРАТОРНА', x: -7.25, z: -16.5 },
    { name: 'СТУДІЯ 04', x: 7.25, z: -16.5 },
    { name: 'ВЕНТВУЗОЛ', x: -7.25, z: -31.5 },
    { name: 'СЕРВЕРНА', x: 7.25, z: -31.5 },
    { name: 'ТЕХСКЛАД', x: -7.25, z: -46.5 },
    { name: 'ДИСПЕТЧЕРСЬКА', x: 7.25, z: -46.5 },
  ];

  for (const r of roomCenters) {
    const c = document.createElement('canvas'); c.width = 256; c.height = 64;
    const ctx = c.getContext('2d');
    ctx.fillStyle = '#162118'; ctx.fillRect(0, 0, 256, 64);
    ctx.strokeStyle = '#415743'; ctx.lineWidth = 4; ctx.strokeRect(2, 2, 252, 60);
    ctx.fillStyle = '#b5cbb0'; ctx.font = 'bold 20px monospace'; ctx.textAlign = 'center'; ctx.textBaseline = 'middle';
    ctx.fillText(r.name, 128, 32);
    const tx = new THREE.CanvasTexture(c);
    const badge = new THREE.Mesh(new THREE.PlaneGeometry(3.6, 0.9), new THREE.MeshBasicMaterial({ map: tx, side: THREE.DoubleSide }));
    badge.rotation.x = -Math.PI / 2;
    badge.position.set(r.x, 0.02, r.z);
    stationGroup.add(badge);
  }
}

// Populate UI Palette
function populateCatalogUI() {
  const container = $('prop-catalog');
  container.innerHTML = '';
  for (const item of PROP_CATALOG) {
    const btn = document.createElement('button');
    btn.className = 'catalog-btn';
    btn.innerHTML = `<span class="icon">${item.icon}</span><span class="name">${item.name}</span>`;
    btn.onclick = () => addPropFromCatalog(item);
    container.appendChild(btn);
  }
}

// Load Map Data from /api/map
async function loadMapData() {
  try {
    const res = await fetch('/api/map');
    if (res.ok) {
      const data = await res.json();
      if (data.ok && Array.isArray(data.props)) {
        renderProps(data.props);
        toast(data.isCustom ? 'Завантажено вашу збережену карту.' : 'Завантажено стандартний макет станції.');
        return;
      }
    }
  } catch (e) {
    console.warn('Failed to fetch from /api/map, using defaults:', e);
  }
  renderProps(DEFAULT_PROPS);
}

// Render Props into Scene
function renderProps(props) {
  // Clear old meshes
  for (const mesh of meshMap.values()) {
    scene.remove(mesh);
  }
  meshMap.clear();
  propsList = JSON.parse(JSON.stringify(props));

  for (const prop of propsList) {
    const mesh = spawnPropMesh(scene, prop);
    if (mesh) {
      meshMap.set(prop.id, mesh);
    }
  }
  deselectProp();
}

// Select a prop
function selectProp(prop, mesh) {
  selectedProp = prop;
  selectedMesh = mesh;
  updateSelectionBox();
  updateInspectorUI();
}

function deselectProp() {
  selectedProp = null;
  selectedMesh = null;
  if (selectionBox) selectionBox.visible = false;
  updateInspectorUI();
}

function updateSelectionBox() {
  if (!selectedMesh || !selectionBox) {
    if (selectionBox) selectionBox.visible = false;
    return;
  }
  const aabb = new THREE.Box3().setFromObject(selectedMesh);
  const size = new THREE.Vector3();
  const center = new THREE.Vector3();
  aabb.getSize(size);
  aabb.getCenter(center);

  selectionBox.scale.set(size.x + 0.1, size.y + 0.1, size.z + 0.1);
  selectionBox.position.copy(center);
  selectionBox.visible = true;
}

// Update Inspector UI
function updateInspectorUI() {
  const container = $('inspector-content');
  if (!selectedProp) {
    container.innerHTML = '<p style="font-size:10px;color:#78856f;line-height:1.7">Виберіть предмет на мапі або додайте новий із лівої панелі, щоб перемістити чи повернути його.</p>';
    return;
  }

  const deg = Math.round(((selectedProp.rotation || 0) * 180 / Math.PI) % 360);
  container.innerHTML = `
    <div class="inspector-card">
      <div>
        <div class="inspector-title">${selectedProp.label || selectedProp.type}</div>
        <div class="inspector-id">ID: ${selectedProp.id} · ТИП: ${selectedProp.type.toUpperCase()}</div>
      </div>
      <div class="coord-grid">
        <div class="coord-box">
          <label>X (КООРДИНАТА)</label>
          <input type="number" step="0.25" id="inp-x" value="${Number(selectedProp.x.toFixed(2))}">
        </div>
        <div class="coord-box">
          <label>Z (КООРДИНАТА)</label>
          <input type="number" step="0.25" id="inp-z" value="${Number(selectedProp.z.toFixed(2))}">
        </div>
      </div>
      <div class="coord-box">
        <label>КУТ ОБЕРТАННЯ: ${deg}°</label>
        <button class="btn" id="btn-rotate-45" style="width:100%">↻ ПОВЕРНУТИ (+45°)</button>
      </div>
      <div class="prop-tools">
        <button class="btn" id="btn-duplicate" style="width:100%">⎘ ДУБЛЮВАТИ</button>
        <button class="btn danger" id="btn-delete" style="width:100%">🗑 ВИДАЛИТИ</button>
      </div>
    </div>
  `;

  $('inp-x').onchange = (e) => {
    selectedProp.x = Number(e.target.value) || 0;
    if (selectedMesh) selectedMesh.position.x = selectedProp.x;
    updateSelectionBox();
  };
  $('inp-z').onchange = (e) => {
    selectedProp.z = Number(e.target.value) || 0;
    if (selectedMesh) selectedMesh.position.z = selectedProp.z;
    updateSelectionBox();
  };
  $('btn-rotate-45').onclick = rotateSelected;
  $('btn-duplicate').onclick = duplicateSelected;
  $('btn-delete').onclick = deleteSelected;
}

// Add new prop from catalog
function addPropFromCatalog(catalogItem) {
  const id = `custom_${catalogItem.type}_${Date.now().toString(36)}`;
  // Spawn in front of camera target
  const targetX = Math.round(controls.target.x * 4) / 4;
  const targetZ = Math.round(controls.target.z * 4) / 4;

  const newProp = {
    id,
    type: catalogItem.type,
    x: targetX,
    y: 0,
    z: targetZ,
    rotation: 0,
    label: `${catalogItem.name}`,
  };
  if (catalogItem.defaultW) newProp.w = catalogItem.defaultW;
  if (catalogItem.defaultD) newProp.d = catalogItem.defaultD;
  if (catalogItem.defaultSize) newProp.size = catalogItem.defaultSize;

  propsList.push(newProp);
  const mesh = spawnPropMesh(scene, newProp);
  if (mesh) {
    meshMap.set(id, mesh);
    selectProp(newProp, mesh);
    toast(`Додано: ${catalogItem.name}`);
  }
}

// Rotate selected prop by 45 degrees
function rotateSelected() {
  if (!selectedProp || !selectedMesh) return;
  selectedProp.rotation = (selectedProp.rotation || 0) + Math.PI / 4;
  selectedMesh.rotation.y = selectedProp.rotation;
  updateSelectionBox();
  updateInspectorUI();
}

// Duplicate selected prop
function duplicateSelected() {
  if (!selectedProp) return;
  const clone = JSON.parse(JSON.stringify(selectedProp));
  clone.id = `copy_${Date.now().toString(36)}`;
  clone.x += 1.0;
  clone.z += 1.0;
  clone.label = `${selectedProp.label} (копія)`;

  propsList.push(clone);
  const mesh = spawnPropMesh(scene, clone);
  if (mesh) {
    meshMap.set(clone.id, mesh);
    selectProp(clone, mesh);
    toast('Предмет продубльовано.');
  }
}

// Delete selected prop
function deleteSelected() {
  if (!selectedProp || !selectedMesh) return;
  scene.remove(selectedMesh);
  meshMap.delete(selectedProp.id);
  const idx = propsList.findIndex(p => p.id === selectedProp.id);
  if (idx !== -1) propsList.splice(idx, 1);
  deselectProp();
  toast('Предмет видалено.');
}

// Event Listeners for Raycasting & Dragging
function setupEventListeners() {
  viewportWrap.addEventListener('pointerdown', onPointerDown);
  window.addEventListener('pointermove', onPointerMove);
  window.addEventListener('pointerup', onPointerUp);

  // Keyboard Shortcuts: R = Rotate, D = Duplicate, Del = Delete
  window.addEventListener('keydown', (e) => {
    if (e.target instanceof HTMLInputElement) return;
    if (e.code === 'KeyR') rotateSelected();
    if (e.code === 'KeyD') duplicateSelected();
    if (e.code === 'Delete' || e.code === 'Backspace') deleteSelected();
    if (e.code === 'Escape') deselectProp();
  });

  // Room navigation buttons
  document.querySelectorAll('#room-nav .room-btn').forEach(btn => {
    btn.onclick = () => jumpToRoom(btn.dataset.room);
  });

  // Header Actions
  $('btn-save').onclick = saveMapToServer;
  $('btn-reset').onclick = resetMapDefaults;
}

function updateMouse(e) {
  const rect = canvas.getBoundingClientRect();
  mouse.x = ((e.clientX - rect.left) / rect.width) * 2 - 1;
  mouse.y = -((e.clientY - rect.top) / rect.height) * 2 + 1;
}

function onPointerDown(e) {
  if (e.button !== 0) return; // left click only
  updateMouse(e);
  raycaster.setFromCamera(mouse, camera);

  // Check intersection with placed props
  const meshes = Array.from(meshMap.values());
  const intersects = raycaster.intersectObjects(meshes, true);

  if (intersects.length > 0) {
    let topGroup = intersects[0].object;
    while (topGroup.parent && topGroup.parent !== scene && !topGroup.userData?.propId) {
      topGroup = topGroup.parent;
    }
    const propId = topGroup.userData?.propId;
    const prop = propsList.find(p => p.id === propId);

    if (prop) {
      selectProp(prop, topGroup);
      isDragging = true;
      controls.enabled = false;

      // Plane intersection for smooth drag offset
      if (raycaster.ray.intersectPlane(dragPlane, intersectionPoint)) {
        dragOffset.x = topGroup.position.x - intersectionPoint.x;
        dragOffset.z = topGroup.position.z - intersectionPoint.z;
      }
      return;
    }
  }

  // Clicked on empty space: deselect
  deselectProp();
}

function onPointerMove(e) {
  if (!isDragging || !selectedMesh || !selectedProp) return;
  updateMouse(e);
  raycaster.setFromCamera(mouse, camera);

  if (raycaster.ray.intersectPlane(dragPlane, intersectionPoint)) {
    // Snap to 0.25 meter grid for clean alignment
    const newX = Math.round((intersectionPoint.x + dragOffset.x) * 4) / 4;
    const newZ = Math.round((intersectionPoint.z + dragOffset.z) * 4) / 4;

    selectedMesh.position.x = newX;
    selectedMesh.position.z = newZ;
    selectedProp.x = newX;
    selectedProp.z = newZ;

    updateSelectionBox();
    const inpX = $('inp-x'), inpZ = $('inp-z');
    if (inpX) inpX.value = newX;
    if (inpZ) inpZ.value = newZ;
  }
}

function onPointerUp() {
  if (isDragging) {
    isDragging = false;
    controls.enabled = true;
  }
}

function onResize() {
  if (!renderer || !camera) return;
  const w = viewportWrap.clientWidth;
  const h = viewportWrap.clientHeight;
  renderer.setSize(w, h);
  camera.aspect = w / h;
  camera.updateProjectionMatrix();
}

// Room Camera Jumps
function jumpToRoom(roomId) {
  const targets = {
    all: { target: [0, 0, -24], camera: [0, 48, 12] },
    courtyard: { target: [0, 0, 21], camera: [0, 26, 44] },
    corridor: { target: [0, 0, -24], camera: [0, 22, -10] },
    archive: { target: [-7.25, 0, -1.5], camera: [-7.25, 14, 8] },
    apparatus: { target: [7.25, 0, -1.5], camera: [7.25, 14, 8] },
    generator: { target: [-7.25, 0, -16.5], camera: [-7.25, 14, -7] },
    studio: { target: [7.25, 0, -16.5], camera: [7.25, 14, -7] },
    vent: { target: [-7.25, 0, -31.5], camera: [-7.25, 14, -22] },
    server: { target: [7.25, 0, -31.5], camera: [7.25, 14, -22] },
    storage: { target: [-7.25, 0, -46.5], camera: [-7.25, 14, -37] },
    dispatch: { target: [7.25, 0, -46.5], camera: [7.25, 14, -37] },
  };

  const t = targets[roomId] || targets.all;
  controls.target.fromArray(t.target);
  camera.position.fromArray(t.camera);
  controls.update();
}

// Save map to server via POST /api/map
async function saveMapToServer() {
  $('btn-save').disabled = true;
  try {
    for (const p of propsList) {
      if (p.type === 'locker') {
        const rot = p.rotation || 0;
        p.exitX = Number((p.x + Math.sin(rot) * 1.3).toFixed(2));
        p.exitZ = Number((p.z + Math.cos(rot) * 1.3).toFixed(2));
      }
    }
    const res = await fetch('/api/map', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ props: propsList }),
    });
    const result = await res.json();
    if (result.ok) {
      toast(`✓ КАРТУ ЗБЕРЕЖЕНО! Збережено ${result.count} об’єктів. Гра використовуватиме цей макет.`);
    } else {
      toast(`Помилка: ${result.error || 'Не вдалося зберегти'}`);
    }
  } catch (e) {
    toast(`Помилка з’єднання: ${e.message}`);
  } finally {
    $('btn-save').disabled = false;
  }
}

// Reset map to defaults via POST /api/map/reset
async function resetMapDefaults() {
  if (!confirm('Скинути всі перестановки до стандартного макета станції?')) return;
  $('btn-reset').disabled = true;
  try {
    const res = await fetch('/api/map/reset', { method: 'POST' });
    const result = await res.json();
    if (result.ok) {
      renderProps(DEFAULT_PROPS);
      toast('✓ Карту скинуто до стандартного стану.');
    } else {
      toast(`Помилка: ${result.error || 'Не вдалося скинути'}`);
    }
  } catch (e) {
    toast(`Помилка з’єднання: ${e.message}`);
  } finally {
    $('btn-reset').disabled = false;
  }
}

// Render Loop
function tick() {
  requestAnimationFrame(tick);
  controls.update();
  renderer.render(scene, camera);
}

// Start Builder on load
initEngine();
