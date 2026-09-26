import * as THREE from 'three';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';
import { lockers } from '../shared/world-data.js';
import { DEFAULT_PROPS } from '../shared/props.js';
import { createSurvivor } from './characters.js';

// Every texture and model in the station is built locally. No downloaded assets.
let seed = 91821;
const rnd = () => { seed = (seed * 1664525 + 1013904223) >>> 0; return seed / 4294967296; };
const palette = {
  plaster: '#706f57', floor: '#424a41', wood: '#503e29', metal: '#3d5148',
  rust: '#694032', fabric: '#696150', blood: '#4d1210', server: '#222926'
};
const shared = {};
const colorValue = (n) => Math.max(0, Math.min(255, n));

function texture(kind, size = 128) {
  const c = document.createElement('canvas'); c.width = c.height = size;
  const x = c.getContext('2d'); x.fillStyle = palette[kind] || '#6b6c56'; x.fillRect(0, 0, size, size);
  const data = x.getImageData(0, 0, size, size);
  for (let i = 0; i < data.data.length; i += 4) {
    const n = (rnd() - .5) * (kind === 'plaster' ? 43 : 30);
    for (let j = 0; j < 3; j++) data.data[i+j] = colorValue(data.data[i+j] + n);
  }
  x.putImageData(data, 0, 0);
  if (kind === 'floor') {
    x.strokeStyle = '#252c29'; x.lineWidth = 3;
    for (let k = 0; k <= size; k += 32) { x.beginPath(); x.moveTo(k,0); x.lineTo(k,size); x.moveTo(0,k); x.lineTo(size,k); x.stroke(); }
    for (let i = 0; i < 28; i++) { x.fillStyle = `rgba(9,15,12,${rnd()*.23})`; x.fillRect(rnd()*size,rnd()*size,4+rnd()*15,2+rnd()*8); }
  } else if (kind === 'wood') {
    for (let i = 0; i < 110; i++) { x.strokeStyle = `rgba(${rnd()>.5?'9,5,0':'140,110,65'},${.1+rnd()*.35})`; x.beginPath(); const y=rnd()*size; x.moveTo(0,y); x.bezierCurveTo(35,y+rnd()*5,70,y-rnd()*5,size,y+2); x.stroke(); }
    x.fillStyle = '#17190f'; for(let i=0;i<4;i++) x.fillRect(i*32,0,2,size);
  } else if (kind === 'plaster') {
    for (let i=0;i<40;i++) { x.fillStyle=`rgba(15,24,16,${.025+rnd()*.045})`; x.fillRect(rnd()*size,rnd()*size,10+rnd()*35,15+rnd()*60); }
    for (let i=0;i<4;i++) { let px=rnd()*size,py=rnd()*size; x.strokeStyle='#444a3a'; x.beginPath(); x.moveTo(px,py); for(let j=0;j<5;j++){px+=(rnd()-.5)*13;py+=rnd()*9;x.lineTo(px,py);} x.stroke(); }
  } else if (kind === 'metal' || kind === 'rust') {
    for(let i=0;i<95;i++){x.fillStyle=`rgba(${kind==='rust'?'180,84,31':'17,27,20'},${rnd()*.35})`; x.fillRect(rnd()*size,rnd()*size,1+rnd()*12,1+rnd()*3);}
    x.strokeStyle='#1d2723'; x.strokeRect(2,2,size-4,size-4); x.fillStyle='#999276';
    [[6,6],[size-7,6],[6,size-7],[size-7,size-7]].forEach(([a,b])=>x.fillRect(a,b,2,2));
  } else if (kind === 'blood') {
    for(let i=0;i<120;i++){x.fillStyle=`rgba(${120+rnd()*100},${10+rnd()*25},${10+rnd()*25},${.3+rnd()*.6})`; x.beginPath(); x.arc(rnd()*size,rnd()*size,2+rnd()*14,0,Math.PI*2); x.fill();}
  } else {
    x.fillStyle='rgba(0,0,0,.14)'; for(let i=0;i<size;i+=3)x.fillRect(0,i,size,1);
  }
  const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace; t.magFilter = THREE.NearestFilter; t.minFilter = THREE.NearestMipmapNearestFilter; t.wrapS=t.wrapT=THREE.RepeatWrapping;
  return t;
}

function mat(kind, color = 0xffffff, repeat = 1) {
  const key = `${kind}-${color}-${repeat}`;
  if (!shared[key]) {
    const map=texture(kind); map.repeat.set(repeat,repeat);
    shared[key]=new THREE.MeshStandardMaterial({map,color,roughness:.94,metalness:kind==='metal'?.3:.04,flatShading:true});
  }
  return shared[key];
}
const plain = (color, roughness = .85) => {
  const key=`plain-${color}-${roughness}`;
  if(!shared[key]) shared[key]=new THREE.MeshStandardMaterial({color,roughness,flatShading:true});
  return shared[key];
};
const glow = (color, intensity = 1) => new THREE.MeshStandardMaterial({color,emissive:color,emissiveIntensity:intensity,roughness:.8});

function box(parent, x,y,z, w,h,d, material, ry=0) {
  const m=new THREE.Mesh(new THREE.BoxGeometry(w,h,d),material); m.position.set(x,y,z); m.rotation.y=ry; m.receiveShadow=true; parent.add(m); return m;
}
function cyl(parent,x,y,z,radius,height,material,segments=8,rotationZ=0) {
  const m=new THREE.Mesh(new THREE.CylinderGeometry(radius,radius,height,segments),material); m.position.set(x,y,z); m.rotation.z=rotationZ; parent.add(m); return m;
}
function lineTube(parent,points,radius,material) {
  const curve=new THREE.CatmullRomCurve3(points.map(p=>new THREE.Vector3(...p)));
  const m=new THREE.Mesh(new THREE.TubeGeometry(curve,Math.max(6,points.length*3),radius,4,false),material); parent.add(m); return m;
}

function textTexture(lines, { bg='#131c18',fg='#c1c4a4',width=256,height=128,font=24,align='center',noise=true } = {}) {
  const c=document.createElement('canvas'); c.width=width;c.height=height; const ctx=c.getContext('2d');
  ctx.fillStyle=bg;ctx.fillRect(0,0,width,height);ctx.fillStyle=fg;ctx.textAlign=align;ctx.textBaseline='middle';ctx.font=`bold ${font}px monospace`;
  lines.forEach((line,i)=>ctx.fillText(line,align==='center'?width/2:12,height/2+(i-(lines.length-1)/2)*(font+6)));
  if(noise){ctx.fillStyle='rgba(0,0,0,.14)';for(let y=0;y<height;y+=3)ctx.fillRect(0,y,width,1);for(let i=0;i<100;i++){ctx.fillStyle=`rgba(8,9,7,${rnd()*.2})`;ctx.fillRect(rnd()*width,rnd()*height,rnd()*12,1);}}
  const tx=new THREE.CanvasTexture(c);tx.colorSpace=THREE.SRGBColorSpace;tx.magFilter=THREE.NearestFilter;tx.minFilter=THREE.LinearFilter;return tx;
}
function label(parent,lines,x,y,z,w,h,ry=0,options={}) {
  const tx=textTexture(lines,options); const mesh=new THREE.Mesh(new THREE.PlaneGeometry(w,h),new THREE.MeshBasicMaterial({map:tx,side:THREE.DoubleSide,toneMapped:false}));mesh.position.set(x,y,z);mesh.rotation.y=ry;parent.add(mesh);return mesh;
}

function table(parent,x,z,w=2,d=1,rotation=0) {
  const group=new THREE.Group();group.position.set(x,0,z);group.rotation.y=rotation;parent.add(group);
  box(group,0,.84,0,w,.12,d,mat('wood'));
  for(const dx of [-w/2+.09,w/2-.09])for(const dz of [-d/2+.09,d/2-.09])box(group,dx,.4,dz,.08,.8,.08,mat('metal'));
  box(group,0,.54,-d/2+.05,w-.1,.3,.07,mat('wood'));
  return group;
}
function chair(parent,x,z,rotation=0) {
  const g=new THREE.Group();g.position.set(x,0,z);g.rotation.y=rotation;parent.add(g);
  box(g,0,.5,0,.62,.12,.62,mat('fabric',0x788164));box(g,0,.94,-.28,.63,.64,.12,mat('fabric',0x788164));
  for(const dx of [-.24,.24])for(const dz of [-.24,.24])box(g,dx,.25,dz,.045,.5,.045,mat('metal'));
  return g;
}

// Interactive Locker model with ventilation louvers for hiding
function locker(parent, x, z, ry = 0, id = 'locker-1') {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = ry; parent.add(g);
  const metalMat = mat('metal', 0x5a6858);
  const darkMetal = mat('rust', 0x3d453b);
  const handleMat = plain(0xb0a88b);

  // Main locker body
  box(g, 0, 1.1, 0, 0.8, 2.2, 0.65, metalMat);
  box(g, 0, 1.1, 0.33, 0.74, 2.12, 0.04, darkMetal);

  // Ventilation louvers / slits (where player peers out)
  for (let i = 0; i < 6; i++) {
    box(g, 0, 1.65 + i * 0.05, 0.355, 0.44, 0.016, 0.02, plain(0x0e1410));
    box(g, 0, 0.45 + i * 0.05, 0.355, 0.44, 0.016, 0.02, plain(0x0e1410));
  }
  // Door handle and latch
  box(g, 0.3, 1.1, 0.365, 0.04, 0.18, 0.045, handleMat);
  box(g, 0.3, 1.18, 0.385, 0.02, 0.03, 0.025, plain(0x19211c));

  label(g, ['№ 0' + id.slice(-1)], 0, 1.98, 0.355, 0.3, 0.1, 0, { font: 24, width: 96, height: 32, bg: '#1c241c', fg: '#b2ba9e' });

  g.userData.id = id;
  return g;
}

function crt(parent,x,y,z,scale=1,on=true) {
  const g=new THREE.Group();g.position.set(x,y,z);g.scale.setScalar(scale);parent.add(g);
  box(g,0,0,0,.78,.63,.64,mat('metal',0x8d9383));box(g,-.04,.035,.328,.59,.44,.018,plain(0x080f0c));
  const screen=label(g,on?['КАНАЛ 04','НЕМАЄ СИГНАЛУ']:[''], -.04,.035,.34,.54,.39,0,{width:128,height:96,font:9,bg:on?'#243d30':'#08100d',fg:'#719271'});
  box(g,.32,-.12,.34,.055,.055,.035,plain(0x29342c));box(g,.32,.0,.34,.04,.11,.035,plain(0x171b17));
  box(g,0,-.38,0,.36,.13,.31,mat('metal'));
  return {group:g,screen};
}

function cassette(parent,x,y,z,rotation=0,interactive=false) {
  const g=new THREE.Group();g.position.set(x,y,z);g.rotation.y=rotation;parent.add(g);
  box(g,0,0,0,.43,.085,.27,plain(0x151815));box(g,0,.045,-.025,.31,.007,.1,plain(interactive?0xc4b579:0x83836b));
  for(const side of [-1,1]){const c=cyl(g,side*.105,.047,.055,.045,.012,plain(0xaaa38a),8);c.rotation.z=0;}
  if(interactive){box(g,0,-.035,0,.455,.018,.29,glow(0x9ea879,.7));const l=new THREE.PointLight(0xc4d38a,.8,1.5,2);l.position.y=.15;g.add(l);g.userData.light=l;}
  return g;
}

function shelf(parent,x,z,rotation=0) {
  const g=new THREE.Group();g.position.set(x,0,z);g.rotation.y=rotation;parent.add(g);
  for(const dx of [-1,1])box(g,dx,1.13,0,.065,2.26,.5,mat('rust'));
  for(let row=0;row<5;row++){
    const y=.2+row*.45;box(g,0,y,0,2.12,.065,.59,mat('wood'));
    for(let j=0;j<8;j++){if(rnd()>.2){const m=box(g,-.88+j*.235,y+.16,(rnd()-.5)*.12,.15,.27,.28,plain(j%3===0?0x665b44:0x303b34));box(g,m.position.x,y+.17,.17,.11,.08,.012,plain(0xaca586));}}
  }
  return g;
}

// 1990s Mainframe Server Rack
function serverRack(parent, x, z, rotation = 0) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = rotation; parent.add(g);
  const rackMat = mat('metal', 0x242b26);
  box(g, 0, 1.25, 0, 0.9, 2.5, 0.85, rackMat);
  for (let u = 0; u < 5; u++) {
    const y = 0.45 + u * 0.42;
    box(g, 0, y, 0.43, 0.78, 0.36, 0.02, plain(0x181f1b));
    for (let led = 0; led < 6; led++) {
      box(g, -0.3 + led * 0.12, y + 0.12, 0.445, 0.02, 0.02, 0.01, glow(led % 2 ? 0x44dd66 : 0xdd9922, 1.5));
    }
  }
  return g;
}

function recordingCamera(parent,x,z,rotation=0) {
  const g=new THREE.Group();g.position.set(x,0,z);g.rotation.y=rotation;parent.add(g);
  cyl(g,0,.76,0,.045,1.5,mat('metal'));for(let i=0;i<3;i++){const a=i/3*Math.PI*2;lineTube(g,[[0,.85,0],[Math.sin(a)*.55,.05,Math.cos(a)*.55]],.026,mat('metal'));}
  box(g,0,1.63,0,.55,.44,.9,mat('metal',0x89937c));box(g,0,1.64,.52,.35,.26,.21,plain(0x111915));
  const lens=cyl(g,0,1.64,.7,.14,.16,plain(0x080e0c),10);lens.rotation.x=Math.PI/2;box(g,-.35,1.72,-.18,.17,.13,.38,mat('metal'));
  label(g,['ТК–04'],.282,1.63,-.06,.42,.16,Math.PI/2,{font:25,width:128,height:48});box(g,0,1.91,.25,.075,.04,.04,glow(0xe51f12,2));return g;
}

