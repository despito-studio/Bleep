import * as THREE from 'three';

// Original, code-modelled cast. Local Y is up, +Z is forward and feet rest on Y=0.
// Low polygon lofts, actual facial geometry and small woven textures retain a
// strong silhouette in the game's deliberately low resolution rendering.
const cache = new Map();
const TAU = Math.PI * 2;
const palettes = {
  engineer: { name: 'Victor', cloth: '#153f3d', skin: '#caa66f', hip: .96, shoulder: 1.5, head: 1.85, armX: .37, upper: .35, lower: .36, legX: .145, legR: .12 },
  operator: { name: 'Vivian', cloth: '#541f33', skin: '#d9b8ad', hip: .94, shoulder: 1.37, head: 1.65, armX: .265, upper: .28, lower: .285, legX: .12, legR: .092 },
  journalist: { name: 'Edgar', cloth: '#2c3432', skin: '#b99a83', hip: .76, shoulder: 1.46, head: 1.68, armX: .57, upper: .41, lower: .35, legX: .215, legR: .185 },
  dispatcher: { name: 'Hazel', cloth: '#c35328', skin: '#bd8a54', hip: 1.0, shoulder: 1.46, head: 1.77, armX: .255, upper: .31, lower: .29, legX: .118, legR: .084 },
};

function material(color, { kind, metalness = 0, roughness = .88, emissive, opacity = 1 } = {}) {
  const key = `${color}/${kind}/${metalness}/${roughness}/${emissive}/${opacity}`;
  if (!cache.has(key)) cache.set(key, new THREE.MeshStandardMaterial({
    color: kind ? '#ffffff' : color,
    map: kind ? clothTexture(kind, color) : null,
    roughness, metalness, flatShading: true, side: THREE.DoubleSide,
    ...(emissive ? { emissive, emissiveIntensity: .6 } : {}),
    ...(opacity < 1 ? { transparent: true, opacity, depthWrite: false } : {}),
  }));
  return cache.get(key);
}

function clothTexture(kind, color) {
  const canvas = document.createElement('canvas');
  canvas.width = canvas.height = 128;
  const ctx = canvas.getContext('2d');
  ctx.fillStyle = color; ctx.fillRect(0, 0, 128, 128);
  let seed = 12031;
  const random = () => { seed = (seed * 1664525 + 1013904223) >>> 0; return seed / 4294967296; };
  if (kind === 'psychedelic') {
    ctx.fillStyle = '#c24b24'; ctx.fillRect(0, 0, 128, 128);
    for (let x = -32; x < 170; x += 29) {
      for (const [offset, width, ink] of [[0, 23, '#732d34'], [4, 13, '#e0763c'], [6, 5, '#dcb367']]) {
        ctx.strokeStyle = ink; ctx.lineWidth = width; ctx.beginPath();
        ctx.moveTo(x + offset, -18);
        for (let y = -18; y < 150; y += 4) ctx.lineTo(x + offset + Math.sin(y * Math.PI / 64) * 17, y);
        ctx.stroke();
      }
    }
  } else if (kind === 'tapestry') {
    ctx.fillStyle = '#5d4630'; ctx.fillRect(0, 0, 128, 128);
    for (let y = 5; y < 128; y += 16) for (let x = 3; x < 128; x += 16) {
      const px = x + ((y / 16) % 2) * 5;
      ctx.strokeStyle = '#918a47'; ctx.lineWidth = 2; ctx.beginPath(); ctx.moveTo(px, y + 10); ctx.lineTo(px + 5, y - 5); ctx.stroke();
      for (let k = 0; k < 4; k++) { ctx.fillStyle = k % 2 ? '#ae8254' : '#784037'; ctx.fillRect(px + (k % 2) * 4, y + Math.floor(k / 2) * 4, 5, 5); }
      ctx.fillStyle = '#c7ad69'; ctx.fillRect(px + 3, y + 3, 3, 3);
    }
  } else if (kind === 'lace') {
    ctx.fillStyle = '#241e28'; ctx.fillRect(0, 0, 128, 128);
    ctx.strokeStyle = '#74616a'; ctx.lineWidth = 1;
    for (let y = 0; y < 140; y += 11) for (let x = 0; x < 140; x += 12) {
      ctx.beginPath(); ctx.ellipse(x + (y % 2) * 6, y, 5, 8, .4, 0, TAU); ctx.stroke();
    }
  } else if (kind === 'tweed') {
    for (let y = 0; y < 128; y += 4) for (let x = 0; x < 128; x += 4) {
      ctx.fillStyle = (x + y) % 8 ? '#424946' : '#202928';
      ctx.fillRect(x, y, 3, 1); ctx.fillRect(x + 1, y + 1, 2, 1); ctx.fillRect(x + 2, y + 2, 1, 1);
    }
    ctx.fillStyle = '#8b88782b'; for (let x = 0; x < 128; x += 32) ctx.fillRect(x, 0, 1, 128);
  }
  for (let i = 0; i < 2700; i++) {
    ctx.fillStyle = random() > .5 ? `rgba(255,229,196,${random() * .11})` : `rgba(5,10,9,${random() * .18})`;
    ctx.fillRect(Math.floor(random() * 128), Math.floor(random() * 128), 1, 1);
  }
  if (kind !== 'lace') {
    ctx.fillStyle = '#080d0a12'; for (let y = 1; y < 128; y += 3) ctx.fillRect(0, y, 128, 1);
  }
  const texture = new THREE.CanvasTexture(canvas);
  texture.colorSpace = THREE.SRGBColorSpace;
  texture.magFilter = THREE.NearestFilter;
  texture.minFilter = THREE.NearestMipmapNearestFilter;
  texture.wrapS = texture.wrapT = THREE.RepeatWrapping;
  return texture;
}

