import * as THREE from 'three';
import {createWorld, createCharacter, createProp} from '../src/world.js';
import {createExterior} from '../src/exterior.js';
import map from '../shared/custom-map.json';

// Run in a real browser: CanvasTexture pixels cannot be recovered from a mocked canvas.
export async function exportUnityScene() {
  const scene = new THREE.Scene();
  const world = createWorld(scene, map.props);
  const exterior = createExterior(scene);
  exterior.setArrival(1);
  for (const [id, object] of world.interactables) object.name = `interactive:${id}`;
  const e = world.exportData;
  for (const [id, door] of e.doorObjects) {
    door.pivot.name = `door:${id}`;
    door.led.name = `door-led:${id}`;
    door.ledLight.name = `door-light:${id}`;
  }
  e.lamps.forEach((lamp, i) => lamp.light.name = `lamp:${i}`);
  e.fans.forEach((fan, i) => fan.name = `fan:${i}`);
  e.monitors.forEach((monitor, i) => monitor.screen.name = `monitor:${i}`);
  for (const name of ['powerLever','powerLamp','broadcastBulb','exitLamp']) e[name].name = `state:${name}`;
  const assets = [
    ['station', world.root], ['exterior', exterior.root],
    ...[['engineer','#1e423f'],['operator','#4a1824'],['journalist','#242726'],['dispatcher','#d95328']]
      .map(([id,color]) => [`character_${id}`,createCharacter(color,false,false,id)]),
    ['monster',createCharacter('#272c24',true,false)],
    ['monster_bloody',createCharacter('#4a1815',true,true)],
    ['camera',createProp('camera')]
  ];
  const geometries=[], materials=[], textures=[], roots=[];
  const geomIds=new Map(), matIds=new Map(), textureIds=new Map();
  const vec=v=>v.toArray();
  function textureId(t) {
    if (!t) return -1;
    if (textureIds.has(t.uuid)) return textureIds.get(t.uuid);
    const id=textures.length;textureIds.set(t.uuid,id);
    const canvas=t.image;
    if (!canvas?.toDataURL) throw new Error(`Texture ${t.uuid} is not backed by a canvas`);
    textures.push({id, png:canvas.toDataURL('image/png').split(',')[1], repeat:vec(t.repeat),offset:vec(t.offset),
      wrap:t.wrapS===THREE.RepeatWrapping,nearest:t.magFilter===THREE.NearestFilter});
    return id;
  }
  function materialId(m) {
    if (matIds.has(m.uuid)) return matIds.get(m.uuid);
    const id=materials.length;matIds.set(m.uuid,id);
    materials.push({id,color:'#'+m.color.getHexString(),emissive:'#'+(m.emissive?.getHexString()||'000000'),
      emission:m.emissiveIntensity||0,roughness:m.roughness??1,metalness:m.metalness||0,
      opacity:m.opacity,transparent:m.transparent,alphaTest:m.alphaTest||0,doubleSided:m.side===THREE.DoubleSide,
      unlit:!!m.isMeshBasicMaterial,map:textureId(m.map),emissiveMap:textureId(m.emissiveMap),
      fog:m.fog!==false,depthWrite:m.depthWrite,additive:m.blending===THREE.AdditiveBlending});
    return id;
  }
  function geometryId(g) {
    if (geomIds.has(g.uuid)) return geomIds.get(g.uuid);
    const id=geometries.length;geomIds.set(g.uuid,id);
    const flat=(a,n)=>{if(!a)return [];const out=[];for(let i=0;i<a.count;i++)for(let j=0;j<n;j++)out.push(a.getComponent(i,j));return out;};
    geometries.push({id,positions:flat(g.attributes.position,3),normals:flat(g.attributes.normal,3),uv:flat(g.attributes.uv,2),
      indices:g.index?Array.from(g.index.array):Array.from({length:g.attributes.position.count},(_,i)=>i),
      groups:g.groups.map(a=>({start:a.start,count:a.count,materialIndex:a.materialIndex}))});
    return id;
  }
  function node(o) {
    const n={name:o.name||o.type,p:vec(o.position),q:vec(o.quaternion),s:vec(o.scale),visible:o.visible,geometry:-1,materials:[],children:[]};
    if(o.isMesh){n.geometry=geometryId(o.geometry);n.materials=(Array.isArray(o.material)?o.material:[o.material]).map(materialId);}
    if(o.isLight){
      n.light={type:o.type,color:'#'+o.color.getHexString(),intensity:o.intensity,range:o.distance||0,angle:o.angle||0,penumbra:o.penumbra||0};
      if(o.target){o.target.updateMatrixWorld(true);const target=new THREE.Vector3();o.target.getWorldPosition(target);n.light.target=vec(target);}
    }
    n.children=o.children.map(node);
    return n;
  }
  for(const [name,root] of assets){root.updateMatrixWorld(true);roots.push({name,node:node(root)});}
  return {version:2,sourceProps:map.props.length,geometries,materials,textures,roots};
}

export function referenceView(position,lookAt) {
  const scene=new THREE.Scene();scene.background=new THREE.Color('#0a100a');scene.fog=new THREE.FogExp2('#0a100a',.032);
  createWorld(scene,map.props);createExterior(scene);scene.add(new THREE.HemisphereLight(0x939f85,0x292115,.34));
  const renderer=new THREE.WebGLRenderer({antialias:false,preserveDrawingBuffer:true});renderer.setSize(1366,768);
  renderer.toneMapping=THREE.ACESFilmicToneMapping;renderer.toneMappingExposure=1.3;
  document.body.style.margin='0';document.body.replaceChildren(renderer.domElement);
  const camera=new THREE.PerspectiveCamera(68,1366/768,.08,85);camera.position.fromArray(position);camera.lookAt(...lookAt);
  const lamp=new THREE.SpotLight(0xffe5b2,26,14,.42,.7,1.3);lamp.position.copy(camera.position);lamp.target.position.fromArray(lookAt);scene.add(lamp, lamp.target);
  renderer.render(scene,camera);
}