function createOlenaFaceTexture() {
  const c = document.createElement('canvas'); c.width = 128; c.height = 128;
  const ctx = c.getContext('2d');
  ctx.fillStyle = '#eddccb'; ctx.fillRect(0, 0, 128, 128);

  // Soft cheekbone blush
  const blushL = ctx.createRadialGradient(28, 76, 2, 28, 76, 28);
  blushL.addColorStop(0, 'rgba(214, 110, 125, 0.45)'); blushL.addColorStop(1, 'rgba(214, 110, 125, 0)');
  ctx.fillStyle = blushL; ctx.fillRect(0, 50, 60, 50);
  const blushR = ctx.createRadialGradient(100, 76, 2, 100, 76, 28);
  blushR.addColorStop(0, 'rgba(214, 110, 125, 0.45)'); blushR.addColorStop(1, 'rgba(214, 110, 125, 0)');
  ctx.fillStyle = blushR; ctx.fillRect(68, 50, 60, 50);

  // Arched sleek dark brows
  ctx.strokeStyle = '#2b211d'; ctx.lineWidth = 3.5; ctx.lineCap = 'round';
  ctx.beginPath(); ctx.moveTo(22, 42); ctx.quadraticCurveTo(38, 34, 52, 42); ctx.stroke();
  ctx.beginPath(); ctx.moveTo(76, 42); ctx.quadraticCurveTo(90, 34, 106, 42); ctx.stroke();

  // Dramatic winged eyeliner & sultry eyes
  for (const [lx, rx] of [[24, 50], [78, 104]]) {
    ctx.fillStyle = '#f8f8f5';
    ctx.beginPath(); ctx.ellipse((lx + rx) / 2, 53, (rx - lx) / 2, 6.5, 0, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = '#1c1817';
    ctx.beginPath(); ctx.arc((lx + rx) / 2, 53, 5, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = '#4a3d36';
    ctx.beginPath(); ctx.arc((lx + rx) / 2, 53, 3, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = '#111111';
    ctx.beginPath(); ctx.arc((lx + rx) / 2, 53, 1.8, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = '#ffffff';
    ctx.beginPath(); ctx.arc((lx + rx) / 2 - 1.5, 51, 1.2, 0, Math.PI * 2); ctx.fill();
    // Heavy winged upper eyeliner line
    ctx.strokeStyle = '#111111'; ctx.lineWidth = 3;
    ctx.beginPath(); ctx.moveTo(lx - 2, 54); ctx.quadraticCurveTo((lx + rx) / 2, 47, rx + 4, 50); ctx.stroke();
  }

  // Delicate nose bridge hint
  ctx.fillStyle = 'rgba(180, 140, 120, 0.4)';
  ctx.fillRect(62, 62, 4, 14);

  // Glamorous Burgundy / Wine Matte Lips (#821428)
  ctx.fillStyle = '#821428';
  ctx.beginPath();
  ctx.moveTo(42, 94);
  ctx.quadraticCurveTo(54, 88, 64, 91);
  ctx.quadraticCurveTo(74, 88, 86, 94);
  ctx.quadraticCurveTo(64, 108, 42, 94);
  ctx.fill();
  ctx.strokeStyle = '#540816'; ctx.lineWidth = 1.5; ctx.stroke();
  ctx.fillStyle = 'rgba(255, 180, 190, 0.55)';
  ctx.beginPath(); ctx.ellipse(64, 98, 9, 3, 0, 0, Math.PI * 2); ctx.fill();

  // Vintage 90s beauty mark
  ctx.fillStyle = '#221512';
  ctx.beginPath(); ctx.arc(77, 85, 2, 0, Math.PI * 2); ctx.fill();

  const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace;
  return t;
}

function createMariaFaceTexture() {
  const c = document.createElement('canvas'); c.width = 128; c.height = 128;
  const ctx = c.getContext('2d');
  // Warm golden-olive tan skin
  ctx.fillStyle = '#dca672'; ctx.fillRect(0, 0, 128, 128);

  // Cheek blush
  ctx.fillStyle = 'rgba(224, 92, 108, 0.35)';
  ctx.beginPath(); ctx.ellipse(26, 75, 18, 10, 0, 0, Math.PI * 2); ctx.fill();
  ctx.beginPath(); ctx.ellipse(102, 75, 18, 10, 0, 0, Math.PI * 2); ctx.fill();

  // Dramatic arched 70s eyebrows
  ctx.strokeStyle = '#23140d'; ctx.lineWidth = 4; ctx.lineCap = 'round';
  ctx.beginPath(); ctx.moveTo(18, 38); ctx.quadraticCurveTo(36, 26, 56, 38); ctx.stroke();
  ctx.beginPath(); ctx.moveTo(72, 38); ctx.quadraticCurveTo(92, 26, 110, 38); ctx.stroke();

  // Vibrant Pink/Magenta Eyeshadow above lids
  ctx.fillStyle = '#d94178';
  ctx.beginPath(); ctx.ellipse(37, 46, 18, 6.5, -0.05, 0, Math.PI * 2); ctx.fill();
  ctx.beginPath(); ctx.ellipse(91, 46, 18, 6.5, 0.05, 0, Math.PI * 2); ctx.fill();

  // Big stylized almond Psychonauts eyes
  for (const cx of [37, 91]) {
    ctx.fillStyle = '#ffffff';
    ctx.beginPath(); ctx.ellipse(cx, 54, 16, 10, 0, 0, Math.PI * 2); ctx.fill();
    const irisGrad = ctx.createRadialGradient(cx, 54, 1, cx, 54, 9);
    irisGrad.addColorStop(0, '#3ae678'); irisGrad.addColorStop(0.7, '#149e48'); irisGrad.addColorStop(1, '#095c27');
    ctx.fillStyle = irisGrad;
    ctx.beginPath(); ctx.arc(cx, 54, 8.5, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = '#08170c';
    ctx.beginPath(); ctx.arc(cx, 54, 4.2, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = '#ffffff';
    ctx.beginPath(); ctx.arc(cx - 2.5, 51.5, 2.5, 0, Math.PI * 2); ctx.fill();
    // Heavy black winged lashes
    ctx.strokeStyle = '#170f0a'; ctx.lineWidth = 3.5;
    ctx.beginPath(); ctx.moveTo(cx - 18, 56); ctx.quadraticCurveTo(cx, 44, cx + 18, 50); ctx.stroke();
  }

  // Nose tip
  ctx.fillStyle = '#bf8756';
  ctx.beginPath(); ctx.arc(64, 71, 3.5, 0, Math.PI * 2); ctx.fill();

  // Big Joyful Wide Smile (Milla Vodello signature)
  ctx.fillStyle = '#d93654'; // coral-red lips
  ctx.beginPath();
  ctx.moveTo(30, 86);
  ctx.quadraticCurveTo(64, 80, 98, 86);
  ctx.quadraticCurveTo(64, 114, 30, 86);
  ctx.fill();
  ctx.fillStyle = '#540d1c';
  ctx.beginPath();
  ctx.moveTo(36, 88);
  ctx.quadraticCurveTo(64, 84, 92, 88);
  ctx.quadraticCurveTo(64, 108, 36, 88);
  ctx.fill();
  // Pearly white upper teeth
  ctx.fillStyle = '#ffffff';
  ctx.beginPath();
  ctx.moveTo(39, 88); ctx.lineTo(89, 88); ctx.lineTo(85, 94); ctx.lineTo(43, 94); ctx.closePath();
  ctx.fill();
  // Pink tongue
  ctx.fillStyle = '#e8617d';
  ctx.beginPath(); ctx.arc(64, 104, 8, Math.PI, 0); ctx.fill();

  const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace;
  return t;
}

function createTarasFaceTexture() {
  const c = document.createElement('canvas'); c.width = 128; c.height = 128;
  const ctx = c.getContext('2d');
  // Distinctive Psychonauts yellow security guard skin
  ctx.fillStyle = '#e8c467'; ctx.fillRect(0, 0, 128, 128);

  // Frantic paranoid furrowed eyebrows
  ctx.strokeStyle = '#292113'; ctx.lineWidth = 4.5; ctx.lineCap = 'round';
  ctx.beginPath(); ctx.moveTo(22, 38); ctx.lineTo(54, 46); ctx.stroke();
  ctx.beginPath(); ctx.moveTo(106, 38); ctx.lineTo(74, 46); ctx.stroke();

  // Wide bulging paranoid eyes
  for (const [cx, pupilOff] of [[38, 2], [90, 2]]) {
    ctx.fillStyle = '#fffdf7';
    ctx.beginPath(); ctx.ellipse(cx, 55, 16, 15, 0, 0, Math.PI * 2); ctx.fill();
    ctx.strokeStyle = '#382a17'; ctx.lineWidth = 2.5; ctx.stroke();
    ctx.strokeStyle = 'rgba(210, 50, 40, 0.4)'; ctx.lineWidth = 1;
    ctx.beginPath(); ctx.moveTo(cx - 13, 56); ctx.lineTo(cx - 7, 54); ctx.stroke();
    ctx.beginPath(); ctx.moveTo(cx + 13, 53); ctx.lineTo(cx + 6, 56); ctx.stroke();
    ctx.fillStyle = '#111111';
    ctx.beginPath(); ctx.arc(cx + pupilOff, 55, 5.5, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = '#ffffff';
    ctx.beginPath(); ctx.arc(cx + pupilOff - 1.5, 53, 1.8, 0, Math.PI * 2); ctx.fill();
    // Heavy droopy under-eye bags
    ctx.strokeStyle = '#7c653d'; ctx.lineWidth = 2.2;
    ctx.beginPath(); ctx.arc(cx, 63, 15, 0.2 * Math.PI, 0.8 * Math.PI); ctx.stroke();
  }

  // Guard nose
  ctx.fillStyle = '#caa346';
  ctx.beginPath(); ctx.ellipse(64, 70, 7, 10, 0, 0, Math.PI * 2); ctx.fill();
  ctx.strokeStyle = '#634f1e'; ctx.lineWidth = 2; ctx.stroke();

  // Greenish-grey 5 o'clock stubble across jaw
  ctx.fillStyle = 'rgba(102, 107, 72, 0.45)';
  ctx.fillRect(18, 82, 92, 42);

  // Tightly clamped grimacing mouth
  ctx.strokeStyle = '#291b12'; ctx.lineWidth = 3.5; ctx.lineCap = 'round';
  ctx.beginPath(); ctx.moveTo(40, 94); ctx.quadraticCurveTo(64, 91, 88, 93); ctx.stroke();

  // The iconic spiral swirl on the chin from Boyd Cooper!
  ctx.strokeStyle = '#544c2a'; ctx.lineWidth = 2.5;
  ctx.beginPath();
  let angle = 0;
  for (let r = 2; r < 12; r += 0.5) {
    angle += 0.35;
    const x = 64 + Math.cos(angle) * r;
    const y = 111 + Math.sin(angle) * r;
    if (r === 2) ctx.moveTo(x, y); else ctx.lineTo(x, y);
  }
  ctx.stroke();

  const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace;
  return t;
}

function createBogdanFaceTexture() {
  const c = document.createElement('canvas'); c.width = 128; c.height = 128;
  const ctx = c.getContext('2d');
  // Weathered, sculptured clay skin
  ctx.fillStyle = '#c7a68a'; ctx.fillRect(0, 0, 128, 128);

  // Heavy wrinkled forehead furrows
  ctx.strokeStyle = '#6e5444'; ctx.lineWidth = 2.2; ctx.lineCap = 'round';
  ctx.beginPath(); ctx.moveTo(26, 32); ctx.quadraticCurveTo(64, 28, 102, 32); ctx.stroke();
  ctx.beginPath(); ctx.moveTo(34, 40); ctx.quadraticCurveTo(64, 36, 94, 40); ctx.stroke();

  // Weary hooded sunken eye sockets looking upward
  for (const cx of [40, 88]) {
    ctx.fillStyle = 'rgba(78, 54, 44, 0.5)';
    ctx.beginPath(); ctx.ellipse(cx, 55, 15, 12, 0, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = '#eae2d6';
    ctx.beginPath(); ctx.ellipse(cx, 53, 10, 5, 0, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = '#261b17';
    ctx.beginPath(); ctx.arc(cx, 51.5, 3.5, 0, Math.PI * 2); ctx.fill();
    ctx.fillStyle = '#ffffff';
    ctx.beginPath(); ctx.arc(cx - 1, 50.5, 1.2, 0, Math.PI * 2); ctx.fill();
    ctx.strokeStyle = '#422c22'; ctx.lineWidth = 2.5;
    ctx.beginPath(); ctx.moveTo(cx - 12, 53); ctx.quadraticCurveTo(cx, 48, cx + 12, 53); ctx.stroke();
  }

  // Prominent sculptural nose
  ctx.fillStyle = '#ab8567';
  ctx.beginPath();
  ctx.moveTo(60, 48); ctx.lineTo(68, 48); ctx.lineTo(73, 76); ctx.lineTo(55, 76); ctx.closePath();
  ctx.fill();
  ctx.strokeStyle = '#5a3d2c'; ctx.lineWidth = 2; ctx.stroke();

  // Weary downturned mouth
  ctx.strokeStyle = '#382218'; ctx.lineWidth = 3; ctx.lineCap = 'round';
  ctx.beginPath(); ctx.moveTo(42, 98); ctx.quadraticCurveTo(64, 93, 86, 98); ctx.stroke();

  // Stubble shading
  ctx.fillStyle = 'rgba(70, 56, 48, 0.28)';
  ctx.fillRect(30, 86, 68, 36);

  const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace;
  return t;
}

function createTweedTexture() {
  const c = document.createElement('canvas'); c.width = 128; c.height = 128;
  const ctx = c.getContext('2d');
  ctx.fillStyle = '#1e2420'; ctx.fillRect(0, 0, 128, 128);
  for (let y = 0; y < 128; y += 4) {
    for (let x = 0; x < 128; x += 4) {
      const v = ((x ^ y) % 5 === 0) ? '#2d382f' : ((x + y) % 7 === 0) ? '#384236' : ((x * y) % 11 === 0) ? '#4a3d2e' : '#1e2420';
      ctx.fillStyle = v;
      ctx.fillRect(x, y, 3, 3);
    }
  }
  const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace;
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.repeat.set(3, 3);
  return t;
}

function createPsychDressTexture() {
  const c = document.createElement('canvas'); c.width = 256; c.height = 256;
  const ctx = c.getContext('2d');
  ctx.fillStyle = '#e85d22'; ctx.fillRect(0, 0, 256, 256);
  const colors = ['#e6286b', '#f58220', '#fad12a', '#d92b52', '#a8208a', '#f59b2a'];
  for (let i = 0; i < 16; i++) {
    ctx.fillStyle = colors[i % colors.length];
    ctx.beginPath();
    const y = i * 16;
    ctx.moveTo(0, y);
    ctx.bezierCurveTo(64, y + 20, 128, y - 18, 256, y + 10);
    ctx.lineTo(256, y + 24);
    ctx.bezierCurveTo(128, y + 8, 64, y + 42, 0, y + 24);
    ctx.closePath();
    ctx.fill();
  }
  const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace;
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  return t;
}

function createStripedTexture() {
  const c = document.createElement('canvas'); c.width = 64; c.height = 64;
  const ctx = c.getContext('2d');
  ctx.fillStyle = '#f5efe6'; ctx.fillRect(0, 0, 64, 64);
  for (let x = 3; x < 64; x += 12) {
    ctx.fillStyle = '#992222'; ctx.fillRect(x, 0, 1.5, 64);
    ctx.fillStyle = '#1e3845'; ctx.fillRect(x + 3, 0, 1.5, 64);
  }
  const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace;
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  return t;
}

function createFloralTieTexture() {
  const c = document.createElement('canvas'); c.width = 128; c.height = 256;
  const ctx = c.getContext('2d');
  ctx.fillStyle = '#9e7428'; ctx.fillRect(0, 0, 128, 256);
  const roseColors = ['#7c1818', '#a82424', '#d94838', '#540f0f'];
  const leafColors = ['#284226', '#3b5c35', '#576e43', '#1b2b18'];
  const goldColors = ['#d4ad3c', '#eed26e', '#b58c24'];

  for (let y = 8; y < 256; y += 22) {
    for (let x = 8; x < 128; x += 22) {
      const off = ((Math.floor(y / 22) % 2) * 11);
      const px = (x + off) % 128;
      ctx.strokeStyle = '#223820'; ctx.lineWidth = 1.8;
      ctx.beginPath(); ctx.moveTo(px - 6, y - 6); ctx.quadraticCurveTo(px, y, px + 8, y + 8); ctx.stroke();
      ctx.fillStyle = leafColors[(x + y) % leafColors.length];
      ctx.beginPath(); ctx.ellipse(px - 5, y + 3, 4, 2, 0.6, 0, Math.PI * 2); ctx.fill();
      ctx.beginPath(); ctx.ellipse(px + 6, y - 3, 4, 2, -0.6, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = roseColors[(x * 3 + y) % roseColors.length];
      ctx.beginPath(); ctx.arc(px, y, 5, 0, Math.PI * 2); ctx.fill();
      ctx.fillStyle = goldColors[(x + y * 2) % goldColors.length];
      ctx.beginPath(); ctx.arc(px + 1.2, y - 1, 2.2, 0, Math.PI * 2); ctx.fill();
    }
  }
  const t = new THREE.CanvasTexture(c); t.colorSpace = THREE.SRGBColorSpace;
  t.wrapS = t.wrapT = THREE.RepeatWrapping;
  t.repeat.set(1, 2);
  return t;
}

let olenaFaceMat = null, mariaFaceMat = null, tarasFaceMat = null, bogdanFaceMat = null;
let tweedMat = null, psychDressMat = null, stripedMat = null, floralTieMat = null;

export function createCharacter(color=0x737e60, monster=false, bloody=false, charClass='engineer') {
  if (!monster) return createSurvivor(color, charClass);
  const g=new THREE.Group();
  const cVal = typeof color === 'string' ? (color.startsWith('#') ? parseInt(color.slice(1), 16) : parseInt(color, 16)) : color;
  const clothColor = monster ? (bloody ? 0x481110 : 0x272c24) : cVal;
  const cloth = mat('fabric', clothColor);
  const skin = plain(monster ? (bloody ? 0xb59388 : 0xc5bda0) : 0xb4a48a);
  const dark = plain(0x111815);
  const limbs = [];

  if(monster){
    // Creepier Monster proportions: elongated, asymmetric, distorted
    box(g,0,1.56,0,.54,1.15,.32,cloth);
    box(g,0,2.15,0,.78,.32,.36,cloth);
    box(g,0,1.02,0,.58,.25,.35,cloth);

    if (bloody) {
      box(g, 0, 1.6, 0.165, 0.44, 0.75, 0.02, glow(0x780e0e, 1.2));
      box(g, -0.1, 1.35, 0.17, 0.18, 0.4, 0.02, plain(0x500a0a));
    }

    const head=new THREE.Group();head.position.set(0,2.62,.08);g.add(head);
    box(head,0,0,0,.31,.74,.31,skin);
    box(head,0,.22,.16,.31,.14,.02,plain(0x222620));

    const eyeMat = glow(bloody ? 0xff1808 : 0xb0cfaa, bloody ? 3.5 : 1.2);
    for(const x of [-.085,.085]) box(head, x, .18, .172, .06, .038, .022, eyeMat);

    box(head,0,-.19,.165,.14,.34,.025,dark);
    box(head,0,-.24,.188,.028,.22,.012,plain(bloody ? 0x881111 : 0x888373));

    if (bloody) {
      box(head, 0.08, -0.12, 0.17, 0.12, 0.28, 0.02, plain(0x6b0d0d));
    }

    for(const s of [-1,1]){
      const arm=new THREE.Group();arm.position.set(s*.42,2.12,0);g.add(arm);
      box(arm,0,-.55,0,.15,1.2,.16,cloth);
      box(arm,s*.028,-1.2,.04,.14,.32,.13,skin);
      const clawMat = bloody ? glow(0x6b1010, 1.2) : skin;
      for(let i=0;i<4;i++) box(arm,(i-1.5)*.04,-1.42,.07,.024,.26,.028,clawMat);
      arm.rotation.z=s*.14;

      const leg=new THREE.Group();leg.position.set(s*.18,1.02,0);g.add(leg);
      box(leg,0,-.48,0,.16,1.04,.18,cloth);
      box(leg,0,-1.05,.08,.19,.12,.38,dark);
      limbs.push({arm,leg,s});
    }

    g.userData.animate=(time,moving)=>{
      const speedMult = bloody ? 4.8 : 2.8;
      const phase = time * speedMult;
      for(const {arm,leg,s}of limbs){
        leg.rotation.x = moving ? Math.sin(phase)*s*.42 : 0;
        arm.rotation.x = moving ? -Math.sin(phase+.5)*s*.28 : Math.sin(time*.7+s)*.05;
      }
      const twitch = Math.sin(time * 19) > (bloody ? 0.94 : 0.985) ? (Math.random() - 0.5) * 0.45 : 0;
      head.rotation.z = Math.sin(time*.95)*.14 + twitch;
      head.rotation.y = Math.sin(time*.48)*.18;
      g.userData.head=head;
    };
  } else {
    if (!olenaFaceMat) olenaFaceMat = new THREE.MeshStandardMaterial({ map: createOlenaFaceTexture(), roughness: 0.8 });
    if (!mariaFaceMat) mariaFaceMat = new THREE.MeshStandardMaterial({ map: createMariaFaceTexture(), roughness: 0.8 });
    if (!tarasFaceMat) tarasFaceMat = new THREE.MeshStandardMaterial({ map: createTarasFaceTexture(), roughness: 0.85 });
    if (!bogdanFaceMat) bogdanFaceMat = new THREE.MeshStandardMaterial({ map: createBogdanFaceTexture(), roughness: 0.85 });
    if (!tweedMat) tweedMat = new THREE.MeshStandardMaterial({ map: createTweedTexture(), roughness: 0.9 });
    if (!psychDressMat) psychDressMat = new THREE.MeshStandardMaterial({ map: createPsychDressTexture(), roughness: 0.85 });
    if (!stripedMat) stripedMat = new THREE.MeshStandardMaterial({ map: createStripedTexture(), roughness: 0.85 });
    if (!floralTieMat) floralTieMat = new THREE.MeshStandardMaterial({ map: createFloralTieTexture(), roughness: 0.85 });

    const cls = charClass || 'engineer';

    if (cls === 'operator') {
      // 1. ОЛЕНА (Операторка): Platinum bob with straight bangs, gold hoop earrings, 3D pearl choker,
      // burgundy slip dress with dark bustier, black sheer tights, cigarette with live animated curling smoke!
      const opSkin = plain(0xeddccb);
      const bobHair = plain(0xf6f3ea);
      const slipMat = mat('fabric', 0x4d1624);
      const bustierMat = plain(0x191e1a);
      const goldMat = mat('metal', 0xe0b63e);
      const stockingMat = plain(0x181818);

      // Torso / Burgundy slip dress with sweetheart bustier & peplum hem
      box(g, 0, 1.16, 0, 0.38, 0.44, 0.25, slipMat);
      box(g, 0, 1.35, 0.012, 0.36, 0.13, 0.255, bustierMat);
      // Sweetheart cleavage cutout
      box(g, 0, 1.38, 0.125, 0.14, 0.07, 0.04, opSkin);
      box(g, 0, 0.86, 0, 0.44, 0.22, 0.29, slipMat); // peplum skirt flare

      // Slender neck
      cyl(g, 0, 1.47, 0, 0.062, 0.16, opSkin, 10);

      // 3D Pearl Choker Necklace: 7 individual pearl spheres around the neck
      const pearlMat = plain(0xfdfcf8, 0.3);
      for (let p = 0; p < 7; p++) {
        const ang = (p - 3) * 0.36;
        const px = Math.sin(ang) * 0.078;
        const pz = Math.cos(ang) * 0.078;
        const bead = new THREE.Mesh(new THREE.SphereGeometry(0.016, 8, 8), pearlMat);
        bead.position.set(px, 1.43, pz);
        g.add(bead);
      }

      // Head with bespoke painted face texture
      const head = new THREE.Group(); head.position.set(0, 1.62, 0); g.add(head);
      box(head, 0, 0, 0, 0.25, 0.29, 0.24, opSkin);
      // Front face decal
      const faceMesh = new THREE.Mesh(new THREE.PlaneGeometry(0.24, 0.28), olenaFaceMat);
      faceMesh.position.set(0, 0, 0.122);
      head.add(faceMesh);

      // 3D Polished Gold Hoop Earrings
      for (const s of [-1, 1]) {
        const hoop = new THREE.Mesh(new THREE.TorusGeometry(0.044, 0.007, 8, 16), goldMat);
        hoop.position.set(s * 0.14, -0.04, 0.01);
        hoop.rotation.y = Math.PI / 2;
        head.add(hoop);
      }

      // Platinum blonde bob with straight blunt bangs
      box(head, 0, 0.16, -0.01, 0.28, 0.09, 0.28, bobHair); // top crown
      box(head, 0, 0.07, 0.134, 0.27, 0.12, 0.024, bobHair); // blunt bangs covering brow
      for (const s of [-1, 1]) box(head, s * 0.14, -0.02, 0.01, 0.035, 0.27, 0.27, bobHair); // angled sides
      box(head, 0, 0.01, -0.135, 0.28, 0.27, 0.04, bobHair); // back

      // Legs in black sheer tights & pointed stilettos
      for (const s of [-1, 1]) {
        const leg = new THREE.Group(); leg.position.set(s * 0.125, 0.78, 0); g.add(leg);
        box(leg, 0, -0.32, 0, 0.14, 0.64, 0.17, stockingMat);
        // Pointed high heel shoe
        box(leg, 0, -0.68, 0.04, 0.13, 0.10, 0.26, plain(0x0a0a0a));
        box(leg, 0, -0.71, 0.14, 0.09, 0.04, 0.08, plain(0x0a0a0a)); // pointed toe tip
        cyl(leg, 0, -0.72, -0.06, 0.015, 0.08, plain(0x1a1a1a), 6); // thin stiletto heel
        limbs.push({ arm: null, leg, s });
      }

      // Left Arm: sassy fashion model pose with hand on hip
      const armL = new THREE.Group(); armL.position.set(-0.24, 1.34, 0); g.add(armL);
      box(armL, -0.06, -0.18, 0.02, 0.10, 0.38, 0.11, opSkin).rotation.z = -0.32;
      const forearmL = new THREE.Group(); forearmL.position.set(-0.12, -0.34, 0.03); armL.add(forearmL);
      forearmL.rotation.z = 0.85; forearmL.rotation.y = -0.3;
      box(forearmL, 0.04, -0.12, 0.01, 0.09, 0.26, 0.10, opSkin);
      box(forearmL, 0.06, -0.24, 0.01, 0.08, 0.07, 0.08, opSkin); // hand on hip
      limbs[0].arm = armL;

      // Right Arm: raised holding thin white cigarette with curling animated smoke
      const armR = new THREE.Group(); armR.position.set(0.24, 1.34, 0); g.add(armR);
      box(armR, 0.04, -0.16, 0.02, 0.10, 0.35, 0.11, opSkin).rotation.z = 0.15;
      const forearmR = new THREE.Group(); forearmR.position.set(0.06, -0.32, 0); armR.add(forearmR);
      forearmR.rotation.x = -0.85; forearmR.rotation.z = -0.35;
      box(forearmR, 0, -0.14, 0, 0.09, 0.28, 0.10, opSkin);
      box(forearmR, 0, -0.29, 0.02, 0.075, 0.09, 0.08, opSkin); // hand

      // Long slim white cigarette with filter and glowing ember
      const cig = box(forearmR, 0.01, -0.33, 0.12, 0.016, 0.016, 0.20, plain(0xf8f8f8));
      cig.rotation.x = 0.2;
      box(forearmR, 0.01, -0.35, 0.05, 0.018, 0.018, 0.04, plain(0xd28d54)); // cork filter
      box(forearmR, 0.01, -0.30, 0.225, 0.02, 0.02, 0.016, glow(0xff3b0a, 3.2)); // glowing red cherry

      // Dynamic curling smoke wisps
      const smokeParticles = [];
      const smokeMat = plain(0xd8e4dc, 0.85);
      const smokeRoot = new THREE.Group(); smokeRoot.position.set(0.01, -0.29, 0.24); forearmR.add(smokeRoot);
      for (let s = 0; s < 4; s++) {
        const sm = new THREE.Mesh(new THREE.SphereGeometry(0.018 + s * 0.008, 8, 8), smokeMat);
        sm.position.set((s % 2 ? 0.015 : -0.015), s * 0.07, s * 0.02);
        smokeRoot.add(sm);
        smokeParticles.push(sm);
      }
      limbs[1].arm = armR;

      g.userData.animate = (time, moving) => {
        for (const { leg, s } of limbs) {
          if (leg) leg.rotation.x = moving ? Math.sin(time * 7) * s * 0.52 : 0;
        }
        armL.rotation.x = moving ? Math.sin(time * 7) * 0.25 : 0;
        armR.rotation.x = moving ? -Math.sin(time * 7) * 0.12 : Math.sin(time * 1.5) * 0.025;
        // Animated smoke curls
        smokeRoot.rotation.y = time * 1.8;
        for (let i = 0; i < smokeParticles.length; i++) {
          const sm = smokeParticles[i];
          sm.position.y = (i * 0.07) + (Math.sin(time * 2.5 + i) * 0.015);
          sm.position.x = Math.sin(time * 3 + i * 1.5) * 0.02;
          sm.scale.setScalar(1 + Math.sin(time * 2 + i) * 0.2);
        }
        g.userData.bob = moving ? Math.abs(Math.sin(time * 7)) * 0.03 : 0;
      };

    } else if (cls === 'dispatcher') {
      // 2. МАРІЯ (Диспетчерка): Psychonauts Milla Vodello style: Hexagonal head, wide smiling face,
      // long dark hair with iconic winged bangs, long swan neck, retro teardrop earrings, vibrant 70s mod dress!
      const dispSkin = plain(0xdca672);
      const darkHair = plain(0x1c130d);

      // Torso in 1970s psychedelic mod wavy dress
      box(g, 0, 1.15, 0, 0.40, 0.56, 0.25, psychDressMat);
      box(g, 0, 0.84, 0, 0.44, 0.20, 0.28, psychDressMat);

      // Elongated slender swan neck (Psychonauts signature!)
      cyl(g, 0, 1.48, 0, 0.06, 0.26, dispSkin, 10);

      // Head with painted Psychonauts face
      const head = new THREE.Group(); head.position.set(0, 1.66, 0); g.add(head);
      box(head, 0, 0, 0, 0.26, 0.31, 0.25, dispSkin);
      const faceMesh = new THREE.Mesh(new THREE.PlaneGeometry(0.25, 0.30), mariaFaceMat);
      faceMesh.position.set(0, 0, 0.127);
      head.add(faceMesh);

      // Dangling retro earrings: curved white horn + pink drop bead
      for (const s of [-1, 1]) {
        const eGroup = new THREE.Group(); eGroup.position.set(s * 0.15, -0.06, 0.01); head.add(eGroup);
        cyl(eGroup, 0, -0.04, 0, 0.018, 0.10, plain(0xf8f8fa), 8);
        const bead = new THREE.Mesh(new THREE.SphereGeometry(0.026, 8, 8), plain(0xe8558a));
        bead.position.set(0, -0.11, 0);
        eGroup.add(bead);
      }

      // Very long hair cascading down back to thighs
      box(head, 0, 0.18, -0.02, 0.28, 0.08, 0.27, darkHair); // crown
      box(head, 0, -0.22, -0.145, 0.32, 0.68, 0.08, darkHair); // long flowing back hair!
      for (const s of [-1, 1]) box(head, s * 0.15, -0.18, 0, 0.04, 0.60, 0.25, darkHair); // flowing side locks

      // Iconic Winged Bangs: flaring wide out to sides and curving upward!
      for (const s of [-1, 1]) {
        const wingRoot = new THREE.Group(); wingRoot.position.set(s * 0.10, 0.13, 0.14); head.add(wingRoot);
        const wing = box(wingRoot, s * 0.08, 0.02, 0, 0.16, 0.06, 0.04, darkHair);
        wing.rotation.z = s * -0.42;
        const wingTip = box(wingRoot, s * 0.18, 0.06, 0, 0.08, 0.04, 0.035, darkHair);
        wingTip.rotation.z = s * -0.68;
      }

      // Arms in psychedelic bell sleeves
      for (const s of [-1, 1]) {
        const arm = new THREE.Group(); arm.position.set(s * 0.26, 1.35, 0); g.add(arm);
        box(arm, 0, -0.22, 0, 0.12, 0.44, 0.13, psychDressMat);
        box(arm, 0, -0.47, 0, 0.09, 0.13, 0.10, dispSkin);

        const leg = new THREE.Group(); leg.position.set(s * 0.13, 0.78, 0); g.add(leg);
        box(leg, 0, -0.32, 0, 0.14, 0.64, 0.17, plain(0x321e16)); // dark tights
        // Retro coral-pink platform shoes
        box(leg, 0, -0.68, 0.04, 0.14, 0.13, 0.26, plain(0xd93852));
        box(leg, 0, -0.73, 0.04, 0.15, 0.04, 0.28, plain(0xfa8898)); // platform sole
        limbs.push({ arm, leg, s });
      }

      g.userData.animate = (time, moving) => {
        for (const { arm, leg, s } of limbs) {
          leg.rotation.x = moving ? Math.sin(time * 7) * s * 0.55 : 0;
          arm.rotation.x = moving ? -Math.sin(time * 7) * s * 0.32 : Math.sin(time * 1.4) * 0.025;
        }
        head.rotation.y = moving ? 0 : Math.sin(time * 1.2) * 0.05;
        g.userData.bob = moving ? Math.abs(Math.sin(time * 7)) * 0.035 : 0;
      };

    } else if (cls === 'engineer') {
      // 3. ТАРАС (Інженер / Охоронець станції): Boyd Cooper style: Peaked octagonal security guard cap
      // with gold star badge, yellow face with paranoid wide eyes, stubble with spiral chin swirl,
      // oversized double-breasted teal coat with big brass buttons, pinstriped shirt & tie, paranoid claw hands!
      const guardSkin = plain(0xe8c467);
      const coatTeal = mat('fabric', 0x133431);
      const capMat = mat('fabric', 0x112d2b);
      const goldStar = glow(0xebb828, 1.4);
      const brassBtn = glow(0xebb828, 1.3);

      // Sturdy Torso in dark teal double-breasted security coat
      box(g, 0, 1.14, 0, 0.54, 0.65, 0.35, coatTeal);
      box(g, 0, 0.86, 0, 0.56, 0.16, 0.37, coatTeal);

      // Large polished embossed brass buttons down front
      for (let b = 0; b < 2; b++) {
        for (const side of [-0.08, 0.08]) {
          cyl(g, side, 1.25 - b * 0.20, 0.185, 0.04, 0.02, brassBtn, 10, Math.PI / 2);
        }
      }
      // Dark chest pocket flaps with security badge & pen
      box(g, -0.16, 1.28, 0.182, 0.14, 0.035, 0.015, plain(0x0e2422));
      box(g, 0.16, 1.28, 0.182, 0.14, 0.035, 0.015, plain(0x0e2422));
      box(g, -0.16, 1.32, 0.188, 0.035, 0.04, 0.01, goldStar); // pocket star badge
      box(g, 0.13, 1.33, 0.188, 0.012, 0.06, 0.01, plain(0xd8d8d8)); // pen clip

      // Open collar with red/blue striped shirt & black necktie
      box(g, 0, 1.38, 0.168, 0.20, 0.14, 0.02, stripedMat);
      box(g, 0, 1.26, 0.182, 0.07, 0.24, 0.018, plain(0x121413)); // black tie

      // Neck & Head with painted Boyd Cooper face
      cyl(g, 0, 1.48, 0, 0.09, 0.15, guardSkin, 8);
      const head = new THREE.Group(); head.position.set(0, 1.64, 0); g.add(head);
      box(head, 0, 0, 0, 0.29, 0.34, 0.28, guardSkin);
      const faceMesh = new THREE.Mesh(new THREE.PlaneGeometry(0.28, 0.33), tarasFaceMat);
      faceMesh.position.set(0, -0.005, 0.142);
      head.add(faceMesh);

      // Prominent cartoon ears sticking out horizontally
      for (const s of [-1, 1]) {
        const ear = box(head, s * 0.17, 0.02, 0, 0.05, 0.11, 0.07, guardSkin);
        ear.rotation.y = s * 0.2;
        box(ear, s * 0.01, 0, 0, 0.02, 0.07, 0.04, plain(0xc79e46)); // inner ear
      }

      // Peaked Octagonal Guard Cap with Gold Star & Visor
      box(head, 0, 0.22, -0.02, 0.36, 0.16, 0.38, capMat);
      box(head, 0, 0.28, 0.04, 0.32, 0.08, 0.32, capMat);
      // Glossy black visor
      const visor = box(head, 0, 0.14, 0.20, 0.36, 0.025, 0.14, plain(0x0a0c0b));
      visor.rotation.x = 0.22;
      // 3D Polished Gold Star Badge
      const star = box(head, 0, 0.24, 0.19, 0.09, 0.09, 0.025, goldStar);
      star.rotation.z = Math.PI / 4;

      // Paranoid Claw Hands & Arms
      for (const s of [-1, 1]) {
        const arm = new THREE.Group(); arm.position.set(s * 0.34, 1.38, 0); g.add(arm);
        box(arm, 0, -0.24, 0, 0.16, 0.48, 0.17, coatTeal);

        // Forearm angled inward with claw hands
        const forearm = new THREE.Group(); forearm.position.set(0, -0.48, 0); arm.add(forearm);
        forearm.rotation.x = -0.35;
        forearm.rotation.y = s * 0.25;
        box(forearm, 0, -0.08, 0, 0.14, 0.16, 0.14, guardSkin); // hand base
        // 4 chunky curled fingers
        for (let f = 0; f < 4; f++) {
          const finger = box(forearm, (f - 1.5) * 0.03, -0.16, 0.05, 0.024, 0.07, 0.03, guardSkin);
          finger.rotation.x = 0.6;
        }

        const leg = new THREE.Group(); leg.position.set(s * 0.16, 0.82, 0); g.add(leg);
        box(leg, 0, -0.33, 0, 0.18, 0.64, 0.20, coatTeal);
        box(leg, 0, -0.72, 0.05, 0.20, 0.14, 0.32, plain(0x0e100f)); // heavy duty boots
        limbs.push({ arm, leg, s });
      }

      g.userData.animate = (time, moving) => {
        for (const { arm, leg, s } of limbs) {
          leg.rotation.x = moving ? Math.sin(time * 7) * s * 0.55 : 0;
          arm.rotation.x = moving ? -Math.sin(time * 7) * s * 0.32 : Math.sin(time * 1.5) * 0.02;
        }
        // Paranoid glance around
        head.rotation.y = moving ? 0 : Math.sin(time * 2.2) * 0.15;
        g.userData.bob = moving ? Math.abs(Math.sin(time * 7)) * 0.035 : 0;
      };

    } else {
      // 4. БОГДАН (Журналіст): Hunched sculpture style: Heavily stooped posture, massive rounded
      // tweed blazer shoulders enveloping back, small weathered sunken head peering up, open cream shirt,
      // and the iconic ULTRA-LONG tapestry floral tie extending down past knees with secondary loose tail!
      const hunchedSkin = plain(0xc7a68a);
      const darkHair = plain(0x1f1915);

      // Huge oversized rounded tweed blazer with massive dropped shoulders
      box(g, 0, 1.08, 0, 0.62, 0.60, 0.40, tweedMat);
      box(g, 0, 1.42, -0.16, 0.66, 0.44, 0.38, tweedMat); // huge hunched upper back
      for (const s of [-1, 1]) {
        box(g, s * 0.34, 1.48, -0.06, 0.30, 0.34, 0.40, tweedMat); // dropped rounded shoulder mass
      }

      // Small sunken head nestled deep down, tilted backward/upward
      const head = new THREE.Group(); head.position.set(0, 1.50, 0.06); g.add(head);
      head.rotation.x = -0.22; // tilted up to peer forward from hunch
      box(head, 0, 0, 0, 0.22, 0.24, 0.22, hunchedSkin);
      const faceMesh = new THREE.Mesh(new THREE.PlaneGeometry(0.21, 0.23), bogdanFaceMat);
      faceMesh.position.set(0, 0, 0.112);
      head.add(faceMesh);
      box(head, 0, 0.12, -0.04, 0.23, 0.08, 0.22, darkHair); // disheveled balding hair

      // Rumpled open cream collared dress shirt
      box(g, 0, 1.26, 0.17, 0.26, 0.45, 0.04, plain(0xeae3d0));
      for (const s of [-1, 1]) {
        const col = box(g, s * 0.09, 1.42, 0.19, 0.08, 0.08, 0.02, plain(0xeae3d0));
        col.rotation.z = s * -0.4;
      }

      // The Iconic Extra-Long Floral Tapestry Tie: drapes all the way down past knees to shins!
      const tieRoot = new THREE.Group(); tieRoot.position.set(0.02, 1.25, 0.19); g.add(tieRoot);
      const mainTie = box(tieRoot, 0, -0.48, 0.01, 0.12, 0.98, 0.02, floralTieMat);
      // Pointed triangular tip
      const tieTip = box(tieRoot, 0, -0.99, 0.01, 0.08, 0.06, 0.02, floralTieMat);
      tieTip.rotation.z = Math.PI / 4;

      // Secondary loose tail of tie hanging free from collar
      const tail = box(tieRoot, -0.08, -0.32, 0.015, 0.045, 0.65, 0.015, floralTieMat);
      tail.rotation.z = 0.08;

      // Heavy baggy trousers & black dress shoes
      for (const s of [-1, 1]) {
        const arm = new THREE.Group(); arm.position.set(s * 0.42, 1.34, -0.06); g.add(arm);
        box(arm, 0, -0.28, 0, 0.18, 0.55, 0.19, tweedMat);
        box(arm, 0, -0.56, 0.01, 0.15, 0.06, 0.16, plain(0xeae3d0)); // shirt cuff
        box(arm, 0, -0.62, 0.02, 0.13, 0.12, 0.13, hunchedSkin); // weathered hand

        const leg = new THREE.Group(); leg.position.set(s * 0.16, 0.78, 0); g.add(leg);
        box(leg, 0, -0.32, 0, 0.21, 0.64, 0.23, tweedMat);
        box(leg, 0, -0.68, 0.06, 0.22, 0.14, 0.33, plain(0x111111)); // rounded leather shoes
        limbs.push({ arm, leg, s });
      }

      g.userData.animate = (time, moving) => {
        // Lumbering stooped gait
        for (const { arm, leg, s } of limbs) {
          leg.rotation.x = moving ? Math.sin(time * 6.5) * s * 0.58 : 0;
          arm.rotation.x = moving ? -Math.sin(time * 6.5) * s * 0.36 : Math.sin(time * 1.1) * 0.02;
        }
        // Breathing tie flutter
        tail.rotation.z = 0.08 + Math.sin(time * 6) * 0.06;
        mainTie.rotation.x = moving ? Math.sin(time * 6.5) * 0.08 : Math.sin(time * 1.5) * 0.015;
        g.userData.bob = moving ? Math.abs(Math.sin(time * 6.5)) * 0.04 : 0;
      };
    }
  }
  g.traverse(o=>{if(o.isMesh)o.castShadow=true;});
  return g;
}

// --- Courtyard Props ---

function guardBooth(parent, x, z, rotation = 0) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = rotation; parent.add(g);
  const metalMat = mat('metal', 0x4d5b4a);
  const concreteMat = mat('metal', 0x363e35);
  const darkWall = mat('rust', 0x5a483a);
  // Base slab
  box(g, 0, 0.1, 0, 2.4, 0.2, 2.2, concreteMat);
  // 4 corner columns
  for (const dx of [-1.08, 1.08]) {
    for (const dz of [-0.98, 0.98]) {
      box(g, dx, 1.25, dz, 0.16, 2.2, 0.16, metalMat);
    }
  }
  // Solid lower wall panels
  box(g, 0, 0.55, -0.98, 2.0, 0.8, 0.08, darkWall);
  box(g, -1.08, 0.55, 0, 0.08, 0.8, 1.8, darkWall);
  box(g, 1.08, 0.55, 0, 0.08, 0.8, 1.8, darkWall);
  box(g, -0.55, 0.55, 0.98, 0.9, 0.8, 0.08, darkWall);
  // Window frames (glazed upper openings)
  box(g, 0, 1.35, -0.98, 1.9, 0.72, 0.03, plain(0x283b33, 0.3));
  box(g, -1.08, 1.35, 0, 0.03, 0.72, 1.7, plain(0x283b33, 0.3));
  box(g, 1.08, 1.35, 0, 0.03, 0.72, 1.7, plain(0x283b33, 0.3));
  box(g, -0.55, 1.35, 0.98, 0.85, 0.72, 0.03, plain(0x283b33, 0.3));
  // Slanted corrugated roof with overhang
  box(g, 0, 2.42, 0, 2.65, 0.14, 2.45, mat('rust', 0x6e4a38));
  // Security desk inside
  box(g, 0, 0.8, -0.4, 1.3, 0.07, 0.65, mat('wood'));
  box(g, -0.5, 0.4, -0.4, 0.08, 0.75, 0.55, mat('metal'));
  box(g, 0.5, 0.4, -0.4, 0.08, 0.75, 0.55, mat('metal'));
  // Stool
  cyl(g, 0, 0.45, 0.15, 0.22, 0.08, mat('fabric', 0x5a6352));
  cyl(g, 0, 0.22, 0.15, 0.03, 0.42, mat('metal'));
  // Desk equipment: logbook and radio transceiver
  box(g, 0.25, 0.86, -0.35, 0.35, 0.04, 0.28, plain(0xd4cfbc));
  box(g, -0.3, 0.92, -0.45, 0.28, 0.18, 0.18, mat('metal', 0x222622));
  cyl(g, -0.4, 1.15, -0.45, 0.012, 0.35, plain(0x181818));
  // Signs and exterior lamp
  label(g, ['КПП-01', 'СЛУЖБА БЕЗПЕКИ'], 0, 2.18, 1.12, 1.4, 0.32, 0, { bg: '#2b3628', fg: '#cdd3b6', font: 22 });
  box(g, 0.85, 1.95, 1.12, 0.12, 0.2, 0.14, glow(0xffd488, 1.6));
  return g;
}

function barrierGate(parent, x, z, rotation = 0) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = rotation; parent.add(g);
  // Red/white barrier post
  box(g, -1.35, 0.55, 0, 0.42, 1.1, 0.42, plain(0xa83224));
  box(g, -1.35, 0.06, 0, 0.55, 0.12, 0.55, mat('metal', 0x242822));
  // Pivot mechanism & counterweight
  cyl(g, -1.35, 0.88, 0, 0.12, 0.48, mat('metal', 0x333333), 8, Math.PI / 2);
  box(g, -1.72, 0.88, 0, 0.38, 0.28, 0.28, mat('metal', 0x222622));
  // Striped barrier arm
  box(g, 0.15, 0.88, 0, 2.7, 0.09, 0.07, plain(0xdedede));
  for (let i = 0; i < 5; i++) {
    box(g, -0.8 + i * 0.52, 0.88, 0, 0.18, 0.096, 0.076, plain(0xa83224));
  }
  // Rest fork support post
  box(g, 1.45, 0.48, 0, 0.1, 0.96, 0.1, plain(0xa83224));
  box(g, 1.45, 0.98, 0, 0.18, 0.1, 0.18, mat('metal'));
  return g;
}

function floodlightTower(parent, x, z, rotation = 0) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = rotation; parent.add(g);
  const steel = mat('metal', 0x485246);
  // 4 steel legs rising 7.2m
  for (const [dx, dz] of [[-0.55, -0.55], [0.55, -0.55], [-0.55, 0.55], [0.55, 0.55]]) {
    cyl(g, dx, 3.6, dz, 0.06, 7.2, steel, 6);
  }
  // Cross bracing struts
  for (let y = 1.5; y <= 6.5; y += 1.8) {
    box(g, 0, y, -0.55, 1.1, 0.05, 0.05, steel);
    box(g, 0, y, 0.55, 1.1, 0.05, 0.05, steel);
    box(g, -0.55, y, 0, 0.05, 0.05, 1.1, steel);
    box(g, 0.55, y, 0, 0.05, 0.05, 1.1, steel);
  }
  // Top platform
  box(g, 0, 7.25, 0, 1.6, 0.08, 1.6, mat('metal', 0x323a31));
  for (const [px, pz] of [[0, -0.75], [0, 0.75], [-0.75, 0], [0.75, 0]]) {
    box(g, px, 7.7, pz, px ? 0.04 : 1.5, 0.8, pz ? 0.04 : 1.5, steel);
  }
  // Floodlight head
  const head = new THREE.Group(); head.position.set(0, 7.8, 0); head.rotation.x = 0.35; g.add(head);
  box(head, 0, 0, 0, 0.7, 0.55, 0.45, mat('metal', 0x222622));
  cyl(head, 0, 0, 0.24, 0.28, 0.08, glow(0xfff0bb, 3.2), 10, Math.PI / 2);
  return g;
}

function cableSpool(parent, x, z, rotation = 0) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = rotation; parent.add(g);
  const timber = mat('wood', 0x6e583d);
  cyl(g, 0, 0.58, -0.38, 0.58, 0.08, timber, 12, Math.PI / 2);
  cyl(g, 0, 0.58, 0.38, 0.58, 0.08, timber, 12, Math.PI / 2);
  cyl(g, 0, 0.58, 0, 0.44, 0.68, plain(0x1a211c), 12, Math.PI / 2);
  for (let i = 0; i < 4; i++) {
    const a = i * Math.PI / 4;
    box(g, 0, 0.58, 0.42, 1.05, 0.09, 0.02, timber, a);
    box(g, 0, 0.58, -0.42, 1.05, 0.09, 0.02, timber, a);
  }
  return g;
}

function abandonedCar(parent, x, z, rotation = 0) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = rotation; parent.add(g);
  const rustCab = mat('rust', 0x5a3d2c);
  const darkFrame = plain(0x181e18);
  const dirtyGlass = plain(0x283b34, 0.4);
  box(g, 0, 0.32, 0, 1.9, 0.22, 4.2, darkFrame);
  for (const side of [-0.98, 0.98]) {
    for (const zOff of [-1.3, 1.3]) {
      const tire = cyl(g, side, 0.26, zOff, 0.36, 0.24, plain(0x121612), 10);
      tire.rotation.z = Math.PI / 2;
    }
  }
  box(g, 0, 1.05, -0.4, 1.98, 1.05, 1.8, rustCab);
  box(g, 0, 0.78, 1.05, 1.85, 0.6, 1.3, rustCab);
  box(g, 0, 1.25, 0.49, 1.7, 0.55, 0.04, dirtyGlass, 0.15);
  box(g, 0, 0.76, 1.71, 1.6, 0.4, 0.06, plain(0x1e241e));
  cyl(g, -0.65, 0.78, 1.74, 0.14, 0.03, plain(0x333333), 8, Math.PI / 2);
  cyl(g, 0.65, 0.78, 1.74, 0.14, 0.03, glow(0xddcc99, 0.3), 8, Math.PI / 2);
  box(g, 0, 0.9, -1.35, 1.98, 0.7, 1.7, mat('metal', 0x485244));
  box(g, 0, 0.62, -1.35, 1.85, 0.14, 1.6, darkFrame);
  return g;
}

// --- Lobby & Corridor Props ---

function turnstile(parent, x, z, rotation = 0) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = rotation; parent.add(g);
  const steel = mat('metal', 0x949c90);
  const darkBase = plain(0x222822);
  for (const [side, dx] of [[-1, -0.98], [1, 0.98]]) {
    box(g, dx, 0.5, 0, 0.44, 1.0, 0.76, steel);
    box(g, dx, 0.04, 0, 0.48, 0.08, 0.8, darkBase);
    cyl(g, dx - side * 0.18, 0.72, 0, 0.07, 0.12, darkBase, 8, Math.PI / 2);
    for (let i = 0; i < 3; i++) {
      const a = i * Math.PI * 2 / 3;
      cyl(g, dx - side * 0.18 + Math.cos(a) * 0.22, 0.72 + Math.sin(a) * 0.22, 0, 0.016, 0.48, steel, 6, a);
    }
    box(g, dx, 0.98, -0.22, 0.08, 0.08, 0.14, plain(0x181818));
    box(g, dx, 1.01, 0.15, 0.06, 0.02, 0.08, glow(side === -1 ? 0x22ee44 : 0xee2211, 2.0));
  }
  return g;
}

function honorBoard(parent, x, z, rotation = 0) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = rotation; parent.add(g);
  box(g, 0, 1.8, 0, 2.2, 1.3, 0.06, mat('wood', 0x423120));
  box(g, 0, 2.32, 0.035, 1.9, 0.18, 0.02, mat('metal', 0xc4a444));
  label(g, ['ДОШКА ПОШАНИ', 'ТЕЛЕЦЕНТР «BLACKPINE»'], 0, 2.32, 0.05, 1.8, 0.16, 0, { bg: '#8a702b', fg: '#1a1808', font: 20 });
  for (let r = 0; r < 2; r++) {
    for (let c = 0; c < 4; c++) {
      const px = -0.75 + c * 0.5;
      const py = 1.95 - r * 0.48;
      box(g, px, py, 0.035, 0.32, 0.38, 0.015, plain(0x283028));
      box(g, px, py, 0.045, 0.28, 0.32, 0.01, plain(0xd2cbb8));
      cyl(g, px, py + 0.04, 0.052, 0.06, 0.12, plain(0x484238), 8);
    }
  }
  return g;
}

function scheduleBoard(parent, x, z, rotation = 0) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = rotation; parent.add(g);
  box(g, 0, 1.8, 0, 2.4, 1.35, 0.05, mat('wood', 0x3d3224));
  label(g, [
    'СІТКА МОВЛЕННЯ · ЖОВТЕНЬ 1996',
    '06:00 РАНКОВІ НОВИНИ  18:00 ТЕЛЕГАЗЕТА',
    '12:00 ДОКУМЕНТАЛІСТИКА 21:00 ПІДСУМКИ ТИЖНЯ',
    '02:00 ТЕХНІЧНА ПЕРЕРВА 02:17 [СИГНАЛ ПЕРЕРВАНО]'
  ], 0, 1.8, 0.03, 2.3, 1.25, 0, { bg: '#1c2820', fg: '#b2c8af', font: 15, align: 'center' });
  return g;
}

function fireBoard(parent, x, z, rotation = 0) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = rotation; parent.add(g);
  box(g, 0, 1.5, 0, 1.3, 1.1, 0.22, plain(0x9a2218));
  box(g, 0, 1.5, 0.115, 1.15, 0.95, 0.015, plain(0x284038, 0.3));
  cyl(g, -0.28, 1.5, 0.02, 0.25, 0.14, plain(0xd4cebc), 10, Math.PI / 2);
  cyl(g, -0.28, 1.22, 0.02, 0.035, 0.22, mat('metal', 0xc49a45), 8);
  box(g, 0.32, 1.5, 0.02, 0.04, 0.72, 0.04, mat('wood'));
  box(g, 0.32, 1.8, 0.02, 0.16, 0.12, 0.03, mat('metal', 0x888888));
  label(g, ['ПОЖЕЖНИЙ КРАН', 'ПК-04 · 380V'], 0, 1.96, 0.12, 0.85, 0.18, 0, { bg: '#801810', fg: '#ffffff', font: 20 });
  return g;
}

function cableTray(parent, x, z, rotation = 0) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = rotation; parent.add(g);
  const steel = mat('metal', 0x5a6356);
  box(g, 0, 3.12, 0, 0.52, 0.04, 3.2, steel);
  box(g, -0.25, 3.18, 0, 0.03, 0.12, 3.2, steel);
  box(g, 0.25, 3.18, 0, 0.03, 0.12, 3.2, steel);
  for (const zOff of [-1.2, 1.2]) {
    cyl(g, -0.24, 3.35, zOff, 0.012, 0.44, steel);
    cyl(g, 0.24, 3.35, zOff, 0.012, 0.44, steel);
  }
  for (const [xOff, col] of [[-0.12, 0x181e18], [0, 0x3d4852], [0.12, 0x6e2820]]) {
    cyl(g, xOff, 3.16, 0, 0.025, 3.2, plain(col), 6, Math.PI / 2);
  }
  return g;
}