function add(parent, geometry, mat, x = 0, y = 0, z = 0) {
  const mesh = new THREE.Mesh(geometry, mat); mesh.position.set(x, y, z);
  mesh.castShadow = true; mesh.receiveShadow = true; parent.add(mesh); return mesh;
}
function group(parent, x = 0, y = 0, z = 0) {
  const g = new THREE.Group(); g.position.set(x, y, z); parent.add(g); return g;
}
function ellipsoid(parent, x, y, z, sx, sy, sz, mat, segments = 8) {
  const m = add(parent, new THREE.SphereGeometry(1, segments, 5), mat, x, y, z); m.scale.set(sx, sy, sz); return m;
}
function cylinder(parent, x, y, z, top, bottom, height, mat, segments = 8) {
  return add(parent, new THREE.CylinderGeometry(top, bottom, height, segments, 1), mat, x, y, z);
}

// Elliptical profile rings: [height, half-width, half-depth, x-offset?, z-offset?].
function loft(parent, rings, mat, segments = 8) {
  const vertices = [], uv = [], indices = [];
  for (let j = 0; j < rings.length; j++) {
    const [y, rx, rz, cx = 0, cz = 0] = rings[j];
    for (let i = 0; i <= segments; i++) {
      const angle = (i + .5) / segments * TAU;
      vertices.push(cx + Math.sin(angle) * rx, y, cz + Math.cos(angle) * rz);
      uv.push(i / segments, j / (rings.length - 1));
    }
  }
  for (let j = 0; j < rings.length - 1; j++) for (let i = 0; i < segments; i++) {
    const a = j * (segments + 1) + i, b = a + segments + 1;
    indices.push(a, b, a + 1, b, b + 1, a + 1);
  }
  for (let i = 1; i < segments - 1; i++) { indices.push(0, i + 1, i); const t = (rings.length - 1) * (segments + 1); indices.push(t, t + i, t + i + 1); }
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute('position', new THREE.Float32BufferAttribute(vertices, 3));
  geometry.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
  geometry.setIndex(indices); geometry.computeVertexNormals();
  return add(parent, geometry, mat);
}

function panel(parent, points, mat) {
  const positions = points.flat(), uv = [], indices = [];
  const minX = Math.min(...points.map(p => p[0])), maxX = Math.max(...points.map(p => p[0]));
  const minY = Math.min(...points.map(p => p[1])), maxY = Math.max(...points.map(p => p[1]));
  for (const [x, y] of points) uv.push((x - minX) / (maxX - minX || 1), (y - minY) / (maxY - minY || 1));
  for (let i = 1; i < points.length - 1; i++) indices.push(0, i, i + 1);
  const geometry = new THREE.BufferGeometry(); geometry.setAttribute('position', new THREE.Float32BufferAttribute(positions, 3));
  geometry.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2)); geometry.setIndex(indices); geometry.computeVertexNormals();
  return add(parent, geometry, mat);
}

function rod(parent, start, end, radius, mat, endRadius = radius) {
  const a = new THREE.Vector3(...start), b = new THREE.Vector3(...end);
  const midpoint = a.clone().add(b).multiplyScalar(.5);
  const mesh = cylinder(parent, midpoint.x, midpoint.y, midpoint.z, endRadius, radius, a.distanceTo(b), mat, 6);
  mesh.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), b.sub(a).normalize()); return mesh;
}

function button(parent, x, y, z, r, mat) {
  const m = cylinder(parent, x, y, z, r, r * .87, .015, mat, 10); m.rotation.x = Math.PI / 2; return m;
}

function hands(parent, size, skin, fingernails = false) {
  const hand = group(parent);
  loft(hand, [[.015, .037 * size, .028 * size], [-.065 * size, .064 * size, .035 * size], [-.12 * size, .058 * size, .026 * size]], skin, 6);
  for (let i = 0; i < 4; i++) {
    const x = (i - 1.5) * .028 * size, length = (.075 - Math.abs(i - 1.7) * .011) * size;
    const finger = group(hand, x, -.102 * size, .003);
    finger.rotation.z = (i - 1.5) * -.05;
    loft(finger, [[0, .015 * size, .021 * size], [-length * .52, .014 * size, .018 * size, 0, .008], [-length, .011 * size, .013 * size, 0, .017]], skin, 5);
    if (fingernails) ellipsoid(finger, 0, -length * .77, .029 * size, .01 * size, .014 * size, .003, material('#51202a'), 6);
  }
  const thumb = ellipsoid(hand, -.068 * size, -.065 * size, .013, .026 * size, .055 * size, .029 * size, skin, 6); thumb.rotation.z = -.4;
  return hand;
}

