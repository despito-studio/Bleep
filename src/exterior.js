import * as THREE from 'three';
import { mergeGeometries } from 'three/addons/utils/BufferGeometryUtils.js';

// Original, procedural low-poly exterior assets. Coordinates share the indoor map.
let randomSeed=6201996;
const random=()=>{randomSeed=(1664525*randomSeed+1013904223)>>>0;return randomSeed/4294967296;};
const materials=new Map();
const textures=new Map();
const plain=(color,roughness=.94)=>{
  const key=`plain-${color}-${roughness}`;
  if(!materials.has(key))materials.set(key,new THREE.MeshStandardMaterial({color,roughness,flatShading:true}));
  return materials.get(key);
};
function texture(kind){
  if(textures.has(kind))return textures.get(kind);
  const canvas=document.createElement('canvas');canvas.width=canvas.height=128;
  const context=canvas.getContext('2d');
  const base={concrete:'#737768',asphalt:'#4b514b',ivory:'#b9b9a2',metal:'#5c6b5c',brick:'#666951',bark:'#4e493b',rust:'#79523c',wood:'#706953'}[kind]||'#606657';
  context.fillStyle=base;context.fillRect(0,0,128,128);
  const pixels=context.getImageData(0,0,128,128);
  for(let i=0;i<pixels.data.length;i+=4){const n=(random()-.5)*(kind==='asphalt'?49:32);for(let j=0;j<3;j++)pixels.data[i+j]=Math.max(0,Math.min(255,pixels.data[i+j]+n));}
  context.putImageData(pixels,0,0);
  if(kind==='brick'){
    context.strokeStyle='#3b4138';context.lineWidth=2;
    for(let y=0;y<=128;y+=16){context.beginPath();context.moveTo(0,y);context.lineTo(128,y);context.stroke();for(let x=(y%32===0?0:16);x<128;x+=32){context.beginPath();context.moveTo(x,y);context.lineTo(x,y+16);context.stroke();}}
  }else if(kind==='ivory'||kind==='metal'){
    for(let i=0;i<45;i++){context.fillStyle=`rgba(75,51,30,${random()*.35})`;context.fillRect(random()*128,random()*128,1+random()*12,1+random()*3);}
    context.fillStyle='rgba(28,41,30,.09)';for(let i=0;i<9;i++)context.fillRect(random()*128,60+random()*68,3+random()*9,68);
  }else if(kind==='wood'||kind==='bark'){
    for(let i=0;i<100;i++){context.fillStyle=`rgba(15,22,14,${random()*.32})`;context.fillRect(random()*128,random()*128,1+random()*2,20+random()*85);}
  }else{
    for(let i=0;i<40;i++){context.fillStyle=`rgba(13,27,17,${random()*.09})`;context.fillRect(random()*128,random()*128,5+random()*24,3+random()*28);}
    if(kind==='concrete'){context.strokeStyle='#424e40';for(let i=0;i<6;i++){let x=random()*128,y=random()*128;context.beginPath();context.moveTo(x,y);for(let j=0;j<5;j++){x+=(random()-.5)*9;y+=3+random()*13;context.lineTo(x,y);}context.stroke();}}
  }
  const result=new THREE.CanvasTexture(canvas);result.colorSpace=THREE.SRGBColorSpace;result.magFilter=THREE.NearestFilter;result.minFilter=THREE.NearestMipmapNearestFilter;result.wrapS=result.wrapT=THREE.RepeatWrapping;textures.set(kind,result);return result;
}
function textured(kind,color=0xffffff,repeat=1){
  const key=`${kind}-${color}-${repeat}`;
  if(!materials.has(key)){const map=texture(kind).clone();map.repeat.set(repeat,repeat);map.needsUpdate=true;materials.set(key,new THREE.MeshStandardMaterial({map,color,roughness:kind==='ivory'?.68:.94,metalness:kind==='metal'?.25:.03,flatShading:true}));}
  return materials.get(key);
}
function emissive(color,intensity=1){return new THREE.MeshStandardMaterial({color,emissive:color,emissiveIntensity:intensity,roughness:.6});}
function box(parent,x,y,z,w,h,d,material,rotationY=0){const result=new THREE.Mesh(new THREE.BoxGeometry(w,h,d),material);result.position.set(x,y,z);result.rotation.y=rotationY;result.receiveShadow=true;parent.add(result);return result;}
function cylinder(parent,x,y,z,r,h,material,sides=8){const result=new THREE.Mesh(new THREE.CylinderGeometry(r,r,h,sides),material);result.position.set(x,y,z);parent.add(result);return result;}
function cable(parent,points,width,material){const curve=new THREE.CatmullRomCurve3(points.map(point=>new THREE.Vector3(...point)));const mesh=new THREE.Mesh(new THREE.TubeGeometry(curve,Math.max(5,points.length*4),width,4,false),material);parent.add(mesh);return mesh;}
function panel(parent,vertices,material){const geometry=new THREE.BufferGeometry();geometry.setAttribute('position',new THREE.Float32BufferAttribute(vertices.flat(),3));geometry.setAttribute('uv',new THREE.Float32BufferAttribute([0,0,1,0,1,1,0,1],2));geometry.setIndex([0,1,2,0,2,3]);geometry.computeVertexNormals();const mesh=new THREE.Mesh(geometry,material);parent.add(mesh);return mesh;}
function sign(parent,lines,x,y,z,w,h,rotationY=0,{background='#27372b',foreground='#c1baa0',font=28,width=512,height=128}={}){
  const canvas=document.createElement('canvas');canvas.width=width;canvas.height=height;const ctx=canvas.getContext('2d');ctx.fillStyle=background;ctx.fillRect(0,0,width,height);ctx.strokeStyle=foreground;ctx.globalAlpha=.25;ctx.strokeRect(6,6,width-12,height-12);ctx.globalAlpha=1;ctx.font=`bold ${font}px monospace`;ctx.textAlign='center';ctx.textBaseline='middle';ctx.fillStyle=foreground;lines.forEach((line,i)=>ctx.fillText(line,width/2,height/2+(i-(lines.length-1)/2)*(font+9)));
  for(let i=0;i<120;i++){ctx.fillStyle=`rgba(20,27,18,${random()*.22})`;ctx.fillRect(random()*width,random()*height,random()*18,1+random()*2);}
  const map=new THREE.CanvasTexture(canvas);map.colorSpace=THREE.SRGBColorSpace;map.magFilter=THREE.NearestFilter;map.minFilter=THREE.LinearFilter;
  const mesh=new THREE.Mesh(new THREE.PlaneGeometry(w,h),new THREE.MeshStandardMaterial({map,roughness:.95,side:THREE.DoubleSide,emissive:0x344833,emissiveIntensity:.2}));mesh.position.set(x,y,z);mesh.rotation.y=rotationY;parent.add(mesh);return mesh;
}