function trashBin(parent, x, z) {
  const g = new THREE.Group(); g.position.set(x, 0, z); parent.add(g);
  cyl(g, 0, 0.42, 0, 0.24, 0.84, mat('metal', 0x485246), 10);
  cyl(g, 0, 0.83, 0, 0.22, 0.04, plain(0x181818), 10);
  box(g, 0.05, 0.86, -0.05, 0.12, 0.08, 0.12, plain(0xd2cbb8));
  return g;
}

function debris(parent, x, z, rotation = 0) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = rotation; parent.add(g);
  box(g, 0.1, 0.01, 0.1, 0.25, 0.01, 0.32, plain(0xdedede), 0.3);
  box(g, -0.15, 0.012, 0.05, 0.22, 0.01, 0.28, plain(0xc4baa2), -0.4);
  box(g, 0.05, 0.025, -0.18, 0.14, 0.04, 0.1, plain(0x8a927a), 0.8);
  box(g, -0.08, 0.02, -0.08, 0.09, 0.035, 0.07, plain(0xb03322), 0.2);
  return g;
}

// --- Archive & Studio Props ---

function cardCatalog(parent, x, z, rotation = 0) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = rotation; parent.add(g);
  const wood = mat('wood', 0x4c3826);
  const brass = mat('metal', 0xc49a45);
  box(g, 0, 0.9, 0, 1.6, 1.8, 0.68, wood);
  for (let r = 0; r < 4; r++) {
    for (let c = 0; c < 4; c++) {
      const dx = -0.57 + c * 0.38;
      const dy = 0.28 + r * 0.42;
      box(g, dx, dy, 0.345, 0.33, 0.36, 0.02, mat('wood', 0x5a442e));
      box(g, dx, dy + 0.06, 0.36, 0.14, 0.08, 0.015, brass);
      box(g, dx, dy + 0.06, 0.365, 0.11, 0.05, 0.01, plain(0xded8c0));
      cyl(g, dx, dy - 0.06, 0.36, 0.022, 0.04, brass, 6, Math.PI / 2);
    }
  }
  return g;
}