function eye(parent, x, y, z, size, iris, angle = 0, feminine = false) {
  const e = group(parent, x, y, z); e.rotation.z = angle;
  ellipsoid(e, 0, 0, -.008, size * 1.1, size * .61, .018, material('#44362e'), 8);
  ellipsoid(e, 0, -.002, 0, size, size * .45, .019, material('#d2cfaf'), 8);
  ellipsoid(e, 0, -.002, .019, size * .35, size * .39, .008, material(iris), 8);
  ellipsoid(e, 0, -.002, .026, size * .16, size * .28, .004, material('#0d1513'), 6);
  ellipsoid(e, -size * .07, size * .12, .03, size * .08, size * .08, .003, material('#faf2cd'), 5);
  if (feminine) {
    panel(e, [[-size * 1.15, .006, .025], [-size * .42, size * .44, .025], [size * .6, size * .35, .025], [size * 1.4, size * .65, .024], [size * .92, -.001, .025], [0, size * .21, .026]], material('#281b20'));
  }
  return e;
}

function facialFeatures(head, type, skin) {
  const feminine = type === 'operator' || type === 'dispatcher';
  const edgar = type === 'journalist', victor = type === 'engineer';
  const width = edgar ? .217 : victor ? .148 : .157;
  const front = edgar ? .166 : .132;
  const low = edgar ? -.22 : victor ? -.28 : -.21;
  loft(head, [[low, width * .53, .076, 0, .018], [low + .055, width * .77, .105], [-.07, width, front * .94], [.09, width * .99, front], [.235, width * .79, front * .95], [.292, width * .46, .075]], skin, 8);
  const eyeY = victor ? .09 : .045, eyeZ = front * .95 + .012;
  const eyeX = edgar ? .087 : .067;
  for (const s of [-1, 1]) {
    const iris = type === 'dispatcher' ? '#488b66' : type === 'operator' ? '#778d90' : '#847451';
    eye(head, s * eyeX, eyeY, eyeZ, edgar ? .046 : victor ? .049 : .046, iris, feminine ? -s * .1 : s * .03, feminine);
    const brow = material(type === 'operator' ? '#8a7772' : '#3e352b');
    panel(head, [[s * .02, eyeY + .055, eyeZ], [s * .08, eyeY + (edgar ? .072 : .069), eyeZ], [s * .135, eyeY + .047, eyeZ - .01], [s * .09, eyeY + .053, eyeZ + .003], [s * .03, eyeY + .039, eyeZ + .008]], brow);
    ellipsoid(head, s * width * .96, -.028, -.006, .035, .058, .038, skin, 6);
    if (edgar) {
      ellipsoid(head, s * .121, -.12, front * .63, .072, .082, .046, skin, 7);
      panel(head, [[s * .045, eyeY - .04, eyeZ], [s * .125, eyeY - .038, eyeZ - .007], [s * .095, eyeY - .06, eyeZ]], material('#866e60'));
    } else {
      panel(head, [[s * .112, -.004, front * .91], [s * .15, -.027, front * .77], [s * .1, -.105, front * .96]], material(feminine ? '#b98576' : '#aa8154'));
    }
  }
  // A pointed bridge and a projecting nose, with explicit side planes.
  const noseTipY = victor ? -.075 : -.045, noseTipZ = front + (victor ? .089 : edgar ? .079 : .046);
  const noseWidth = edgar ? .068 : .026;
  panel(head, [[-.021, .09, front], [.021, .09, front], [noseWidth, noseTipY - .025, front + .027], [0, noseTipY, noseTipZ]], skin);
  panel(head, [[-.021, .09, front], [0, noseTipY, noseTipZ], [-noseWidth, noseTipY - .025, front + .027]], material(edgar ? '#a88776' : '#b38c70'));
  panel(head, [[-noseWidth, noseTipY - .025, front + .027], [0, noseTipY, noseTipZ], [noseWidth, noseTipY - .025, front + .027], [0, noseTipY - .038, front + .024]], material('#98725b'));
  if (edgar) ellipsoid(head, 0, -.05, front + .04, .059, .051, .061, skin, 7);
  if (!victor) {
    const mouthY = edgar ? -.154 : -.125, mouthW = edgar ? .089 : .065;
    const lip = material(feminine ? (type === 'operator' ? '#5e2430' : '#9b3b32') : '#806052');
    panel(head, [[-mouthW, mouthY + .012, front * .85], [-mouthW * .33, mouthY + .026, front * .93], [0, mouthY + .013, front], [mouthW * .28, mouthY + .025, front * .94], [mouthW, mouthY + .013, front * .85], [0, mouthY - .026, front]], lip);
    panel(head, [[-mouthW * .86, mouthY + .006, front * .95], [0, mouthY - .004, front + .003], [mouthW * .85, mouthY + .008, front * .95], [0, mouthY - .013, front + .003]], material('#251d1b'));
    if (type === 'dispatcher') panel(head, [[-.047, mouthY + .006, front + .005], [.05, mouthY + .008, front + .005], [.027, mouthY - .006, front + .006], [-.026, mouthY - .008, front + .006]], material('#e7d7b5'));
  }
  return { front, width };
}