function conifer(parent,x,z,height=7){
  const group=new THREE.Group();group.position.set(x,0,z);group.rotation.y=random()*6.28;parent.add(group);
  cylinder(group,0,height*.3,0,.14+height*.012,height*.62,textured('bark'),6);
  for(let level=0;level<4;level++){
    const size=(1-level*.2)*height*.27;const cone=new THREE.Mesh(new THREE.ConeGeometry(size,height*.43,7,1),plain(level%2?0x263c2d:0x314334));cone.position.set((random()-.5)*.12,height*(.32+level*.18),0);group.add(cone);
  }
  return group;
}
function shrub(parent,x,z,scale=1){
  const group=new THREE.Group();group.position.set(x,0,z);parent.add(group);
  for(let i=0;i<4;i++){const mesh=new THREE.Mesh(new THREE.IcosahedronGeometry((.25+random()*.32)*scale,0),plain(i%2?0x384537:0x485440));mesh.position.set((random()-.5)*scale,.2*scale,(random()-.5)*scale);mesh.scale.y=.6;group.add(mesh);}
}
function barrel(parent,x,z){
  const body=cylinder(parent,x,.49,z,.35,.98,textured('rust'),10);
  for(const y of [.12,.79])cylinder(parent,x,y,z,.36,.065,textured('metal'),10);
  cylinder(parent,x,.989,z,.32,.013,plain(0x485241),10);box(parent,x+.17,.996,z-.08,.065,.01,.065,plain(0x172b20));return body;
}