function archivistDesk(parent, x, z, rotation = 0) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = rotation; parent.add(g);
  const wood = mat('wood');
  box(g, 0, 0.78, 0, 2.3, 0.08, 1.25, wood);
  for (const dx of [-0.85, 0.85]) {
    box(g, dx, 0.38, 0, 0.48, 0.72, 1.1, wood);
    for (let i = 0; i < 3; i++) {
      box(g, dx, 0.18 + i * 0.22, 0.56, 0.42, 0.18, 0.02, mat('wood', 0x5a4632));
      cyl(g, dx, 0.18 + i * 0.22, 0.58, 0.018, 0.03, mat('metal', 0xc49a45), 6, Math.PI / 2);
    }
  }
  // Green Banker's Lamp
  cyl(g, -0.65, 0.84, -0.32, 0.09, 0.03, mat('metal', 0xc49a45));
  cyl(g, -0.65, 0.98, -0.32, 0.018, 0.26, mat('metal', 0xc49a45));
  cyl(g, -0.65, 1.1, -0.32, 0.1, 0.24, glow(0x228833, 1.2), 8, Math.PI / 2);
  cyl(g, -0.65, 1.07, -0.32, 0.035, 0.14, glow(0xffe299, 2.2), 8, Math.PI / 2);
  // Typewriter
  box(g, 0, 0.88, 0, 0.44, 0.12, 0.42, mat('metal', 0x3d4840));
  box(g, 0, 0.92, 0.08, 0.36, 0.06, 0.18, plain(0x181e18), -0.2);
  box(g, 0, 0.98, -0.12, 0.38, 0.14, 0.06, plain(0xdedede), 0.2);
  // Ledger book
  box(g, 0.6, 0.84, 0.1, 0.38, 0.05, 0.48, mat('fabric', 0x6e2820));
  return g;
}