function shoes(parent, y, radius, female, skin) {
  const leather = material(female ? '#292127' : '#171e1c', { roughness: .52 });
  // Foot profile is narrower at the heel and low at the pointed toe.
  const shoe = group(parent, 0, y, 0);
  loft(shoe, [[-.055, radius * .88, .16, 0, .058], [.005, radius, .175, 0, .045], [.07, radius * .76, .105, 0, .005]], leather, 8);
  if (female) {
    const heel = cylinder(shoe, 0, -.08, -.065, .018, .017, .08, leather, 5);
    heel.rotation.x = -.1;
    panel(shoe, [[-radius * .62, .07, .01], [radius * .62, .07, .01], [radius * .5, .019, .113], [-radius * .5, .019, .113]], skin);
  } else {
    rod(shoe, [-radius * .73, .042, .1], [radius * .73, .042, .1], .006, material('#565b50'));
    for (let i = 0; i < 3; i++) rod(shoe, [-radius * .48, .065 - i * .005, .015 + i * .022], [radius * .48, .065 - i * .005, .015 + i * .022], .005, material('#323b34'));
  }
  return shoe;
}

function buildLeg(rig, side, spec, type, skin, fabric, dark) {
  const hip = group(rig, side * spec.legX, spec.hip, 0);
  const footHeight = type === 'operator' ? .11 : .09;
  const total = spec.hip - footHeight, upper = total * .5, lower = total - upper;
  const isFemale = type === 'operator' || type === 'dispatcher';
  const stockings = type === 'operator' ? material('#423b42', { kind: 'woven' }) : skin;
  const legMat = isFemale ? stockings : fabric;
  loft(hip, [[0, spec.legR, spec.legR * 1.08], [-upper * .52, spec.legR * .87, spec.legR * .94], [-upper, spec.legR * .62, spec.legR * .68]], legMat, 7);
  const knee = group(hip, 0, -upper, 0);
  const boot = type === 'dispatcher';
  loft(knee, [[0, spec.legR * .63, spec.legR * .7], [-lower * .37, spec.legR * .71, spec.legR * .79, 0, -.011], [-lower, spec.legR * .41, spec.legR * .45]], boot ? material('#482c2a', { kind: 'woven' }) : legMat, 7);
  if (boot) loft(knee, [[-.04, spec.legR * .79, spec.legR * .86], [-.075, spec.legR * .78, spec.legR * .84]], material('#251e1d'), 7);
  if (type === 'engineer') rod(hip, [side * spec.legR * .83, -.055, 0], [side * spec.legR * .62, -upper + .02, 0], .009, material('#7a8155'));
  if (type === 'journalist') loft(knee, [[-lower + .085, spec.legR * .78, spec.legR * .81], [-lower + .025, spec.legR * .83, spec.legR * .89]], fabric, 7);
  shoes(knee, -lower, type === 'journalist' ? .144 : type === 'engineer' ? .099 : .075, type === 'operator', skin);
  return { hip, knee, side, upper, lower };
}

function buildArm(torso, side, spec, type, skin, cloth) {
  const heavy = type === 'journalist', guard = type === 'engineer', female = !heavy && !guard;
  const shoulder = group(torso, side * spec.armX, spec.shoulder - spec.hip, heavy ? -.035 : 0);
  const radius = heavy ? .176 : guard ? .112 : .064;
  loft(shoulder, [[.035, radius * .84, radius * .93], [-.08, radius, radius], [-spec.upper, radius * .73, radius * .75]], female ? skin : cloth, 7);
  if (type === 'operator') loft(shoulder, [[.032, radius * 1.03, radius * 1.07], [-.10, radius * 1.07, radius * 1.04], [-.15, radius * .94, radius * .98]], cloth, 7);
  const elbow = group(shoulder, 0, -spec.upper, 0);
  loft(elbow, [[0, radius * .73, radius * .77], [-spec.lower * .53, radius * .65, radius * .65], [-spec.lower, radius * .47, radius * .47]], female ? skin : cloth, 7);
  if (!female) {
    loft(elbow, [[-spec.lower + .046, radius * .59, radius * .58], [-spec.lower - .011, radius * .59, radius * .58]], guard ? material('#192d2a') : material('#c6beb0'), 7);
    if (guard) for (let i = 0; i < 2; i++) loft(elbow, [[-spec.lower + .085 + i * .023, radius * .68, radius * .7], [-spec.lower + .095 + i * .023, radius * .68, radius * .7]], material('#a7975c'), 7);
  }
  const handRoot = group(elbow, 0, -spec.lower - .018, 0);
  const hand = hands(handRoot, guard ? 1.35 : heavy ? 1.22 : .76, skin, female);
  shoulder.rotation.z = side * (heavy ? .14 : .075);
  return { shoulder, elbow, handRoot, hand, side, baseZ: shoulder.rotation.z, baseX: heavy ? -.08 : 0, bend: heavy ? -.28 : -.12 };
}