export function createVan(parent){
  const van=new THREE.Group();van.name='Broadcast service van · 1993';parent.add(van);
  const body=textured('ivory'),trim=textured('metal',0x828a77),black=plain(0x17221b),rubber=plain(0x101913),glass=new THREE.MeshStandardMaterial({color:0x213c39,roughness:.25,metalness:.3,transparent:true,opacity:.67,side:THREE.DoubleSide});
  const dirtyGlass=new THREE.MeshStandardMaterial({color:0x344c42,roughness:.39,metalness:.2,transparent:true,opacity:.72,side:THREE.DoubleSide});
  const wheels=[];const wheelPivots=[];
  // Chassis, long wheelbase, sill and separate front bonnet.
  box(van,0,.42,0,2.1,.22,4.7,black);box(van,0,.72,.05,2.22,.47,4.86,body);
  box(van,0,1.04,1.1,2.22,.25,2.65,body);box(van,0,1.11,-1.72,2.23,.25,1.4,body);
  box(van,0,.81,-2.49,2.33,.16,.14,trim);box(van,0,.69,2.52,2.34,.18,.15,trim);
  box(van,0,.54,-2.17,1.75,.08,.48,black);
  // Cargo roof and its metal ribs.
  box(van,0,2.11,.43,2.23,.12,4.03,body);box(van,0,2.19,.5,2.0,.045,3.57,textured('ivory',0xaeb19a));
  for(const z of [-.75,.25,1.25])box(van,0,2.223,z,1.99,.023,.035,trim);
  for(const side of [-1,1]){
    box(van,side*1.11,1.7,2.28,.11,.72,.24,body);
    box(van,side*1.11,1.72,-.4,.105,.78,.16,body);
    box(van,side*1.11,1.36,-1.06,.07,.47,1.19,body);
    box(van,side*1.126,1.7,-.95,.025,.48,1.07,dirtyGlass);
    box(van,side*1.145,1.95,-.97,.045,.04,1.19,trim);
    box(van,side*1.139,1.455,-1,.04,.045,1.19,trim);
    box(van,side*1.145,1.69,-1.44,.035,.53,.047,trim);
    box(van,side*1.145,1.69,-.82,.035,.53,.031,trim);
    box(van,side*1.152,1.25,-.72,.035,.045,.19,black);
    box(van,side*1.19,1.08,-1.56,.15,.18,.22,body);
    box(van,side*1.33,1.59,-1.58,.15,.24,.29,black);
    box(van,side*1.345,1.59,-1.568,.015,.18,.22,plain(0x768776,.24));
    box(van,side*1.132,.83,.1,.035,.11,4.52,plain(0x66715b));
    box(van,side*1.18,.62,-.5,.16,.075,1.8,trim);
    for(const z of [-1.63,1.67]){
      const wheel=new THREE.Group();wheel.position.set(side*1.11,.43,z);van.add(wheel);wheels.push(wheel);wheelPivots.push(wheel);
      const tire=cylinder(wheel,0,0,0,.425,.22,rubber,12);tire.rotation.z=Math.PI/2;
      const hub=cylinder(wheel,side*.123,0,0,.243,.02,trim,10);hub.rotation.z=Math.PI/2;
      const cap=cylinder(wheel,side*.139,0,0,.092,.025,plain(0x8a9380),8);cap.rotation.z=Math.PI/2;
      for(let i=0;i<5;i++){const angle=i/5*6.28;box(wheel,side*.149,Math.cos(angle)*.166,Math.sin(angle)*.166,.012,.051,.051,black);}
      // Squared arches suit 1990s stamped utility panels.
      box(van,side*1.129,.96,z,.075,.12,.99,trim);box(van,side*1.133,.73,z-.49,.075,.47,.09,trim);box(van,side*1.133,.73,z+.49,.075,.47,.09,trim);
    }
  }
  // The front windshield slopes forward towards the bonnet; two wipers remain visible.
  panel(van,[[-1.045,1.35,-2.02],[1.045,1.35,-2.02],[1.035,2.052,-1.53],[-1.035,2.052,-1.53]],glass);
  for(const side of [-1,1])cable(van,[[side*1.08,1.34,-2.02],[side*1.08,2.07,-1.53]],.062,body);
  box(van,0,2.07,-1.545,2.15,.075,.09,body);box(van,0,1.34,-2.026,2.15,.062,.08,trim);
  for(const side of [-1,1])cable(van,[[side*.65,1.39,-2.005],[side*.25,1.63,-1.84]],.012,black);
  box(van,0,.94,-2.452,.96,.2,.055,black);for(let i=0;i<4;i++)box(van,0,.86+i*.055,-2.484,.87,.017,.01,trim);
  const headlightMaterial=emissive(0xf5dda8,3.3);
  for(const side of [-1,1]){box(van,side*.815,1.0,-2.465,.43,.235,.06,headlightMaterial);box(van,side*1.047,1.0,-2.471,.07,.22,.066,emissive(0xd2924b,1.4));}
  sign(van,['AX 0417 TB'],0,.681,-2.575,.67,.155,Math.PI,{background:'#adae94',foreground:'#263426',font:29,width:256,height:64});
  // Left panel is solid; the right sliding panel uncovers a modeled interior.
  box(van,-1.11,1.52,.92,.09,1.13,2.57,body);
  box(van,1.11,1.57,1.96,.09,1.04,.59,body);
  const door=new THREE.Group();door.name='Animated passenger sliding door';door.position.set(1.12,0,.39);van.add(door);
  box(door,0,1.54,0,.085,1.09,1.72,body);box(door,.055,1.38,-.62,.055,.07,.24,black);box(door,.059,1.85,0,.015,.19,1.55,plain(0x9a9e84));
  sign(door,['BLACKPINE','BROADCAST'],.061,1.65,.05,1.12,.39,Math.PI/2,{background:'#858f78',foreground:'#263c2a',font:28,width:384,height:128});
  sign(van,['BLACKPINE','FIELD UNIT / 04'],-1.163,1.65,.85,1.78,.52,-Math.PI/2,{background:'#a3a68c',foreground:'#364b38',font:25,width:384,height:128});
  box(van,0,1.55,2.405,2.2,1.11,.08,body);box(van,0,1.56,2.454,.035,1.0,.017,trim);
  for(const side of [-1,1]){
    box(van,side*.56,1.77,2.458,.79,.42,.02,dirtyGlass);box(van,side*.94,1.08,2.479,.12,.31,.035,emissive(0x782e22,.8));box(van,side*.14,1.33,2.471,.095,.047,.035,black);
  }
  sign(van,['AX 0417 TB'],0,.79,2.54,.67,.155,0,{background:'#adae94',foreground:'#263426',font:29,width:256,height:64});
  // Interior: floor, two seats, steering wheel, radio console, cargo bench and equipment cases.
  box(van,0,.985,.16,1.99,.075,4.15,plain(0x394738));
  for(const side of [-1,1]){box(van,side*.52,1.17,-.73,.56,.14,.54,plain(0x465744));const seat=box(van,side*.52,1.53,-.465,.56,.61,.11,plain(0x526049));seat.rotation.x=-.09;box(van,side*.52,1.9,-.45,.34,.15,.12,plain(0x526049));}
  box(van,0,1.37,-1.64,1.96,.2,.44,plain(0x354235));box(van,.3,1.49,-1.47,.49,.07,.22,plain(0x1a2b21));box(van,.3,1.528,-1.43,.34,.015,.1,emissive(0x65794e,.25));
  const steering=new THREE.Mesh(new THREE.TorusGeometry(.2,.024,4,10),black);steering.position.set(-.53,1.51,-1.32);steering.rotation.x=-.45;van.add(steering);cable(van,[[-.53,1.51,-1.34],[-.53,1.3,-1.61]],.031,black);box(van,-.53,1.51,-1.32,.03,.36,.03,black);
  box(van,-.77,1.19,1.1,.42,.2,1.68,plain(0x42553f));box(van,-.92,1.42,1.1,.12,.34,1.68,plain(0x526249));
  for(const z of [.57,1.34]){box(van,.17,1.21,z,.73,.39,.55,textured('metal',0x778570));box(van,.17,1.42,z,.15,.03,.17,black);for(const x of [-.18,.51])box(van,x,1.22,z+.283,.045,.17,.02,trim);}
  box(van,0,2.02,.7,.25,.027,.22,emissive(0xe2bb71,.8));
  const antenna=cylinder(van,-.85,2.55,-1.25,.012,.76,black,5);antenna.rotation.z=.09;
  const lamp=new THREE.SpotLight(0xffd6a1,100,31,Math.PI/5.6,.64,1.25);lamp.position.set(0,1.05,-2.5);lamp.target.position.set(0,.05,-16);van.add(lamp,lamp.target);
  // Hazy, deliberately subtle cones reinforce the low-resolution lamp silhouette.
  const coneMaterial=new THREE.MeshBasicMaterial({color:0xc6b879,transparent:true,opacity:.036,depthWrite:false,side:THREE.DoubleSide});
  for(const side of [-1,1]){const cone=new THREE.Mesh(new THREE.ConeGeometry(2.2,9,14,1,true),coneMaterial);cone.rotation.x=Math.PI/2;cone.position.set(side*.82,.92,-6.93);van.add(cone);}
  return {van,door,wheels:wheelPivots,headlight:lamp};
}