function vhsStack(parent, x, z, rotation = 0) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = rotation; parent.add(g);
  const card = mat('wood', 0x8a7752);
  box(g, 0, 0.22, 0, 0.75, 0.44, 0.55, card);
  box(g, 0.03, 0.62, 0.02, 0.72, 0.4, 0.52, mat('wood', 0x7a6946), 0.04);
  box(g, -0.02, 0.98, -0.02, 0.7, 0.36, 0.5, mat('wood', 0x82704b), -0.05);
  for (let i = 0; i < 4; i++) {
    box(g, -0.22 + i * 0.15, 1.18, 0, 0.12, 0.22, 0.42, plain(i % 2 ? 0x222622 : 0x485244));
  }
  return g;
}

function anchorDesk(parent, x, z, rotation = 0) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = rotation; parent.add(g);
  const wood = mat('wood', 0x3d3024);
  box(g, 0, 0.78, 0, 3.4, 0.09, 1.4, wood);
  box(g, 0, 0.4, 0.65, 3.25, 0.74, 0.08, mat('metal', 0x222a22));
  cyl(g, 0, 0.48, 0.7, 0.22, 0.03, mat('metal', 0xc49a45), 12, Math.PI / 2);
  label(g, ['04'], 0, 0.48, 0.72, 0.35, 0.35, 0, { bg: '#8a702b', fg: '#1a1808', font: 36 });
  for (const dx of [-0.65, 0.65]) {
    cyl(g, dx, 0.85, 0.2, 0.045, 0.03, mat('metal', 0x181818));
    cyl(g, dx, 1.05, 0.15, 0.012, 0.38, mat('metal', 0x888888), 6, -0.2);
    cyl(g, dx, 1.22, 0.08, 0.025, 0.08, plain(0x222222), 8);
  }
  box(g, 0, 0.84, 0.05, 0.5, 0.02, 0.38, plain(0xded8c0));
  return g;
}

function chromakeyBackdrop(parent, x, z, rotation = 0) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = rotation; parent.add(g);
  const steel = mat('metal', 0x888888);
  cyl(g, -2.25, 1.75, 0, 0.05, 3.5, steel);
  cyl(g, 2.25, 1.75, 0, 0.05, 3.5, steel);
  cyl(g, 0, 3.45, 0, 0.05, 4.5, steel, 8, Math.PI / 2);
  box(g, 0, 1.7, 0, 4.4, 3.3, 0.04, plain(0x188a24, 0.95));
  box(g, 0, 0.015, 0.45, 4.4, 0.02, 0.9, plain(0x188a24, 0.95));
  return g;
}

function micStand(parent, x, z, rotation = 0) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = rotation; parent.add(g);
  const steel = mat('metal', 0x777777);
  cyl(g, 0, 0.03, 0, 0.2, 0.06, plain(0x181818), 10);
  cyl(g, 0, 0.85, 0, 0.02, 1.6, steel, 8);
  cyl(g, 0.2, 1.68, 0, 0.016, 0.7, steel, 8, Math.PI / 4);
  cyl(g, 0.42, 1.88, 0, 0.035, 0.12, plain(0x222222), 8);
  return g;
}

function ceilingSpotlight(parent, x, z, rotation = 0) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = rotation; parent.add(g);
  cyl(g, 0, 3.38, 0, 0.018, 0.35, mat('metal', 0x1a1a1a));
  cyl(g, 0, 3.18, 0, 0.15, 0.32, mat('metal', 0x222622), 10, Math.PI / 4);
  cyl(g, 0.1, 3.08, 0.1, 0.13, 0.02, glow(0xffe8aa, 2.8), 10);
  return g;
}

// --- Generator & Industrial Props ---

function dieselGenerator(parent, x, z, rotation = 0, fans = null) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = rotation; parent.add(g);
  const metalEngine = mat('metal', 0x485842);
  const darkBase = mat('metal', 0x242822);
  const rust = mat('rust', 0x6e4533);
  // Skid base
  box(g, 0, 0.12, 0, 3.2, 0.24, 1.8, darkBase);
  // Engine Block
  box(g, -0.4, 0.85, 0, 1.8, 1.25, 1.2, metalEngine);
  for (let i = 0; i < 4; i++) {
    box(g, -1.0 + i * 0.42, 1.52, 0, 0.32, 0.12, 0.85, darkBase);
  }
  // Radiator at front (+X) with fan
  box(g, 0.85, 0.85, 0, 0.5, 1.3, 1.4, mat('metal', 0x3d4538));
  const fanGroup = new THREE.Group(); fanGroup.position.set(1.12, 0.85, 0); fanGroup.rotation.y = Math.PI / 2; g.add(fanGroup);
  for (let i = 0; i < 4; i++) {
    const blade = box(fanGroup, 0, 0, 0, 0.08, 0.88, 0.04, mat('metal', 0x222222));
    blade.rotation.z = i * Math.PI / 4;
  }
  if (fans) fans.push(fanGroup);
  // Flywheel on rear (-X)
  cyl(g, -1.35, 0.85, 0, 0.52, 0.16, rust, 12, Math.PI / 2);
  // Vertical exhaust muffler pipe
  cyl(g, -0.7, 1.95, -0.42, 0.11, 1.1, rust, 8);
  box(g, -0.7, 2.52, -0.42, 0.24, 0.03, 0.24, rust, 0.4);
  // Control gauges & warning plate
  for (let i = 0; i < 3; i++) {
    cyl(g, -0.8 + i * 0.4, 1.35, 0.62, 0.08, 0.04, plain(0xdedede), 10, Math.PI / 2);
  }
  cyl(g, 0.45, 1.25, 0.62, 0.06, 0.06, glow(0xdd2211, 1.8), 8, Math.PI / 2);
  label(g, ['ДИЗЕЛЬ-ГЕНЕРАТОР ДГ-200', 'УВАГА! НЕБЕЗПЕЧНА ЗОНА'], 0, 0.55, 0.92, 1.6, 0.38, 0, { bg: '#8a2416', fg: '#ffee77', font: 17 });
  return g;
}