function dress(torso, spec, type, cloth, skin) {
  const h = spec.hip;
  // Neckline and exposed décolletage occupy their own shaped volume.
  loft(torso, [[.19, .15, .10], [spec.shoulder - h - .07, .217, .125], [spec.shoulder - h + .012, .204, .113], [spec.shoulder - h + .10, .069, .068]], skin, 8);
  const patterned = type === 'dispatcher';
  loft(torso, [[-.18, .259, .153], [.012, .226, .14], [.20, .156, .103], [.32, .205, .126], [.39, .208, .121]], cloth, 10);
  const neckline = patterned ? cloth : material('#2b202a', { kind: 'lace' });
  panel(torso, [[-.215, .403, .095], [-.14, .368, .13], [0, .28, .134], [.14, .369, .132], [.215, .403, .095], [.183, .29, .133], [0, .246, .126], [-.183, .29, .133]], neckline);
  for (const side of [-1, 1]) panel(torso, [[side * .205, .398, .077], [side * .187, .465, .065], [side * .147, .442, .105], [side * .145, .365, .132]], cloth);
  if (!patterned) {
    rod(torso, [-.153, .197, .047], [-.215, .035, .087], .006, material('#7a4350'));
    rod(torso, [.153, .197, .047], [.215, .035, .087], .006, material('#7a4350'));
    loft(torso, [[-.177, .26, .155], [-.195, .265, .156]], material('#30232c'), 10);
  }
}

function platinumHair(head, secondary) {
  const white = material('#d7cfb8', { kind: 'woven' });
  const shadow = material('#b6a6a0');
  loft(head, [[.13, .177, .157, 0, -.025], [.27, .181, .155, 0, -.012], [.338, .112, .112, 0, -.028]], white, 8);
  // Broad heavy fringe, with a few individually cut ends.
  for (let i = 0; i < 5; i++) {
    const x = (i - 2) * .058;
    panel(head, [[x - .034, .269, .133], [x + .034, .269, .133], [x + .032, .09 + (i % 2) * .012, .157], [x - .031, .088, .159]], i % 2 ? white : material('#e2d9c1'));
  }
  for (const s of [-1, 1]) {
    const side = group(head, s * .145, .15, -.015);
    loft(side, [[.10, .038, .096], [-.04, .05, .105, s * .025], [-.32, .041, .076, s * .038, -.001], [-.43, .016, .045, s * .063, .009]], white, 5);
    panel(side, [[s * .024, .04, .072], [s * .048, -.285, .075], [s * .08, -.41, .062], [s * .025, -.32, .075]], shadow);
    secondary.push({ object: side, amplitude: .034, phase: s, axis: 'z' });
    const hoop = add(head, new THREE.TorusGeometry(.044, .007, 4, 12), material('#ba994e', { metalness: .62, roughness: .35 }), s * .169, -.079, .033);
    hoop.rotation.y = s * .25;
  }
  loft(head, [[-.30, .166, .068, 0, -.11], [.15, .18, .083, 0, -.092]], white, 7);
}

function longHair(head, secondary) {
  const hair = material('#291e19', { kind: 'woven' }), lit = material('#433025');
  loft(head, [[.095, .178, .135, 0, -.04], [.26, .184, .148, 0, -.045], [.323, .123, .097, 0, -.025]], hair, 8);
  // Back curtains taper outward around the shoulders, leaving the face open.
  for (const s of [-1, 1]) {
    const side = group(head, s * .142, .21, -.051);
    loft(side, [[0, .052, .105], [-.26, .083, .108, s * .024, -.012], [-.58, .088, .088, s * .07, .018], [-.83, .014, .021, s * .148, .105]], hair, 6);
    loft(side, [[-.035, .018, .062, s * .03, .082], [-.32, .033, .046, s * .075, .083], [-.67, .007, .008, s * .116, .13]], lit, 5);
    secondary.push({ object: side, amplitude: .055, phase: s * 1.7, axis: 'z' });
    // Swept pointed fringe has a high central widow's peak and flared ends.
    panel(head, [[0, .282, .11], [s * .085, .306, .126], [s * .181, .223, .155], [s * .263, .22, .093], [s * .197, .159, .151], [s * .113, .153, .166], [s * .042, .214, .166]], hair);
    panel(head, [[s * .023, .263, .165], [s * .111, .192, .17], [s * .177, .187, .16], [s * .132, .22, .17]], lit);
    const earring = group(head, s * .18, -.047, .035);
    const ivory = material('#d7d4b6', { roughness: .45 });
    ellipsoid(earring, 0, -.005, 0, .014, .018, .012, ivory, 6);
    panel(earring, [[0, -.014, .004], [-s * .008, -.066, .01], [s * .034, -.102, .014], [s * .012, -.124, .018], [-s * .018, -.112, .018], [-s * .026, -.075, .01]], ivory);
    secondary.push({ object: earring, amplitude: .07, phase: s, axis: 'z' });
  }
  loft(head, [[-.55, .145, .042, 0, -.117], [-.14, .18, .066, 0, -.128], [.21, .16, .065, 0, -.117]], hair, 7);
}