export function createExterior(scene){
  const root=new THREE.Group();root.name='BLACKPINE · exterior courtyard';scene.add(root);
  const concrete=textured('concrete'),metal=textured('metal'),asphalt=textured('asphalt',0xffffff,18),brick=textured('brick',0xb5b9a2,3),rust=textured('rust'),black=plain(0x18281e);
  const props=new Map();const colliders=[{id:'van',minX:-8.35,maxX:-5.65,minZ:20.35,maxZ:25.65},{id:'service-shed',minX:15.8,maxX:20.8,minZ:8.3,maxZ:13.7}];
  box(root,0,-.13,21,44,.25,30,asphalt);box(root,0,-.21,44,72,.24,18,textured('asphalt',0x7d8775,18));
  box(root,0,-.13,9,8,.25,6,textured('concrete',0x849180,4));
  // Cracked aggregate edge rather than a step: the entrance stays wheelchair-flat.
  for(const side of [-1,1])box(root,side*15,.03,6.68,5.8,.08,1.2,concrete);
  for(const x of [-13,-8,-3,2,7]){
    box(root,x,.007,28,.075,.013,5.3,plain(0x8c8f72));box(root,x+2.1,.008,30.6,4.2,.013,.078,plain(0x8c8f72));
  }
  for(let i=0;i<5;i++)box(root,-7,.015,36.5+i*2.6,.08,.01,1.2,plain(0x9da086));
  sign(root,['04'],9,.015,28,1.4,1.4,0,{background:'#50594c',foreground:'#b4b28e',font:100,width:160,height:160}).rotation.x=-Math.PI/2;
  for(let i=0;i<12;i++){
    const puddle=new THREE.Mesh(new THREE.CircleGeometry(.65+random()*1.3,7),new THREE.MeshStandardMaterial({color:0x3e544b,roughness:.2,metalness:.48,transparent:true,opacity:.56}));puddle.rotation.x=-Math.PI/2;puddle.rotation.z=random()*6.28;puddle.position.set(-17+random()*34,.013,9+random()*25);puddle.scale.x=1.8;root.add(puddle);
  }
  // Front facade: the central 3.2-metre aperture is deliberately kept open.
  for(const side of [-1,1]){
    box(root,side*6.85,1.7,5.99,10.3,3.4,.18,brick);
    box(root,side*6.85,.31,6.11,10.3,.62,.13,textured('concrete',0x677662,3));
    for(const x of [3.4,7.5,11.3])box(root,side*x,1.72,6.18,.33,3.44,.43,concrete);
    for(const x of [4.95,9.47]){
      box(root,side*x,1.97,6.105,1.63,1.36,.06,black);box(root,side*x,1.98,6.16,1.49,1.19,.027,plain(0x2f4740,.4));
      for(const dx of [-.78,0,.78])box(root,side*x+dx,1.98,6.2,.045,1.39,.047,metal);
      for(const y of [1.34,2,2.65])box(root,side*x,y,6.2,1.6,.04,.047,metal);
      box(root,side*x,1.265,6.25,1.85,.12,.32,concrete);
      for(let i=0;i<7;i++)box(root,side*x-.65+i*.22,1.97,6.248,.013,1.22,.014,plain(0x596a52));
    }
  }
  box(root,0,3.26,6.045,3.4,.48,.32,concrete);
  for(const side of [-1,1])box(root,side*1.65,1.52,6.12,.14,3.04,.32,metal);
  box(root,0,3.54,6.17,24.45,.25,.52,concrete);box(root,0,3.75,5.93,24.35,.15,.18,textured('metal',0x747e66));
  // The entrance canopy, huge station type and one working practical light.
  box(root,0,3.21,7.08,5.3,.14,2.12,textured('metal',0x727b64));
  for(const side of [-1,1])box(root,side*2.47,1.6,7.8,.13,3.2,.13,textured('metal',0x9ea88c));
  sign(root,['BLACKPINE BROADCAST'],0,3.69,6.505,9,.55,0,{background:'#243a2b',foreground:'#c8bf8e',font:39,width:1024,height:96});
  sign(root,['04'],9.65,3.47,6.56,1.08,.65,0,{background:'#28362a',foreground:'#c4aa70',font:57,width:160,height:112});
  sign(root,['ТЕХНІЧНИЙ ВХІД','ЛИШЕ ДЛЯ ПЕРСОНАЛУ'],0,2.91,6.48,2.65,.31,0,{background:'#293c2c',foreground:'#b1b799',font:22,width:512,height:96});
  box(root,0,3.12,7.13,.83,.035,.24,emissive(0xf2ce83,2.5));
  const entranceLight=new THREE.PointLight(0xffd49a,29,15,1.65);entranceLight.position.set(0,2.92,7.9);root.add(entranceLight);
  sign(root,['ПІСЛЯ 02:17','НЕ ВХОДИТИ'],2.89,1.87,6.31,.86,.57,0,{background:'#918b5e',foreground:'#4a3325',font:27,width:256,height:160});
  box(root,-2.95,1.4,6.3,.23,.64,.1,textured('metal',0x8d9174));for(let i=0;i<4;i++)box(root,-2.95,1.52-i*.08,6.36,.11,.027,.012,black);
  // Service shed and radio equipment stay at the side of the playable courtyard.
  const shed=new THREE.Group();shed.position.set(18.3,0,11);root.add(shed);props.set('service-shed',shed);
  box(shed,0,1.25,0,5,2.5,5.4,textured('metal',0x778470,3));box(shed,0,2.55,0,5.4,.15,5.75,textured('metal',0x485b47,2));
  box(shed,0,1.19,2.73,1.3,2.35,.1,textured('metal',0x71785d));box(shed,.48,1.09,2.799,.035,.17,.037,black);
  sign(shed,['ОБЕРЕЖНО','380 V'],0,1.85,2.79,.76,.52,0,{background:'#aba465',foreground:'#3c3824',font:31,width:224,height:144});
  box(root,-16,.38,10.7,4.6,.17,.52,textured('wood'));for(const x of [-17.9,-14.1])box(root,x,.2,10.7,.15,.4,.45,metal);box(root,-16,.72,10.43,4.6,.39,.13,textured('wood'));
  const bin=cylinder(root,-19,.48,9.5,.36,.96,metal,8);props.set('bin',bin);
  // Perimeter fence with barbed wire outriggers: wide open vehicle gate at x=-7, z=36.
  const fenceWire=plain(0x738169);
  function fenceSegment(x1,z1,x2,z2){
    const dx=x2-x1,dz=z2-z1,length=Math.hypot(dx,dz),angle=Math.atan2(dx,dz);const segment=new THREE.Group();segment.position.set((x1+x2)/2,0,(z1+z2)/2);segment.rotation.y=angle;root.add(segment);
    for(const y of [.25,1.18,2.15])box(segment,0,y,0,.035,.035,length,metal);
    const step=.52;for(let i=0;i<=length/step;i++){const z=-length/2+i*step;box(segment,0,1.22,z,.022,2.13,.022,fenceWire);}
    const count=Math.ceil(length/3.4);for(let i=0;i<=count;i++)box(segment,0,1.22,-length/2+length*i/count,.075,2.44,.075,textured('metal',0x9aa087));
    // Top barbed wire strands sag between angled outrigger posts.
    for(let i=0;i<count;i++){
      const a=-length/2+length*i/count,b=-length/2+length*(i+1)/count;
      cable(segment,[[0,2.4,a],[0,2.25,(a+b)/2],[0,2.4,b]],.012,black);
      cable(segment,[[0.15,2.62,a],[0.15,2.48,(a+b)/2],[0.15,2.62,b]],.01,plain(0x5a6358));
      box(segment, 0.08, 2.52, a, 0.18, 0.02, 0.02, metal, -0.4);
    }
  }
  fenceSegment(-22,6,-22,36);fenceSegment(22,6,22,36);fenceSegment(-22,36,-10.4,36);fenceSegment(-3.6,36,22,36);
  fenceSegment(-10.4,36,-10.9,40.5);fenceSegment(-3.6,36,-3.1,40.5);
  sign(root,['BLACKPINE','PRIVATE PROPERTY'],12,1.45,35.94,3.25,.79,Math.PI,{background:'#384330',foreground:'#bab28b',font:35,width:512,height:144});
  for(const [x,z]of [[-20,19],[19,30]]){
    cylinder(root,x,3.32,z,.075,6.64,textured('metal',0x7c8975),8);cable(root,[[x,6.6,z],[x-.7,6.87,z],[x-1.6,6.85,z]],.056,metal);box(root,x-1.56,6.8,z,.62,.12,.31,metal);box(root,x-1.56,6.73,z,.43,.017,.19,emissive(0xcad3a3,.55));
  }
  // A broad soft practical lights the four lobby operators without washing out the station.
  const courtyardLight=new THREE.PointLight(0xe4d1a3,73,34,1.5);courtyardLight.position.set(4.5,5.7,22.5);root.add(courtyardLight);
  for(const x of [-24.5,24.5]){
    cylinder(root,x,6.5,12,.16,13,textured('bark'),7);box(root,x,11.3,12,3.7,.11,.15,textured('wood'));
    for(const dx of [-1.4,0,1.4]){cylinder(root,x+dx,11.61,12,.08,.42,plain(0x566b55),6);cable(root,[[x+dx,11.72,12],[x+dx,10.13,27],[x+dx,11.68,43]],.017,black);}
  }
  cable(root,[[-24.5,10.9,12],[-7,8.9,3],[9.8,9.25,.5]],.028,black);
  // Slender mast, horizontal antenna array and constant red obstruction lamp.
  cylinder(root,9.8,6.66,.5,.066,6.12,metal,7);
  for(const y of [5.3,7.1,8.7]){box(root,9.8,y,.5,3.6,.037,.035,metal);for(const dx of [-1.5,-.75,0,.75,1.5])box(root,9.8+dx,y,.5,.025,.025,1.15,metal);}
  for(const [x,z]of [[6.7,2],[11.1,-2]])cable(root,[[9.8,8.8,.5],[x,3.6,z]],.012,plain(0x53654f));
  box(root,9.8,9.78,.5,.105,.16,.105,emissive(0xd24b32,1.6));
  for(let i=0;i<17;i++){const z=6+i*2.4;conifer(root,-25-random()*6,z,6+random()*5);conifer(root,25+random()*6,z,6+random()*5);}
  for(let i=0;i<15;i++){const x=-31+i*4.3;if(x>-12&&x<-3)continue;conifer(root,x,45+random()*7,7+random()*5);}
  for(const [x,z]of [[-18,7],[-20,11],[-20,27],[19,21],[20,26],[14,7],[-15,33],[15,33]])shrub(root,x,z,1.9);
  for(let i=0;i<30;i++){
    const x=(random()>.5?1:-1)*(15+random()*6),z=8+random()*27;
    if(random()>.5)box(root,x,.03,z,.18+random()*.28,.035,.2+random()*.3,plain(0x7a8068),random()*6.28);
    else{const stone=new THREE.Mesh(new THREE.IcosahedronGeometry(.12+random()*.2,0),plain(0x56634e));stone.scale.y=.5;stone.position.set(x,.06,z);root.add(stone);}
  }
  const moon=new THREE.Mesh(new THREE.SphereGeometry(1.55,12,8),new THREE.MeshBasicMaterial({color:0x7c998d,fog:false}));moon.position.set(18,28,-25);root.add(moon);
  const vehicle=createVan(root);props.set('van',vehicle.van);
  let arrival=1,lobby=false;
  const fixedLights={headlight:100,courtyard:73,entrance:29};
  function setArrival(progress){
    arrival=THREE.MathUtils.clamp(Number.isFinite(progress)?progress:1,0,1);
    const eased=1-Math.pow(1-arrival,2);
    vehicle.van.position.set(-7,0,32-9*eased);
    vehicle.van.rotation.y=0;
    vehicle.wheels.forEach(wheel=>wheel.rotation.x=-9*eased/.425);
    const opening=THREE.MathUtils.smoothstep(arrival,.89,1);
    vehicle.door.position.set(1.12+opening*.115,0,.39+opening*1.25);
    vehicle.van.userData.arrival=arrival;vehicle.van.userData.doorOpen=opening;
  }
  function setLobby(value){lobby=Boolean(value);courtyardLight.intensity=lobby?89:fixedLights.courtyard;vehicle.headlight.intensity=lobby?83:fixedLights.headlight;}
  function update(time,dt,state={}){
    if(Number.isFinite(state.arrivalProgress)&&Math.abs(state.arrivalProgress-arrival)>.0001)setArrival(state.arrivalProgress);
    if(typeof state.lobby==='boolean'&&state.lobby!==lobby)setLobby(state.lobby);
    // All practicals are steady: no involuntary camera motion or light strobing.
    if(arrival>=1)vehicle.van.position.set(-7,0,23);
  }
  // Collapse static geometry by material. Van, wheels and doors remain editable.
  const batches=new Map();root.updateMatrixWorld(true);
  root.traverse(object=>{if(!object.isMesh||Array.isArray(object.material))return;let ancestor=object;while(ancestor&&ancestor!==root){if(ancestor===vehicle.van)return;ancestor=ancestor.parent;}const id=object.material.uuid;if(!batches.has(id))batches.set(id,[]);batches.get(id).push(object);});
  for(const meshes of batches.values()){
    if(meshes.length<3)continue;const geometries=meshes.map(mesh=>mesh.geometry.clone().applyMatrix4(mesh.matrixWorld));const merged=mergeGeometries(geometries,false);
    if(merged){const mesh=new THREE.Mesh(merged,meshes[0].material);mesh.receiveShadow=true;root.add(mesh);for(const original of meshes){original.removeFromParent();original.geometry.dispose();}}
    geometries.forEach(geometry=>geometry.dispose());
  }
  setArrival(1);
  return {root,van:vehicle.van,update,setArrival,setLobby,colliders,props,entrance:new THREE.Vector3(0,0,6),spawn:new THREE.Vector3(-5,1.65,19)};
}