function fuelCanister(parent, x, z, rotation = 0) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = rotation; parent.add(g);
  box(g, 0, 0.26, 0, 0.34, 0.52, 0.46, mat('metal', 0x4a5840));
  box(g, 0, 0.54, 0, 0.04, 0.06, 0.3, mat('metal', 0x364230));
  cyl(g, 0, 0.54, 0.16, 0.04, 0.07, plain(0x222222), 8);
  label(g, ['ДИЗЕЛЬ', '20 Л'], 0, 0.28, 0.235, 0.24, 0.12, 0, { bg: '#3a4632', fg: '#d0c896', font: 20 });
  return g;
}

function fuelValve(parent, x, z, rotation = 0) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = rotation; parent.add(g);
  const pipe = mat('metal', 0x485246);
  cyl(g, 0, 0.8, 0, 0.07, 1.6, pipe, 8);
  box(g, 0, 1.1, 0, 0.26, 0.26, 0.26, mat('metal', 0x242822));
  cyl(g, 0, 1.26, 0, 0.03, 0.2, mat('metal', 0x888888), 8);
  const wheel = new THREE.Mesh(new THREE.TorusGeometry(0.2, 0.024, 4, 12), plain(0xc4281a));
  wheel.position.set(0, 1.36, 0); wheel.rotation.x = Math.PI / 2; g.add(wheel);
  cyl(g, 0, 1.36, 0, 0.05, 0.03, mat('metal', 0x888888));
  for (let i = 0; i < 4; i++) {
    box(g, 0, 1.36, 0, 0.38, 0.015, 0.02, plain(0xc4281a), i * Math.PI / 4);
  }
  cyl(g, 0, 1.62, 0, 0.1, 0.04, plain(0xdedede), 10, Math.PI / 2);
  return g;
}

function fusePanel(parent, x, z, rotation = 0) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = rotation; parent.add(g);
  box(g, 0, 1.5, 0, 1.1, 1.5, 0.3, mat('metal', 0x6e7868));
  box(g, 0, 1.5, 0.155, 1.0, 1.38, 0.02, plain(0x283028));
  for (const dx of [-0.22, 0.22]) {
    box(g, dx, 1.45, 0.18, 0.16, 0.42, 0.06, plain(0x181e18));
    cyl(g, dx, 1.76, 0.18, 0.028, 0.025, glow(0xee3311, 2.2), 8, Math.PI / 2);
  }
  cyl(g, 0, 1.88, 0.18, 0.12, 0.03, plain(0xdedede), 10, Math.PI / 2);
  label(g, ['380 V', 'ВИСОКА НАПРУГА'], 0, 2.12, 0.16, 0.85, 0.22, 0, { bg: '#e5aa22', fg: '#151515', font: 22 });
  return g;
}

function airDuct(parent, x, z, rotation = 0) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = rotation; parent.add(g);
  const zinc = mat('metal', 0x667464);
  box(g, 0, 1.75, 0, 1.05, 3.5, 1.05, zinc);
  for (const y of [0.8, 1.7, 2.6]) {
    box(g, 0, y, 0, 1.12, 0.06, 1.12, mat('rust', 0x5a483a));
  }
  box(g, 0, 1.25, 0.54, 0.44, 0.52, 0.03, mat('metal', 0x3d483b));
  return g;
}

function ventGrate(parent, x, z, rotation = 0) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = rotation; parent.add(g);
  box(g, 0, 1.8, 0, 1.2, 1.2, 0.07, mat('metal', 0x384236));
  box(g, 0, 1.8, 0.02, 1.05, 1.05, 0.02, plain(0x0a0f0c));
  for (let i = 0; i < 7; i++) {
    box(g, 0, 1.35 + i * 0.15, 0.04, 1.04, 0.06, 0.02, mat('metal', 0x556052), 0.5);
  }
  return g;
}

function cableBundle(parent, x, z, rotation = 0) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = rotation; parent.add(g);
  const dark = plain(0x181e18);
  lineTube(g, [[-0.9, 0.03, -0.2], [-0.3, 0.04, 0.15], [0.3, 0.03, -0.1], [0.9, 0.03, 0.2]], 0.032, dark);
  lineTube(g, [[-0.9, 0.03, 0.1], [-0.2, 0.04, -0.1], [0.4, 0.03, 0.2], [0.9, 0.03, -0.15]], 0.026, dark);
  return g;
}

// --- Apparatus & Control Props ---

function mixingConsole(parent, x, z, rotation = 0) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = rotation; parent.add(g);
  box(g, 0, 0.4, 0, 2.6, 0.8, 1.25, mat('wood', 0x241d18));
  const slope = box(g, 0, 0.84, -0.05, 2.5, 0.12, 1.05, mat('metal', 0x3d483e));
  slope.rotation.x = -0.14;
  for (let i = 0; i < 14; i++) {
    const fx = -1.05 + i * 0.16;
    box(g, fx, 0.89, 0.1, 0.015, 0.025, 0.14, plain(0xded8c0));
    cyl(g, fx, 0.94, -0.15, 0.022, 0.03, plain(i % 3 === 0 ? 0xcc3322 : 0x446655), 6);
  }
  box(g, -0.32, 0.98, -0.38, 0.24, 0.14, 0.04, glow(0xffe899, 1.8));
  box(g, 0.32, 0.98, -0.38, 0.24, 0.14, 0.04, glow(0xffe899, 1.8));
  cyl(g, 0.85, 0.96, -0.2, 0.01, 0.32, mat('metal', 0x888888), 6, 0.3);
  return g;
}

function oscilloscope(parent, x, z, rotation = 0) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = rotation; parent.add(g);
  box(g, 0, 0.18, 0, 0.52, 0.36, 0.46, mat('metal', 0x5a6358));
  box(g, -0.1, 0.18, 0.235, 0.24, 0.24, 0.02, plain(0x181f19));
  label(g, ['∿∿∿∿'], -0.1, 0.18, 0.248, 0.22, 0.22, 0, { bg: '#081f0e', fg: '#33ff55', font: 32 });
  for (let r = 0; r < 3; r++) {
    cyl(g, 0.12, 0.1 + r * 0.08, 0.24, 0.025, 0.03, plain(0x222222), 6, Math.PI / 2);
  }
  return g;
}

function monitorWall(parent, x, z, rotation = 0, monitors = null) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = rotation; parent.add(g);
  box(g, 0, 1.1, 0, 2.2, 2.2, 0.65, mat('metal', 0x222a24));
  const channelNames = [
    '01 ДВІР', '02 ВХІД', '03 АРХІВ',
    '04 СТУДІЯ', '05 ГЕНЕРАТОР', '06 ВЕНТВУЗОЛ',
    '07 СЕРВЕРНА', '08 ТЕХСКЛАД', '09 ЕФІР'
  ];
  for (let r = 0; r < 3; r++) {
    for (let c = 0; c < 3; c++) {
      const idx = r * 3 + c;
      const mx = -0.68 + c * 0.68;
      const my = 0.42 + r * 0.68;
      box(g, mx, my, 0.33, 0.62, 0.56, 0.03, plain(0x111612));
      const screen = label(g, [channelNames[idx]], mx, my, 0.35, 0.54, 0.46, 0, { width: 128, height: 96, font: 12, bg: '#1c2820', fg: '#719271' });
      if (monitors) monitors.push({ group: g, screen });
    }
  }
  return g;
}

function acousticPanel(parent, x, z, rotation = 0) {
  const g = new THREE.Group(); g.position.set(x, 0, z); g.rotation.y = rotation; parent.add(g);
  box(g, 0, 1.8, 0, 1.2, 1.2, 0.06, mat('fabric', 0x282c26));
  for (let i = 0; i < 4; i++) {
    box(g, 0, 1.35 + i * 0.3, 0.045, 1.14, 0.14, 0.04, mat('fabric', 0x1e221c), 0.2);
  }
  return g;
}

export function spawnPropMesh(parent, prop, interactables = null, monitors = null, fans = null) {
  let mesh = null;
  const rot = prop.rotation || 0;
  switch (prop.type) {
    case 'table':
      mesh = table(parent, prop.x, prop.z, prop.w || 2.4, prop.d || 1.3, rot);
      break;
    case 'chair':
      mesh = chair(parent, prop.x, prop.z, rot);
      break;
    case 'shelf':
      mesh = shelf(parent, prop.x, prop.z, rot);
      break;
    case 'serverRack':
      mesh = serverRack(parent, prop.x, prop.z, rot);
      break;
    case 'crate':
      mesh = crate(parent, prop.x, prop.y || 0, prop.z, prop.size || 0.9, rot);
      break;
    case 'barrel':
      mesh = barrel(parent, prop.x, prop.z, prop.color ? (typeof prop.color === 'string' ? parseInt(prop.color.replace('#', ''), 16) : prop.color) : 0x3d4e42);
      break;
    case 'camera':
      mesh = recordingCamera(parent, prop.x, prop.z, rot);
      break;
    case 'crt':
      const c = crt(parent, prop.x, prop.y || 1.3, prop.z, prop.scale || 0.9, true);
      mesh = c.group;
      if (monitors) monitors.push(c);
      break;
    case 'locker':
      mesh = locker(parent, prop.x, prop.z, rot, prop.id);
      if (interactables) interactables.set(prop.id, mesh);
      break;
    case 'guardBooth':
      mesh = guardBooth(parent, prop.x, prop.z, rot);
      break;
    case 'barrier':
      mesh = barrierGate(parent, prop.x, prop.z, rot);
      break;
    case 'floodlightTower':
      mesh = floodlightTower(parent, prop.x, prop.z, rot);
      break;
    case 'cableSpool':
      mesh = cableSpool(parent, prop.x, prop.z, rot);
      break;
    case 'abandonedCar':
      mesh = abandonedCar(parent, prop.x, prop.z, rot);
      break;
    case 'turnstile':
      mesh = turnstile(parent, prop.x, prop.z, rot);
      break;
    case 'honorBoard':
      mesh = honorBoard(parent, prop.x, prop.z, rot);
      break;
    case 'scheduleBoard':
      mesh = scheduleBoard(parent, prop.x, prop.z, rot);
      break;
    case 'fireBoard':
      mesh = fireBoard(parent, prop.x, prop.z, rot);
      break;
    case 'cableTray':
      mesh = cableTray(parent, prop.x, prop.z, rot);
      break;
    case 'trashBin':
      mesh = trashBin(parent, prop.x, prop.z);
      break;
    case 'debris':
      mesh = debris(parent, prop.x, prop.z, rot);
      break;
    case 'cardCatalog':
      mesh = cardCatalog(parent, prop.x, prop.z, rot);
      break;
    case 'archivistDesk':
      mesh = archivistDesk(parent, prop.x, prop.z, rot);
      break;
    case 'vhsStack':
      mesh = vhsStack(parent, prop.x, prop.z, rot);
      break;
    case 'anchorDesk':
      mesh = anchorDesk(parent, prop.x, prop.z, rot);
      break;
    case 'chromakeyBackdrop':
      mesh = chromakeyBackdrop(parent, prop.x, prop.z, rot);
      break;
    case 'micStand':
      mesh = micStand(parent, prop.x, prop.z, rot);
      break;
    case 'ceilingSpotlight':
      mesh = ceilingSpotlight(parent, prop.x, prop.z, rot);
      break;
    case 'dieselGenerator':
      mesh = dieselGenerator(parent, prop.x, prop.z, rot, fans);
      break;
    case 'fuelCanister':
      mesh = fuelCanister(parent, prop.x, prop.z, rot);
      break;
    case 'fuelValve':
      mesh = fuelValve(parent, prop.x, prop.z, rot);
      break;
    case 'fusePanel':
      mesh = fusePanel(parent, prop.x, prop.z, rot);
      break;
    case 'airDuct':
      mesh = airDuct(parent, prop.x, prop.z, rot);
      break;
    case 'ventGrate':
      mesh = ventGrate(parent, prop.x, prop.z, rot);
      break;
    case 'cableBundle':
      mesh = cableBundle(parent, prop.x, prop.z, rot);
      break;
    case 'mixingConsole':
      mesh = mixingConsole(parent, prop.x, prop.z, rot);
      break;
    case 'oscilloscope':
      mesh = oscilloscope(parent, prop.x, prop.z, rot);
      break;
    case 'monitorWall':
      mesh = monitorWall(parent, prop.x, prop.z, rot, monitors);
      break;
    case 'acousticPanel':
      mesh = acousticPanel(parent, prop.x, prop.z, rot);
      break;
    default:
      break;
  }
  if (mesh) {
    mesh.userData.propId = prop.id;
    mesh.userData.propType = prop.type;
    mesh.userData.propData = prop;
  }
  return mesh;
}

export function createProp(kind = 'camera') {
  const g = new THREE.Group();
  if (kind === 'tape') cassette(g, 0, 0, 0, 0, true);
  else if (kind === 'fuse') industrialFuse(g, 0, 0, 0, 0, 'preview');
  else if (kind === 'monitor') crt(g, 0, 0.42, 0, 1, true);
  else if (kind === 'archive' || kind === 'shelf') shelf(g, 0, 0);
  else if (kind === 'locker') locker(g, 0, 0, 0, 'preview');
  else if (kind === 'guardBooth') guardBooth(g, 0, 0);
  else if (kind === 'barrier') barrierGate(g, 0, 0);
  else if (kind === 'floodlightTower') floodlightTower(g, 0, 0);
  else if (kind === 'cableSpool') cableSpool(g, 0, 0);
  else if (kind === 'abandonedCar') abandonedCar(g, 0, 0);
  else if (kind === 'turnstile') turnstile(g, 0, 0);
  else if (kind === 'honorBoard') honorBoard(g, 0, 0);
  else if (kind === 'scheduleBoard') scheduleBoard(g, 0, 0);
  else if (kind === 'fireBoard') fireBoard(g, 0, 0);
  else if (kind === 'cableTray') cableTray(g, 0, 0);
  else if (kind === 'trashBin') trashBin(g, 0, 0);
  else if (kind === 'debris') debris(g, 0, 0);
  else if (kind === 'cardCatalog') cardCatalog(g, 0, 0);
  else if (kind === 'archivistDesk') archivistDesk(g, 0, 0);
  else if (kind === 'vhsStack') vhsStack(g, 0, 0);
  else if (kind === 'anchorDesk') anchorDesk(g, 0, 0);
  else if (kind === 'chromakeyBackdrop') chromakeyBackdrop(g, 0, 0);
  else if (kind === 'micStand') micStand(g, 0, 0);
  else if (kind === 'ceilingSpotlight') ceilingSpotlight(g, 0, 0);
  else if (kind === 'dieselGenerator') dieselGenerator(g, 0, 0);
  else if (kind === 'fuelCanister') fuelCanister(g, 0, 0);
  else if (kind === 'fuelValve') fuelValve(g, 0, 0);
  else if (kind === 'fusePanel') fusePanel(g, 0, 0);
  else if (kind === 'airDuct') airDuct(g, 0, 0);
  else if (kind === 'ventGrate') ventGrate(g, 0, 0);
  else if (kind === 'cableBundle') cableBundle(g, 0, 0);
  else if (kind === 'mixingConsole') mixingConsole(g, 0, 0);
  else if (kind === 'oscilloscope') oscilloscope(g, 0, 0);
  else if (kind === 'monitorWall') monitorWall(g, 0, 0);
  else if (kind === 'acousticPanel') acousticPanel(g, 0, 0);
  else if (kind === 'serverRack') serverRack(g, 0, 0);
  else if (kind === 'table') table(g, 0, 0);
  else if (kind === 'chair') chair(g, 0, 0);
  else if (kind === 'crate') crate(g, 0, 0, 0);
  else if (kind === 'barrel') barrel(g, 0, 0);
  else recordingCamera(g, 0, 0);
  return g;
}