function guardDetails(torso, head, spec, cloth, skin, secondary) {
  const gold = material('#b9a05a', { metalness: .55, roughness: .43 }), ivory = material('#cfcaad'), dark = material('#142120');
  const h = spec.shoulder - spec.hip;
  loft(torso, [[-.18, .335, .182], [.06, .266, .16], [.34, .306, .178], [h, .346, .18], [h + .065, .175, .11]], cloth, 8);
  panel(torso, [[-.126, h + .018, .159], [.126, h + .018, .159], [.083, .215, .178], [-.071, .215, .178]], ivory);
  panel(torso, [[-.026, h + .027, .19], [.029, h + .027, .19], [.06, .24, .199], [0, .179, .205], [-.052, .25, .199]], material('#101b1b'));
  for (const s of [-1, 1]) {
    panel(torso, [[s * .117, h + .035, .154], [s * .302, h - .008, .162], [s * .235, .355, .193], [s * .121, .302, .196]], material('#20514a', { kind: 'woven' }));
    panel(torso, [[s * .117, h + .026, .168], [s * .218, .364, .197], [s * .08, .273, .209]], cloth);
    const flap = panel(torso, [[s * .09, .08, .181], [s * .255, .088, .16], [s * .253, .026, .171], [s * .125, .012, .187]], dark);
    flap.userData.detail = 'pocket';
    rod(torso, [s * .25, h + .025, -.02], [s * .39, h - .008, -.006], .018, gold);
  }
  for (const y of [.16, -.015, -.13]) button(torso, -.015, y, .195, .036, gold);
  button(torso, .225, .29, .175, .032, gold);
  const beard = material('#d8d4bd', { kind: 'woven' });
  // The two long beard tips sit over the collar, with contrasting strand planes.
  for (const s of [-1, 1]) {
    const lock = group(head, s * .066, -.13, .103);
    loft(lock, [[.045, .056, .031], [-.11, .079, .038, s * .008, .023], [-.29, .052, .021, s * .02, .044], [-.42, .006, .011, s * .04, .046]], beard, 5);
    rod(lock, [s * .01, -.065, .062], [s * .034, -.325, .069], .007, material('#969b8b'), .003);
    secondary.push({ object: lock, amplitude: .025, phase: s, axis: 'x' });
    panel(head, [[0, -.112, .186], [s * .06, -.075, .182], [s * .146, -.115, .159], [s * .106, -.16, .184], [s * .027, -.147, .194]], beard);
  }
  panel(head, [[-.041, -.143, .161], [.041, -.143, .161], [.028, -.162, .166], [-.023, -.163, .166]], dark);
  // Oversized six-sided peaked cap and a proper five-point metal badge.
  const cap = group(head, 0, .274, -.017); cap.rotation.z = -.035;
  loft(cap, [[0, .177, .147], [.09, .194, .16], [.12, .207, .178]], dark, 6);
  loft(cap, [[.088, .206, .174], [.268, .242, .18, -.02, -.025], [.327, .087, .119, -.01, -.016]], cloth, 5);
  loft(cap, [[.032, .182, .153], [.047, .184, .155]], gold, 8);
  const visor = ellipsoid(cap, 0, -.009, .157, .217, .031, .139, dark, 8); visor.rotation.x = .12;
  const star = [];
  for (let i = 0; i < 10; i++) { const a = i / 10 * TAU + Math.PI / 2, r = i % 2 ? .03 : .067; star.push([Math.cos(a) * r, .204 + Math.sin(a) * r, .188]); }
  panel(cap, star, gold); button(cap, 0, .204, .193, .018, material('#deca79', { metalness: .65, roughness: .3 }));
}

