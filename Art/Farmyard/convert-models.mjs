// Offline, dependency-free glTF skin baker for the two CC0 farmyard animals.
// Source GLBs are retained alongside this script; runtime needs no glTF package.
import fs from 'node:fs';
import path from 'node:path';
const source = path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1'));
const out = path.resolve(source, '../../Assets/Resources/Farmyard');
fs.mkdirSync(out, {recursive:true});
const identity = () => [1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1];
function mul(a,b) { const o=new Array(16).fill(0); for(let c=0;c<4;c++)for(let r=0;r<4;r++)for(let k=0;k<4;k++)o[c*4+r]+=a[k*4+r]*b[c*4+k]; return o; }
function trs(t=[0,0,0],q=[0,0,0,1],s=[1,1,1]) {
 const [x,y,z,w]=q,x2=x+x,y2=y+y,z2=z+z,xx=x*x2,xy=x*y2,xz=x*z2,yy=y*y2,yz=y*z2,zz=z*z2,wx=w*x2,wy=w*y2,wz=w*z2;
 return [(1-yy-zz)*s[0],(xy+wz)*s[0],(xz-wy)*s[0],0,(xy-wz)*s[1],(1-xx-zz)*s[1],(yz+wx)*s[1],0,(xz+wy)*s[2],(yz-wx)*s[2],(1-xx-yy)*s[2],0,...t,1];
}
function transform(m,v,w=1){return [m[0]*v[0]+m[4]*v[1]+m[8]*v[2]+m[12]*w,m[1]*v[0]+m[5]*v[1]+m[9]*v[2]+m[13]*w,m[2]*v[0]+m[6]*v[1]+m[10]*v[2]+m[14]*w];}
function norm(v){const n=Math.hypot(...v)||1;return v.map(x=>x/n);}
function load(name,height) {
 const b=fs.readFileSync(path.join(source,name+'.glb')); if(b.readUInt32LE(0)!==0x46546c67)throw Error('GLB header');
 const jsonSize=b.readUInt32LE(12),g=JSON.parse(b.toString('utf8',20,20+jsonSize)),binStart=20+jsonSize+8;
 const dims={SCALAR:1,VEC2:2,VEC3:3,VEC4:4,MAT4:16},sizes={5120:1,5121:1,5122:2,5123:2,5125:4,5126:4};
 function access(i){const a=g.accessors[i],v=g.bufferViews[a.bufferView],d=dims[a.type],step=v.byteStride||d*sizes[a.componentType],start=binStart+(v.byteOffset||0)+(a.byteOffset||0),r=[];for(let k=0;k<a.count;k++)for(let c=0;c<d;c++){const p=start+k*step+c*sizes[a.componentType]; let x=a.componentType===5126?b.readFloatLE(p):a.componentType===5125?b.readUInt32LE(p):a.componentType===5123?b.readUInt16LE(p):a.componentType===5122?b.readInt16LE(p):a.componentType===5121?b.readUInt8(p):b.readInt8(p);if(a.normalized&&a.componentType!==5126)x/=a.componentType===5123?65535:255;r.push(x);}return r;}
 const nodeIndex=g.nodes.findIndex(n=>n.mesh!==undefined),meshNode=g.nodes[nodeIndex],p=g.meshes[meshNode.mesh].primitives[0];
 if(p.mode!==undefined&&p.mode!==4)throw Error('Triangles required');
 const a=p.attributes,pos=access(a.POSITION),normal=access(a.NORMAL),uv=access(a.TEXCOORD_0),joint=access(a.JOINTS_0),weight=access(a.WEIGHTS_0),indices=access(p.indices),skin=g.skins[meshNode.skin],bind=access(skin.inverseBindMatrices),n=pos.length/3;
 const parents=g.nodes.map(()=>-1);g.nodes.forEach((node,i)=>(node.children||[]).forEach(c=>parents[c]=i));
 function frame(anim,time) {
  const local=g.nodes.map(node=>({t:[...(node.translation||[0,0,0])],q:[...(node.rotation||[0,0,0,1])],s:[...(node.scale||[1,1,1])]}));
  for(const channel of anim.channels){const samp=anim.samplers[channel.sampler],times=access(samp.input),values=access(samp.output),d=channel.target.path==='rotation'?4:3;let k=0;while(k+1<times.length&&times[k+1]<time)k++;const k2=Math.min(k+1,times.length-1),f=samp.interpolation==='STEP'?0:Math.max(0,Math.min(1,(time-times[k])/(times[k2]-times[k]||1)));let v=values.slice(k*d,(k+1)*d),v2=values.slice(k2*d,(k2+1)*d);if(d===4&&v.reduce((z,x,i)=>z+x*v2[i],0)<0)v2=v2.map(x=>-x);v=v.map((x,i)=>x+(v2[i]-x)*f);if(d===4)v=norm(v);local[channel.target.node][channel.target.path==='rotation'?'q':channel.target.path==='scale'?'s':'t']=v;}
  const world=[];function matrix(i){if(world[i])return world[i];const tr=local[i],m=g.nodes[i].matrix||trs(tr.t,tr.q,tr.s);return world[i]=parents[i]<0?m:mul(matrix(parents[i]),m);}
  const bones=skin.joints.map((j,i)=>mul(matrix(j),bind.slice(i*16,i*16+16))),vertices=[],normals=[];
  for(let i=0;i<n;i++){const v=pos.slice(i*3,i*3+3),vn=normal.slice(i*3,i*3+3),o=[0,0,0],on=[0,0,0];for(let j=0;j<4;j++){const w=weight[i*4+j];if(!w)continue;const m=bones[joint[i*4+j]],t=transform(m,v),tn=transform(m,vn,0);for(let c=0;c<3;c++){o[c]+=t[c]*w;on[c]+=tn[c]*w;}}vertices.push(...o);normals.push(...norm(on));}return {vertices,normals};
 }
 const animations=[];for(const anim of g.animations.filter(a=>['idle','walk','attack'].includes(a.name))){const frames=[];for(let i=0;i<30;i++)frames.push(frame(anim,i/24));animations.push({name:anim.name,frames});}
 const base=animations[0].frames[0].vertices;let low=[Infinity,Infinity,Infinity],high=[-Infinity,-Infinity,-Infinity];for(let i=0;i<base.length;i++) {low[i%3]=Math.min(low[i%3],base[i]);high[i%3]=Math.max(high[i%3],base[i]);}
 const scale=height/(high[1]-low[1]);const offset=[(low[0]+high[0])*.5,low[1],(low[2]+high[2])*.5];
 // Reflect X and reverse winding at the glTF-to-Unity boundary. Texture V is flipped.
 for(const animation of animations)for(const fr of animation.frames)for(let i=0;i<fr.vertices.length;i++){fr.vertices[i]=(fr.vertices[i]-offset[i%3])*scale*(i%3===0?-1:1);if(i%3===0)fr.normals[i]=-fr.normals[i];}
 for(let i=0;i<indices.length;i+=3)[indices[i+1],indices[i+2]]=[indices[i+2],indices[i+1]];
 for(let i=1;i<uv.length;i+=2)uv[i]=1-uv[i];
 const bytes=[];function int(i){const z=Buffer.alloc(4);z.writeInt32LE(i);bytes.push(z);}function fl(x){const z=Buffer.alloc(4);z.writeFloatLE(x);bytes.push(z);}function floats(arr){const z=Buffer.alloc(arr.length*4);arr.forEach((v,i)=>z.writeFloatLE(v,i*4));bytes.push(z);}function str(s){const z=Buffer.from(s,'utf8');int(z.length);bytes.push(z);}
 int(0x44414631);int(n);int(indices.length);indices.forEach(int);floats(uv);int(animations.length);for(const animation of animations){str(animation.name);int(animation.frames.length);fl(24);for(const fr of animation.frames){floats(fr.vertices);floats(fr.normals);}}
 fs.writeFileSync(path.join(out,name.toLowerCase()+'.bytes'),Buffer.concat(bytes));
 const image=g.images[0],v=g.bufferViews[image.bufferView];fs.writeFileSync(path.join(out,name.toLowerCase()+'.png'),b.subarray(binStart+(v.byteOffset||0),binStart+(v.byteOffset||0)+v.byteLength));
 // Small textual preview fixture for offline geometric QA, outside runtime Resources.
 fs.writeFileSync(path.join(source,name+'-preview.json'),JSON.stringify({vertices:animations[0].frames[0].vertices,indices,uv}));
 console.log(name,{vertices:n,triangles:indices.length/3,height,animations:animations.map(a=>a.name),bounds:high.map((v,i)=>(v-low[i])*scale)});
}
// Adult goat: clearly larger than the 47cm chicken, hooves grounded at Y=0.
load('Goat',1.22);load('Rooster',.47);