function industrialFuse(parent, x, y, z, rotation = 0, id = 'fuse-1') {
  const g = new THREE.Group(); g.position.set(x, y, z); g.rotation.y = rotation; parent.add(g);
  // Ceramic white body
  cyl(g, 0, 0, 0, 0.08, 0.38, plain(0xd4cfbc), 12, Math.PI / 2);
  // Brass end terminals
  for (const s of [-1, 1]) {
    cyl(g, s * 0.17, 0, 0, 0.088, 0.07, mat('metal', 0xc49a45), 12, Math.PI / 2);
    box(g, s * 0.22, 0, 0, 0.04, 0.09, 0.09, mat('metal', 0xc49a45));
  }
  // Central red glowing sight indicator
  cyl(g, 0, 0, 0, 0.085, 0.09, glow(0xee3311, 2.2), 10, Math.PI / 2);
  const light = new THREE.PointLight(0xff5522, 1.2, 2.5, 2);
  light.position.y = 0.25;
  g.add(light);
  g.userData.id = id;
  g.userData.baseY = y;
  g.userData.light = light;
  label(g, ['380V', '50A'], 0, 0.09, 0, 0.18, 0.08, 0, { bg: '#222822', fg: '#ffaa44', font: 24, width: 64, height: 32 });
  return g;
}

function fireExtinguisher(parent, x, y, z, ry = 0) {
  const g = new THREE.Group(); g.position.set(x, y, z); g.rotation.y = ry; parent.add(g);
  cyl(g, 0, 0, 0, 0.1, 0.55, plain(0xba2222), 10);
  box(g, 0, 0.32, 0, 0.08, 0.1, 0.06, mat('metal'));
  box(g, 0, 0.38, 0.05, 0.04, 0.05, 0.14, plain(0x191919));
  cyl(g, 0.05, 0.34, 0.04, 0.03, 0.02, glow(0xddaa44, 0.8), 8, Math.PI / 2);
  label(g, ['ВВ-5', '380В'], 0, 0, 0.105, 0.16, 0.12, 0, { bg: '#901515', fg: '#ffffff', font: 20, width: 64, height: 48 });
  return g;
}

function wallClock(parent, x, y, z, ry = 0) {
  const g = new THREE.Group(); g.position.set(x, y, z); g.rotation.y = ry; parent.add(g);
  cyl(g, 0, 0, 0, 0.32, 0.06, mat('metal', 0x3a3f38), 16, Math.PI / 2);
  cyl(g, 0, 0, 0.025, 0.28, 0.02, plain(0xe8e4d2), 16, Math.PI / 2);
  label(g, ['02:17'], 0, 0, 0.04, 0.34, 0.2, 0, { bg: '#e8e4d2', fg: '#1a1d18', font: 32, width: 128, height: 64 });
  return g;
}

function hazardTape(parent, x, z, w, d, ry = 0) {
  const c = document.createElement('canvas'); c.width = 128; c.height = 32;
  const ctx = c.getContext('2d');
  ctx.fillStyle = '#c5a028'; ctx.fillRect(0, 0, 128, 32);
  ctx.fillStyle = '#181b16';
  for (let i = -32; i < 160; i += 24) {
    ctx.beginPath(); ctx.moveTo(i, 0); ctx.lineTo(i + 14, 0); ctx.lineTo(i - 2, 32); ctx.lineTo(i - 16, 32); ctx.fill();
  }
  const tx = new THREE.CanvasTexture(c); tx.wrapS = tx.wrapT = THREE.RepeatWrapping;
  tx.repeat.set(Math.max(1, Math.round(w / 0.5)), 1);
  const mesh = new THREE.Mesh(new THREE.PlaneGeometry(w, d), new THREE.MeshBasicMaterial({ map: tx, side: THREE.DoubleSide }));
  mesh.rotation.x = -Math.PI / 2; mesh.rotation.z = ry; mesh.position.set(x, 0.015, z); parent.add(mesh);
  return mesh;
}

function hangingCable(parent, p1, p2, sag = 0.4) {
  const mid = [(p1[0] + p2[0]) / 2, Math.min(p1[1], p2[1]) - sag, (p1[2] + p2[2]) / 2];
  lineTube(parent, [p1, mid, p2], 0.022, plain(0x181e19));
}

function crate(parent, x, y, z, size = 0.9, ry = 0) {
  const g = new THREE.Group(); g.position.set(x, y, z); g.rotation.y = ry; parent.add(g);
  box(g, 0, size / 2, 0, size, size, size, mat('wood', 0x5a4832));
  for (const s of [-1, 1]) {
    box(g, s * (size / 2 - 0.03), size / 2, 0, 0.04, size * 0.96, size * 0.96, mat('rust'));
    box(g, 0, size / 2, s * (size / 2 - 0.03), size * 0.96, size * 0.96, 0.04, mat('rust'));
  }
  return g;
}

function barrel(parent, x, z, color = 0x3d4e42) {
  const g = new THREE.Group(); g.position.set(x, 0, z); parent.add(g);
  cyl(g, 0, 0.52, 0, 0.32, 1.04, mat('metal', color), 12);
  for (const dy of [0.2, 0.52, 0.84]) {
    cyl(g, 0, dy, 0, 0.335, 0.04, mat('rust'), 12);
  }
  return g;
}