function reporterDetails(torso, head, spec, cloth, skin, secondary) {
  const shirt = material('#c4bba5', { kind: 'woven' }), lapel = material('#333b37', { kind: 'tweed' });
  const h = spec.shoulder - spec.hip;
  loft(torso, [[-.085, .427, .25], [.2, .487, .325, 0, .023], [.47, .536, .295, 0, -.025], [h, .565, .255, 0, -.06], [h + .085, .338, .172, 0, -.07]], cloth, 8);
  ellipsoid(torso, 0, h - .02, -.165, .415, .283, .215, cloth, 8);
  loft(torso, [[-.034, .294, .074, 0, .245], [.18, .292, .099, 0, .265], [.43, .246, .082, 0, .25], [h + .05, .191, .043, 0, .171]], shirt, 7);
  for (const s of [-1, 1]) {
    panel(torso, [[s * .205, h + .067, .171], [s * .437, h - .012, .202], [s * .34, .42, .317], [s * .276, .343, .345], [s * .104, .126, .352]], lapel);
    panel(torso, [[s * .165, h + .101, .179], [s * .264, h + .032, .211], [s * .179, h - .091, .256], [s * .032, h - .058, .253]], shirt);
    panel(torso, [[s * .309, .18, .288], [s * .424, .217, .273], [s * .434, .142, .292], [s * .317, .114, .31]], material('#222c28'));
  }
  for (const y of [.48, .28, .11]) button(torso, .025, y, .363, .013, material('#a99d83'));
  const tie = group(torso, .074, h - .018, .299);
  const tapestry = material('#655038', { kind: 'tapestry' });
  loft(tie, [[.026, .047, .021], [-.06, .031, .017], [-.125, .043, .012]], tapestry, 5);
  panel(tie, [[-.033, -.083, .017], [.04, -.083, .017], [.092, -.67, .062], [.071, -1.027, .067], [0, -1.13, .06], [-.073, -1.023, .064], [-.047, -.65, .053]], tapestry);
  const tail = group(torso, -.134, h + .02, .234);
  panel(tail, [[-.026, 0, .01], [.026, 0, .01], [.037, -.79, .09], [0, -.9, .075], [-.039, -.784, .091]], tapestry);
  secondary.push({ object: tie, amplitude: .06, phase: 0, axis: 'z' }, { object: tail, amplitude: .07, phase: 1.5, axis: 'x' });
  const hair = material('#393b30', { kind: 'woven' });
  for (const s of [-1, 1]) {
    loft(head, [[-.025, .035, .067, s * .187, -.066], [.183, .038, .077, s * .174, -.056], [.247, .022, .058, s * .121, -.065]], hair, 5);
    panel(head, [[s * .178, -.015, .003], [s * .196, -.08, -.01], [s * .169, -.068, .063]], hair);
  }
  loft(head, [[.17, .149, .028, 0, -.128], [.255, .115, .047, 0, -.1], [.282, .071, .035, 0, -.074]], hair, 7);
  // A sparse crooked part and forehead creases keep the balding face readable.
  rod(head, [-.085, .267, .038], [.028, .293, -.014], .009, hair, .003);
  for (const y of [.139, .173]) rod(head, [-.071, y, .152], [.062, y + .002, .152], .003, material('#947b66'));
}

function pearlNecklace(torso, spec) {
  const pearls = material('#d8cfb5', { roughness: .42 });
  const height = spec.shoulder - spec.hip;
  for (let i = 0; i < 15; i++) {
    const angle = i / 14 * Math.PI;
    const x = Math.cos(angle) * .12, y = height + .026 - Math.sin(angle) * .10;
    ellipsoid(torso, x, y, .091 + Math.sin(angle) * .053, .012, .012, .012, pearls, 6);
  }
  for (let i = 0; i < 17; i++) {
    const angle = i / 16 * Math.PI;
    ellipsoid(torso, Math.cos(angle) * .128, height + .055 - Math.sin(angle) * .086, .088 + Math.sin(angle) * .048, .008, .008, .008, pearls, 5);
  }
}

function cigarette(arm, secondary) {
  const holder = group(arm.handRoot, -.036, -.09, .016);
  holder.rotation.z = -.55;
  rod(holder, [0, 0, 0], [-.184, -.018, .015], .007, material('#d9cfb4'));
  rod(holder, [-.002, 0, 0], [-.038, -.003, .003], .0075, material('#a88258'));
  const ember = ellipsoid(holder, -.188, -.019, .016, .008, .008, .008, material('#bf633f', { emissive: '#96311c' }), 5);
  const smoke = [];
  const smokeMat = new THREE.MeshBasicMaterial({ color: '#8d9b94', transparent: true, opacity: .12, depthWrite: false });
  for (let i = 0; i < 5; i++) {
    const puff = add(holder, new THREE.SphereGeometry(1, 5, 3), smokeMat, -.188, .025 + i * .036, .02);
    puff.scale.set(.008 + i * .002, .014, .005); puff.castShadow = false; smoke.push(puff);
  }
  secondary.push({ smoke, ember });
}

/**
 * Create one of the four playable characters. The public animation contract
 * accepts the existing two arguments; a third optional state enables sprinting.
 */
