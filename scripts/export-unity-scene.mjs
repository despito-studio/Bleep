import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import {createRequire} from 'node:module';
import {createServer} from 'vite';

const root=path.resolve('.');
const runtime=process.env.CODEX_NODE_MODULES||path.join(os.homedir(),'.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules');
const require=createRequire(import.meta.url);
let chromium;
try { ({chromium}=require('playwright')); }
catch { ({chromium}=createRequire(path.join(runtime,'playwright/package.json'))('playwright')); }
const server=await createServer({root,server:{host:'127.0.0.1',port:5178,strictPort:true}});
await server.listen();
const browser=await chromium.launch({headless:true,channel:'chrome',args:['--disable-dev-shm-usage']});
try {
  const page=await browser.newPage({viewport:{width:1366,height:768}});
  await page.goto('http://127.0.0.1:5178/builder.html');
  const result=await page.evaluate(async()=>{
    const module=await import('/scripts/unity-scene-browser.js');
    return module.exportUnityScene();
  });
  const target=path.join(root,'DeadAirUnityProject/Assets/SourceExport');
  fs.mkdirSync(path.join(target,'Textures'),{recursive:true});
  for(const texture of result.textures){fs.writeFileSync(path.join(target,'Textures',`texture_${texture.id}.png`),Buffer.from(texture.png,'base64'));delete texture.png;}
  fs.writeFileSync(path.join(target,'scene.json'),JSON.stringify(result));
  console.log(JSON.stringify({roots:result.roots.length,props:result.sourceProps,geometries:result.geometries.length,materials:result.materials.length,textures:result.textures.length}));
  const screenshots=path.join(root,'output/unity-fidelity');fs.mkdirSync(screenshots,{recursive:true});
  for(const [name,p,t] of [['courtyard',[-5,1.65,19],[0,1.7,6]],['corridor',[0,1.65,1],[0,1.65,-18]],['archive',[-5,1.65,-1],[-9,1,-4]]]){
    await page.evaluate(async({p,t})=>(await import('/scripts/unity-scene-browser.js')).referenceView(p,t),{p,t});
    await page.screenshot({path:path.join(screenshots,`web-${name}.png`)});
  }
} finally {await browser.close();await server.close();}