export function createWorld(scene, customProps = null) {
  const root = new THREE.Group(); root.name = 'ТЕЛЕЦЕНТР / 1996'; scene.add(root);
  const interactables = new Map(); const lamps = []; const monitors = []; const fans = []; const tapeObjects = []; const fuseObjects = [];
  const wall = mat('plaster'), floor = mat('floor', 0xffffff, 5), ceiling = mat('plaster', 0x666959, 4), trim = mat('metal', 0x526752), timber = mat('wood'), rusty = mat('rust');

  // Continuous master floor and ceiling slab: ZERO black voids or gaps anywhere!
  // Spans full station footprint: width 25.5m (X: -12.75 to 12.75), length 62m (Z: -55 to 7)
  box(root, 0, -0.15, -24, 25.5, 0.3, 62, floor);
  box(root, 0, 3.58, -24, 25.5, 0.16, 62, ceiling);

  function wallBox(x, z, w, d) {
    box(root, x, 1.75, z, w, 3.5, d, wall);
    box(root, x, 0.52, z, w + 0.015, 1.04, d + 0.015, mat('plaster', 0x617663));
    box(root, x, 1.065, z, w + 0.027, 0.065, d + 0.027, trim);
    box(root, x, 0.08, z, w + 0.045, 0.16, d + 0.045, mat('wood', 0x7b725a));
  }

  // End walls of the long corridor and perimeter
  wallBox(-2.15, 6, 1.1, 0.3);
  wallBox(2.15, 6, 1.1, 0.3);
  box(root, 0, 3.22, 6, 3.2, 0.56, 0.3, wall);
  wallBox(0, -54, 5.3, 0.3);

  // Outer Room Walls and Dividing Partition Walls
  const doorObjects = new Map();
  for (const side of [-1, 1]) {
    // Outer perimeter walls (North, South, East, West)
    wallBox(side * 12, -24, 0.3, 60.3);
    wallBox(side * 7.25, 6, 9.5, 0.3);
    wallBox(side * 7.25, -54, 9.5, 0.3);

    // Transverse Dividing Partition Walls separating rooms
    // At z = -9: separates Archive/Apparatus from Generator/Studio 04
    wallBox(side * 7.25, -9, 9.5, 0.3);

    // At z = -24: separates Generator/Studio 04 from Vent/Server (with loop doorway at x = side * 10.5)
    wallBox(side * 5.875, -24, 6.75, 0.3);
    box(root, side * 10.5, 3.13, -24, 2.5, 0.74, 0.3, wall);
    wallBox(side * 11.875, -24, 0.5, 0.3);

    // At z = -39: separates Vent/Server from Tech Storage/Dispatcher
    wallBox(side * 7.25, -39, 9.5, 0.3);

    // Longitudinal Corridor Partition Walls at x = side * 2.5
    const cx = side * 2.5;
    // Solid corridor segments between doorways (doorways centered at z = -1.5, -16.5, -31.5, -46.5)
    wallBox(cx, 2.85, 0.22, 6.3);
    wallBox(cx, -9.0, 0.22, 12.6);
    wallBox(cx, -24.0, 0.22, 12.6);
    wallBox(cx, -39.0, 0.22, 12.6);
    wallBox(cx, -50.85, 0.22, 6.3);

    // Doorway portals at z = -1.5, -16.5, -31.5, -46.5
    for (const z of [-1.5, -16.5, -31.5, -46.5]) {
      box(root, cx, 3.13, z, 0.24, 0.74, 2.4, wall);
      for (const edge of [-1, 1]) box(root, cx, 1.4, z + edge * 1.19, 0.4, 2.8, 0.16, trim);
      box(root, cx, 2.79, z, 0.4, 0.14, 2.5, trim);

      let doorId = null;
      if (side < 0) {
        if (z === -1.5) doorId = 'door-archive';
        else if (z === -16.5) doorId = 'door-generator';
        else if (z === -31.5) doorId = 'door-vent';
        else if (z === -46.5) doorId = 'door-tech';
      } else {
        if (z === -16.5) doorId = 'door-studio';
        else if (z === -31.5) doorId = 'door-server';
        else if (z === -46.5) doorId = 'door-dispatch';
      }

      if (doorId) {
        const doorPivot = new THREE.Group();
        doorPivot.position.set(cx, 1.35, z + 1.05);
        root.add(doorPivot);

        const doorMat = mat('wood', 0x8a9274);
        box(doorPivot, 0, 0, -1.05, 0.12, 2.65, 2.1, doorMat);
        box(doorPivot, -side * 0.1, -0.1, -1.9, 0.08, 0.06, 0.16, rusty);
        box(doorPivot, side * 0.1, -0.1, -1.9, 0.08, 0.06, 0.16, rusty);

        // Keycard Reader box on corridor side
        const readerMat = mat('metal', 0x1a221a);
        box(root, cx - side * 0.22, 1.4, z - 1.25, 0.12, 0.28, 0.18, readerMat);
        const ledMat = new THREE.MeshBasicMaterial({ color: 0xe04434 });
        const led = box(root, cx - side * 0.29, 1.48, z - 1.25, 0.03, 0.06, 0.06, ledMat);
        const ledLight = new THREE.PointLight(0xe04434, 1.2, 2.5);
        ledLight.position.set(cx - side * 0.35, 1.48, z - 1.25);
        root.add(ledLight);

        doorObjects.set(doorId, {
          id: doorId,
          pivot: doorPivot,
          side,
          ledMat,
          ledLight,
        });
        interactables.set(doorId, doorPivot);
      }
    }
  }

  // Ceiling Lamps along the 60m Corridor
  for (const z of [3, -4, -11, -18, -25, -32, -39, -46, -51]) {
    box(root, 0, 3.4, z, 1.18, 0.08, 0.38, mat('metal'));
    box(root, 0, 3.33, z, 0.94, 0.055, 0.19, glow(0xf0d29a, 1.9));
    const lamp = new THREE.PointLight(0xffd298, 28, 12, 2); lamp.position.set(0, 2.95, z); root.add(lamp);
    lamps.push({ light: lamp, base: 28, offset: rnd() * 60 });
  }

  // Ceiling Lamps in all 8 Sector Rooms
  const roomCenters = [
    { x: -7.25, z: -1.5 }, { x: 7.25, z: -1.5 },
    { x: -7.25, z: -16.5 }, { x: 7.25, z: -16.5 },
    { x: -7.25, z: -31.5 }, { x: 7.25, z: -31.5 },
    { x: -7.25, z: -46.5 }, { x: 7.25, z: -46.5 },
  ];
  for (const r of roomCenters) {
    box(root, r.x, 3.39, r.z, 1.35, 0.12, 0.38, mat('metal'));
    box(root, r.x, 3.3, r.z, 1.13, 0.045, 0.19, glow(r.x > 0 ? 0x93b9a0 : 0xddbb7d, 1.1));
    const l = new THREE.PointLight(r.x > 0 ? 0xa4c5b0 : 0xf0bd7a, 24, 13, 2); l.position.set(r.x, 2.9, r.z); root.add(l);
    lamps.push({ light: l, base: 24, offset: rnd() * 60 });
  }

  root.add(new THREE.HemisphereLight(0x829d94, 0x343124, 0.48));

  // Overhead corridor pipes and ventilation ductwork
  for (const x of [-1.85, -1.55]) {
    const p = cyl(root, x, 3.16, -24, 0.045, 58, rusty); p.rotation.x = Math.PI / 2;
    for (let z = -52; z < 5; z += 4) cyl(root, x, 3.16, z, 0.071, 0.095, trim).rotation.x = Math.PI / 2;
  }

  // Sagging Industrial Ceiling Wire Bundles
  for (const z of [-6, -14, -22, -29, -37, -44, -50]) {
    hangingCable(root, [-1.8, 3.42, z - 2], [1.8, 3.42, z + 2], 0.32);
    hangingCable(root, [1.6, 3.42, z - 1], [-1.6, 3.42, z + 3], 0.42);
  }

  // Yellow-Black Hazard Striping on Floors
  for (const z of [-1.5, -16.5, -31.5, -46.5]) {
    for (const side of [-1, 1]) hazardTape(root, side * 2.5, z, 0.8, 2.3);
  }
  hazardTape(root, -8, -17.5, 3.5, 0.6); // Front of Generator
  for (const side of [-1, 1]) hazardTape(root, side * 10.5, -24, 2.4, 0.6); // Loop doorways
  hazardTape(root, 0, 5.2, 2.4, 0.6); // Exit Doorway

  // Fire Extinguishers on Corridor Walls
  fireExtinguisher(root, -2.35, 1.1, 1.5, Math.PI / 2);
  fireExtinguisher(root, 2.35, 1.1, -13.5, -Math.PI / 2);
  fireExtinguisher(root, -2.35, 1.1, -28.5, Math.PI / 2);
  fireExtinguisher(root, 2.35, 1.1, -43.5, -Math.PI / 2);

  // Industrial Wall Clocks (Frozen at 02:17)
  wallClock(root, -2.35, 2.5, 3.0, Math.PI / 2);
  wallClock(root, 11.82, 2.5, -1.5, -Math.PI / 2);
  wallClock(root, 11.82, 2.5, -46.5, -Math.PI / 2);

  // Safety Signs & Posters
  label(root, ['ПЛАН ЕВАКУАЦІЇ', 'ПРИ НАДЗВИЧАЙНОМУ СТАНІ'], -2.36, 1.9, 2.5, 1.1, 0.7, Math.PI / 2, { bg: '#2b3628', fg: '#c4caa8', font: 17, width: 224, height: 128 });
  label(root, ['ТИША В СТУДІЇ', 'ІДЕ ПРЯМИЙ ЗАПИС'], 2.65, 2.3, -14.5, 1.3, 0.45, -Math.PI / 2, { bg: '#801818', fg: '#ffffff', font: 22, width: 256, height: 80 });

  // Open entrance connects the building and the courtyard without a loading screen.
  const entranceDoor = box(root, -1.48, 1.38, 6.8, 1.72, 2.7, 0.12, mat('metal', 0x6b8065));
  entranceDoor.rotation.y = Math.PI / 2;
  const exitLamp = box(root, 0, 2.99, 5.8, 1.12, 0.31, 0.06, glow(0xdd3322, 1.5), Math.PI);
  label(root, ['ВИХІД'], 0, 2.99, 5.78, 1.05, 0.28, Math.PI, { font: 28, width: 192, height: 48, bg: '#193729', fg: '#b2d7b5' });
  const exit = new THREE.Object3D(); exit.position.set(0, 1.5, 5.5); root.add(exit); interactables.set('exit', exit);

  // Sector 1: Archive (x=-7.25, z=-1.5)
  const deskLight = new THREE.PointLight(0xffb760, 4, 3.2, 2); deskLight.position.set(-8.62, 1.32, -2.65); root.add(deskLight);
  label(root, ['АРХІВ ЗАПИСІВ', '1986 — 1996'], -9, 2.65, -6.825, 2.15, 0.65, 0, { bg: '#5c5943', fg: '#bab492', font: 25, width: 256, height: 96 });

  // Sector 1: Apparatus Room (x=7.25, z=-1.5)
  const mic = new THREE.Group(); mic.position.set(10, 0.98, -2); root.add(mic); cyl(mic, 0, 0.16, 0, 0.024, 0.32, trim); box(mic, 0, 0.33, 0, 0.2, 0.14, 0.17, mat('metal'));
  const broadcast = new THREE.Group(); broadcast.position.set(10, 1.2, -2); root.add(broadcast); interactables.set('broadcast', broadcast);
  const broadcastBulb = box(root, 9.85, 1.09, -1.75, 0.12, 0.055, 0.12, glow(0x93442a, 0.4));
  label(root, ['ПРЯМИЙ ЕФІР', '96.4 MHz'], 11.82, 2.59, -2, 1.8, 0.6, -Math.PI / 2, { bg: '#242d22', fg: '#8b9e7b', width: 256, height: 80, font: 26 });
  label(root, ['СХЕМА АВАРІЙНОГО ЕФІРУ', 'ТРАНСЛЯЦІЯ 96.4 МГц'], 11.82, 1.9, -4.5, 1.4, 0.6, -Math.PI / 2, { bg: '#1c241c', fg: '#a5caa0', font: 18, width: 256, height: 96 });

  // Sector 1: Generator Room (x=-7.25, z=-16.5)
  box(root, -11.79, 1.45, -16.5, 0.28, 1.25, 0.88, mat('metal', 0x879471));
  const powerLever = new THREE.Group(); powerLever.position.set(-11.59, 1.27, -16.5); root.add(powerLever); box(powerLever, 0.12, 0, 0, 0.25, 0.075, 0.075, rusty); box(powerLever, 0.26, 0.1, 0, 0.055, 0.28, 0.14, plain(0x7a392d));
  const power = new THREE.Object3D(); power.position.set(-11, 1.4, -16.5); root.add(power); interactables.set('power', power);
  const powerLamp = box(root, -11.6, 1.98, -16.5, 0.035, 0.09, 0.09, glow(0xe0522c, 0.7));
  label(root, ['ОБЕРЕЖНО! 380В', 'ВСТАНОВІТЬ 2 ЗАПОБІЖНИКИ'], -11.79, 2.3, -16.5, 1.6, 0.5, Math.PI / 2, { bg: '#8f2015', fg: '#ffea70', font: 19, width: 256, height: 80 });

  // Sector 1: Studio 04 (x=7.25, z=-16.5)
  box(root, 8, 0.13, -21.55, 7, 0.26, 2.6, timber);
  for (let i = 0; i < 30; i++) { const x = 4.4 + i * 0.24; box(root, x, 1.9, -22.67, 0.26, 3.3, 0.22, mat('fabric', i % 2 ? 0x494e39 : 0x3e4935)); }

  // Sector 2: Vent Room (x=-7.25, z=-31.5)
  const bigFan = new THREE.Group(); bigFan.position.set(-11.6, 1.8, -31.5); bigFan.rotation.y = Math.PI / 2; root.add(bigFan);
  cyl(bigFan, 0, 0, 0, 1.3, 0.3, mat('metal'), 16, Math.PI / 2);
  for (let i = 0; i < 6; i++) box(bigFan, 0, 0, 0, 0.14, 2.2, 0.05, trim, i * Math.PI / 3);
  fans.push(bigFan);
  label(root, ['ВЕНТВУЗОЛ', 'СЕКТОР Б'], -2.364, 2.45, -30.5, 1.15, 0.6, Math.PI / 2, { font: 25, width: 224, height: 96, bg: '#28372b', fg: '#c7c0a3' });
  label(root, ['УВАГА! ВЕНТИЛЯЦІЙНА ШАХТА', 'НЕ ВМИКАТИ БЕЗ ЗАПОБІЖНИКА'], -11.79, 2.3, -28.5, 1.6, 0.5, Math.PI / 2, { bg: '#916812', fg: '#fffae0', font: 18, width: 256, height: 80 });

  // Sector 2: Server Room (x=7.25, z=-31.5)
  label(root, ['СЕРВЕРНА', 'ОБЧИСЛЮВАЛЬНИЙ ЦЕНТР'], 2.364, 2.45, -30.5, 1.15, 0.6, -Math.PI / 2, { font: 21, width: 256, height: 96, bg: '#28372b', fg: '#c7c0a3' });

  // Sector 2: Tech Storage (x=-7.25, z=-46.5)
  const techDeskLight = new THREE.PointLight(0xffb760, 3.5, 3, 2); techDeskLight.position.set(-8.5, 1.3, -50.5); root.add(techDeskLight);
  label(root, ['ТЕХНІЧНИЙ СКЛАД', 'РЕЗЕРВНИЙ АРХІВ'], -2.364, 2.45, -45.5, 1.15, 0.6, Math.PI / 2, { font: 22, width: 256, height: 96, bg: '#28372b', fg: '#c7c0a3' });

  // Sector 2: Dispatcher Room (x=7.25, z=-46.5)
  label(root, ['ДИСПЕТЧЕРСЬКА', 'РАДІОВЕЖА № 3'], 2.364, 2.45, -45.5, 1.15, 0.6, -Math.PI / 2, { font: 23, width: 224, height: 96, bg: '#28372b', fg: '#c7c0a3' });

  // Corridor Doorway Labels
  label(root, ['АРХІВ', '01'], -2.364, 2.45, -0.15, 1.15, 0.6, Math.PI / 2, { font: 28, width: 192, height: 96, bg: '#28372b', fg: '#c7c0a3' });
  label(root, ['АПАРАТНА', '02'], 2.364, 2.45, -0.15, 1.15, 0.6, -Math.PI / 2, { font: 25, width: 224, height: 96, bg: '#28372b', fg: '#c7c0a3' });
  label(root, ['ГЕНЕРАТОР', '03'], -2.364, 2.45, -15.14, 1.15, 0.6, Math.PI / 2, { font: 24, width: 224, height: 96, bg: '#28372b', fg: '#c7c0a3' });
  label(root, ['СТУДІЯ', '04'], 2.364, 2.45, -15.14, 1.15, 0.6, -Math.PI / 2, { font: 28, width: 192, height: 96, bg: '#28372b', fg: '#c7c0a3' });

  // Dynamic Station Props (loaded from customProps if present, or DEFAULT_PROPS)
  const propsToSpawn = Array.isArray(customProps) && customProps.length > 0 ? customProps : DEFAULT_PROPS;
  for (const p of propsToSpawn) {
    spawnPropMesh(root, p, interactables, monitors, fans);
  }

  // Tapes (3 cassettes in Archive, Studio 04, and Tech Storage)
  for (const [id, x, y, z] of [['tape-1', -8, 1.1, -3], ['tape-2', 8, 1.1, -16.5], ['tape-3', -8, 1.1, -50.5]]) {
    const item = cassette(root, x, y, z, -0.13, true);
    item.userData.id = id; item.userData.baseY = y; item.userData.baseRotation = -0.13;
    interactables.set(id, item); tapeObjects.push(item);
  }

  // Industrial Collectible Fuses (Fuse 1 in Vent room, Fuse 2 in Server room)
  const f1 = industrialFuse(root, -8, 1.1, -26.5, 0.25, 'fuse-1');
  interactables.set('fuse-1', f1); fuseObjects.push(f1);
  const f2 = industrialFuse(root, 10.5, 1.1, -31.5, -0.35, 'fuse-2');
  interactables.set('fuse-2', f2); fuseObjects.push(f2);

  // Keycards (Guard Keycard at CP booth, Engineer Keycard in Archive)
  const keycardObjects = [];
  function createKeycard(id, x, y, z, colorHex) {
    const g = new THREE.Group();
    g.position.set(x, y, z);
    box(g, 0, 0, 0, 0.42, 0.03, 0.28, mat('plastic', colorHex));
    box(g, -0.09, 0.02, 0, 0.12, 0.01, 0.14, mat('metal', 0xd5bd77));
    const l = new THREE.PointLight(colorHex, 4, 3, 2);
    l.position.set(0, 0.2, 0);
    g.add(l);
    g.userData.id = id;
    g.userData.light = l;
    root.add(g);
    interactables.set(id, g);
    keycardObjects.push(g);
    return g;
  }
  createKeycard('keycard-guard', -15, 1.1, 14, 0x50c878);
  createKeycard('keycard-engineer', -8, 1.1, 2, 0x48a0ff);

  // Fuel canister in courtyard service shed (x: 18, z: 11)
  const canisterMesh = fuelCanister(root, 18, 11, 0.3);
  canisterMesh.userData.id = 'fuel-canister';
  const canisterLight = new THREE.PointLight(0xff7733, 4, 3.5, 2);
  canisterLight.position.set(18, 0.8, 11);
  root.add(canisterLight);
  canisterMesh.userData.light = canisterLight;
  interactables.set('fuel-canister', canisterMesh);

  // Pressure valve in vent room (x: -10.5, z: -34.5)
  const valveMesh = fuelValve(root, -10.5, -34.5, 0);
  valveMesh.userData.id = 'pressure-valve';
  interactables.set('pressure-valve', valveMesh);

  let current = { tapes: [], fuses: [], keycards: [], doors: {}, power: false, broadcast: false };
  let lastTapesSig = null, lastFusesSig = null, lastKeycardsSig = null, lastPowerState = null, lastBroadcastState = null, lastNightmare = null;

  function setState(state = {}) {
    current = state;
    const tapesSig = (state.tapes || []).join(',');
    if (tapesSig !== lastTapesSig) {
      lastTapesSig = tapesSig;
      for (const tape of tapeObjects) tape.visible = !(state.tapes || []).includes(tape.userData.id);
    }
    const fusesSig = (state.fuses || []).join(',');
    if (fusesSig !== lastFusesSig) {
      lastFusesSig = fusesSig;
      for (const fuse of fuseObjects) {
        const collected = (state.fuses || []).includes(fuse.userData.id);
        fuse.visible = !collected;
        if (fuse.userData.light) fuse.userData.light.visible = !collected;
      }
    }

    const keycardsSig = (state.keycards || []).join(',');
    if (keycardsSig !== lastKeycardsSig) {
      lastKeycardsSig = keycardsSig;
      for (const kc of keycardObjects) {
        const collected = (state.keycards || []).includes(kc.userData.id);
        kc.visible = !collected;
        if (kc.userData.light) kc.userData.light.visible = !collected;
      }
    }

    const hasFuel = Boolean(state.canister || state.quest?.fuelAdded);
    if (canisterMesh) {
      canisterMesh.visible = !hasFuel;
      if (canisterMesh.userData.light) canisterMesh.userData.light.visible = !hasFuel;
    }

    const hasValve = Boolean(state.valveOpened || state.quest?.valveOpened);
    if (valveMesh) {
      valveMesh.rotation.y = hasValve ? Math.PI : 0;
    }

    if (state.doors) {
      for (const [id, dObj] of doorObjects) {
        const d = state.doors[id] || { locked: false, open: false };
        const isOpen = Boolean(d.open);
        const isLocked = Boolean(d.locked);
        dObj.pivot.rotation.y = isOpen ? -dObj.side * (Math.PI / 2.2) : 0;
        const color = isLocked ? 0xe04434 : (isOpen ? 0x50c878 : 0xd5bd77);
        dObj.ledMat.color.setHex(color);
        dObj.ledLight.color.setHex(color);
      }
    }

    const p = Boolean(state.power);
    if (p !== lastPowerState) {
      lastPowerState = p;
      powerLever.rotation.z = p ? -0.7 : 0.55;
      powerLamp.material.emissive.setHex(p ? 0x87c886 : 0xe0522c);
      powerLamp.material.color.copy(powerLamp.material.emissive);
      for (const { screen } of monitors) screen.material.color.setHex(p ? 0xffffff : 0x637768);
    }
    const b = Boolean(state.broadcast);
    if (b !== lastBroadcastState) {
      lastBroadcastState = b;
      broadcastBulb.material.emissiveIntensity = b ? 3.5 : p ? 1 : 0.4;
      broadcastBulb.material.color.setHex(b ? 0xdd2211 : 0x93442a);
      broadcastBulb.material.emissive.copy(broadcastBulb.material.color);
      exitLamp.material.emissive.setHex(b ? 0x33ff55 : 0xdd3322);
      exitLamp.material.color.copy(exitLamp.material.emissive);
    }
  }

  function update(time, dt, state) {
    if (state) setState(state);
    const reducedMotion = current.reducedMotion === true;
    const flickerEnabled = current.flicker !== false && !reducedMotion;
    const isNightmare = Boolean(current.broadcast);

    if (isNightmare !== lastNightmare) {
      lastNightmare = isNightmare;
      for (const lamp of lamps) {
        if (!lamp.normalColor) lamp.normalColor = lamp.light.color.clone();
        if (isNightmare) lamp.light.color.setHex(0xff1505);
        else lamp.light.color.copy(lamp.normalColor);
      }
    }

    for (const item of tapeObjects) if (item.visible) {
      item.position.y = item.userData.baseY + (reducedMotion ? 0 : Math.sin(time * 1.8) * 0.023);
      item.userData.light.intensity = flickerEnabled ? 0.65 + Math.sin(time * 2) * 0.2 : 0.65;
    }

    for (const item of fuseObjects) if (item.visible) {
      item.userData.light.intensity = flickerEnabled ? 1.0 + Math.sin(time * 3.5) * 0.35 : 1.0;
    }

    for (const kc of keycardObjects) if (kc.visible) {
      if (kc.userData.light) kc.userData.light.intensity = flickerEnabled ? 0.9 + Math.sin(time * 2.5) * 0.25 : 0.9;
    }

    // Ceiling lamps flicker, and pulse blood-red in nightmare escape phase!
    for (const lamp of lamps) {
      const t = time + lamp.offset;
      const flicker = Math.sin(t * 1.7) * Math.sin(t * 13.6);
      if (isNightmare) {
        lamp.light.intensity = lamp.base * (flickerEnabled ? 0.7 + Math.sin(time * 3) * 0.2 : 0.8);
      } else {
        lamp.light.intensity = lamp.base * (flickerEnabled ? (flicker > 0.94 ? 0.15 : 0.91 + Math.sin(t * 2.3) * 0.025) : 0.91);
      }
    }

    if (current.power && !reducedMotion) for (const f of fans) f.rotation.z += dt * 14;
  }

  // Merge scenery by material
  const dynamic = new Set([
    ...tapeObjects, ...fuseObjects, ...keycardObjects, canisterMesh, valveMesh, powerLever, powerLamp, broadcastBulb, exitLamp,
    ...fans, ...monitors.map(m => m.screen),
    ...lockers.map(l => interactables.get(l.id)),
  ]);
  for (const d of doorObjects.values()) {
    dynamic.add(d.pivot);
    dynamic.add(d.led);
  }
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
    geometries.forEach(g => g.dispose());
  }
  setState(current);
  return { root, interactables, update, setState, rooms: [] };
}

export {
  table, chair, shelf, serverRack, crate, barrel, recordingCamera, crt, locker, industrialFuse, textTexture,
  guardBooth, barrierGate, floodlightTower, cableSpool, abandonedCar, turnstile, honorBoard, scheduleBoard,
  fireBoard, cableTray, trashBin, debris, cardCatalog, archivistDesk, vhsStack, anchorDesk, chromakeyBackdrop,
  micStand, ceilingSpotlight, dieselGenerator, fuelCanister, fuelValve, fusePanel, airDuct, ventGrate,
  cableBundle, mixingConsole, oscilloscope, monitorWall, acousticPanel
};
