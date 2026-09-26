import * as THREE from 'three';
import { createCharacter } from './world.js';
import { getClassData } from '../shared/classes.js';

export function createLobby(scene) {
  const group = new THREE.Group();
  group.name = 'Outdoor crew lineup';
  scene.add(group);
  const models = new Map();
  const slots = [-2.4, -.3, 1.8, 3.9];
  let signature='';
  function nameplate(name, host) {
    const c=document.createElement('canvas');c.width=384;c.height=80;
    const ctx=c.getContext('2d');ctx.textAlign='center';ctx.font='24px monospace';
    ctx.fillStyle=host?'#dac797':'#d6d6c7';ctx.fillText(name.slice(0,20),192,34);
    ctx.fillStyle='#9caa97';ctx.font='15px monospace';ctx.fillText(host?'HOST':'CONNECTED',192,60);
    const tx=new THREE.CanvasTexture(c);
    const sprite=new THREE.Sprite(new THREE.SpriteMaterial({map:tx,depthTest:false,transparent:true}));
    sprite.scale.set(1.6,.34,1);sprite.position.y=2.75;return sprite;
  }
  return {
    group,
    sync(players=[],hostId=null,selectedClass='engineer',isMenu=false){
      const spec=isMenu?[{id:'preview',characterClass:selectedClass}]:players;
      const next=JSON.stringify([spec.map(p=>[p.id,p.name,p.characterClass]),hostId,isMenu]);
      if(signature===next)return;signature=next;
      for(const m of models.values()){
        m.userData.plate?.material.map.dispose();m.userData.plate?.material.dispose();
        group.remove(m);
      }
      models.clear();
      spec.forEach((p,i)=>{
        const data=getClassData(p.characterClass);const m=createCharacter(data.color,false,false,data.id);
        m.position.set(isMenu?3.4:slots[i],0,isMenu?21:20);m.rotation.y=isMenu?-.16:(i-1.5)*-.045;
        if(!isMenu){const plate=nameplate(p.name||data.name,p.id===hostId);m.add(plate);m.userData.plate=plate;}
        group.add(m);models.set(p.id,m);
      });
    },
    update(time,visible){group.visible=visible;if(visible)for(const [i,m]of [...models.values()].entries())m.userData.animate?.(time+i*2.1,false);},
  };
}