export function createSurvivor(color, characterClass = 'engineer') {
  const type = Object.hasOwn(palettes, characterClass) ? characterClass : 'engineer';
  const spec = palettes[type];
  const root = new THREE.Group(); root.name = `${spec.name} / ${type}`;
  const rig = group(root), torso = group(rig, 0, spec.hip, 0), secondary = [];
  const tint = new THREE.Color(spec.cloth);
  if (color !== undefined && color !== null) {
    try { tint.lerp(new THREE.Color(color), .13); } catch { /* Palette is the fallback. */ }
  }
  const cloth = material(`#${tint.getHexString()}`, { kind: type === 'journalist' ? 'tweed' : type === 'dispatcher' ? 'psychedelic' : 'woven' });
  const skin = material(spec.skin), dark = material('#202b29', { kind: 'woven' });
  const legs = [-1, 1].map(s => buildLeg(rig, s, spec, type, skin, type === 'engineer' ? cloth : dark, dark));
  const arms = [-1, 1].map(s => buildArm(torso, s, spec, type, skin, cloth));
  const head = group(torso, 0, spec.head - spec.hip, type === 'journalist' ? .089 : .008);
  const neckBottom = spec.shoulder - spec.hip - .008, neckTop = spec.head - spec.hip - .17;
  loft(torso, [[neckBottom, type === 'journalist' ? .132 : .072, .077, 0, .008], [neckTop, type === 'journalist' ? .122 : .065, .074, 0, type === 'journalist' ? .082 : .008]], skin, 7);
  facialFeatures(head, type, skin);

  if (type === 'engineer') guardDetails(torso, head, spec, cloth, skin, secondary);
  else if (type === 'journalist') reporterDetails(torso, head, spec, cloth, skin, secondary);
  else {
    dress(torso, spec, type, cloth, skin);
    if (type === 'operator') {
      platinumHair(head, secondary); pearlNecklace(torso, spec);
      arms[0].baseX = -.27; arms[0].bend = -1.28; arms[0].baseZ = -.18;
      arms[1].baseX = .045; arms[1].bend = -.24;
      cigarette(arms[0], secondary);
    } else longHair(head, secondary);
  }

  // Small shared utility clip identifies playable characters without covering
  // the character-defining costume. The role's main equipment lives in the HUD.
  const clipMat = material('#727362', { metalness: .38, roughness: .52 });
  if (type === 'engineer' || type === 'journalist') {
    const clip = group(torso, type === 'journalist' ? -.31 : -.21, .30, type === 'journalist' ? .299 : .171);
    loft(clip, [[-.047, .045, .018], [.047, .045, .018]], material('#27322d'), 4);
    for (let i = 0; i < 3; i++) rod(clip, [-.025, i * .018 - .02, .024], [.025, i * .018 - .02, .024], .0035, clipMat);
  }

  root.userData.characterClass = type;
  root.userData.characterName = spec.name;
  root.userData.head = head;
  root.userData.rig = { body: torso, arms, legs, head };
  root.userData.animate = (time, moving = false, state = {}) => {
    const active = Boolean(moving), run = Boolean(state.sprinting);
    const pace = type === 'journalist' ? 5.3 : type === 'engineer' ? 6.1 : 6.6;
    const phase = time * pace * (run ? 1.3 : 1);
    const amplitude = active ? (run ? .60 : type === 'journalist' ? .34 : .43) : 0;
    const breathe = Math.sin(time * 1.5);
    rig.position.y = active ? Math.abs(Math.sin(phase)) * (type === 'journalist' ? .016 : .025) : breathe * .006;
    torso.rotation.x = (type === 'journalist' ? .085 : 0) + (active && run ? .055 : 0) + breathe * .006;
    torso.rotation.z = active ? Math.sin(phase) * (type === 'journalist' ? .025 : .017) : Math.sin(time * .69) * .007;
    torso.rotation.y = active ? Math.sin(phase) * .032 : Math.sin(time * .37) * .015;
    for (const leg of legs) {
      const step = Math.sin(phase + (leg.side < 0 ? Math.PI : 0));
      leg.hip.rotation.x = step * amplitude;
      leg.knee.rotation.x = active ? Math.max(0, -step) * (run ? .71 : .48) : .015;
      leg.hip.rotation.z = leg.side * (type === 'journalist' ? .035 : .012);
    }
    for (const arm of arms) {
      const wave = Math.sin(phase + (arm.side < 0 ? Math.PI : 0));
      const carrying = type === 'operator' && arm.side < 0;
      arm.shoulder.rotation.x = arm.baseX - (active ? wave * amplitude * (carrying ? .24 : .7) : Math.sin(time * 1.15 + arm.side) * .015);
      arm.shoulder.rotation.z = arm.baseZ + (active ? Math.cos(phase) * .018 : Math.sin(time * .85 + arm.side) * .012);
      arm.elbow.rotation.x = arm.bend - (active ? Math.max(0, wave) * (carrying ? .08 : .18) : breathe * .013);
      arm.handRoot.rotation.z = Math.sin(time * .8 + arm.side) * .035;
    }
    head.rotation.y = Math.sin(time * .48) * (active ? .035 : .12);
    head.rotation.x = (type === 'journalist' ? -.06 : -.016) + Math.sin(time * .79) * .022;
    head.rotation.z = type === 'dispatcher' ? Math.sin(time * .61) * .026 : Math.sin(time * .55) * .014;
    for (const item of secondary) {
      if (item.object) item.object.rotation[item.axis] = Math.sin(time * (active ? pace : 1.3) + item.phase) * item.amplitude * (active ? 1 : .35);
      if (item.smoke) for (let i = 0; i < item.smoke.length; i++) {
        const puff = item.smoke[i]; const progress = (time * .22 + i * .2) % 1;
        puff.position.set(-.188 + Math.sin(time * 1.4 + i) * .016, .018 + progress * .20, .016 + Math.cos(time + i) * .008);
        puff.scale.setScalar(.006 + progress * .016); puff.scale.y *= 1.55;
      }
    }
    root.userData.bob = rig.position.y;
  };
  root.userData.animate(0, false);
  root.updateMatrixWorld(true);
  const bounds = new THREE.Box3().setFromObject(root);
  root.userData.height = bounds.max.y;
  root.userData.bounds = { min: bounds.min.toArray(), max: bounds.max.toArray() };
  return root;
}
