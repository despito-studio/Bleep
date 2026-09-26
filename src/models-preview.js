import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { GLTFExporter } from 'three/addons/exporters/GLTFExporter.js';
import { createCharacter, createProp, createWorld } from './world.js';
import { createExterior, createVan } from './exterior.js';

const $=id=>document.getElementById(id);
const viewer=$('viewer');
const info={
  engineer:{title:'Віктор (Victor) — Інженер та охоронець',label:'ВІКТОР / ОХОРОНЕЦЬ 001',number:'01',category:'ОПЕРАТИВНИК',detail:'1,85 М',description:'Офіцер безпеки та інженер BUREAU–9 у темно-бірюзовій формі з золотими ґудзиками та фірмовим кашкетом. Перк: потужний ліхтар (+25% дистанція) та підвищена витривалість.',target:[0,1,0],position:[2.8,1.9,4],max:8},
  operator:{title:'Вівіан (Vivian) — Операторка та кураторка',label:'ВІВІАН / ОПЕРАТОРКА 002',number:'02',category:'ОПЕРАТИВНИЦЯ',detail:'1,72 М',description:'Хронікерка телецентру з платиновим каре, золотими сережками-кільцями, перлинним кольє та сигаретою. Перк: світловий шок камери (+35% стан диктора) та стійкий розсудок.',target:[0,1,0],position:[2.8,1.9,4],max:8},
  journalist:{title:'Едґар (Edgar) — Журналіст та архівіст',label:'ЕДҐАР / ЖУРНАЛІСТ 003',number:'03',category:'ОПЕРАТИВНИК',detail:'1,80 М',description:'Згорблений спецкор у масивному твідовому піджаку з наддовгою вінтажною краваткою з орнаментом. Перк: швидкий спринт (+15%) та інтуїтивне нічне чуття касет.',target:[0,1,0],position:[2.8,1.9,4],max:8},
  dispatcher:{title:'Гейзел (Hazel) — Диспетчерка та зв’язківиця',label:'ГЕЙЗЕЛ / ДИСПЕТЧЕРКА 004',number:'04',category:'ОПЕРАТИВНИЦЯ',detail:'1,70 М',description:'Диспетчерка у вінтажній психоделічній сукні з хвилястим візерунком та крилатим чубчиком. Перк: рація кожні 1.5 сек та акустичний слух кроків ворога з 22 метрів.',target:[0,1,0],position:[2.8,1.9,4],max:8},
  monster:{title:'Той, хто в кадрі (Диктор / The Anchor)',label:'ДИКТОР / СТАЛКЕР 005',number:'05',category:'ПЕРЕСЛІДУВАЧ',detail:'2,85 М',description:'Автентична PS1-модель: пошарпаний двобортний дикторський костюм 90-х із бейджем станції 04, ламповий катодний генератор у розтрощених грудях, хребетні антени та мікрофонна щелепа.',target:[0,1.4,0],position:[3.4,2.15,5.3],max:10},
  monster_bloody:{title:'Кривавий Диктор (Фінальна трансформація)',label:'ТРАНСФОРМАЦІЯ / ФІНАЛ 006',number:'06',category:'КОШМАР',detail:'2,85 М',description:'Після пуску генератора та калібрування 96.4 МГц диктор трансформується: понівечений кривавий костюм, оголені дроти, червоне пульсуюче сяйво катода, аналогові глітч-сіпання та скажена гонитва.',target:[0,1.4,0],position:[3.4,2.15,5.3],max:10},
  van:{title:'Службовий фургон BUREAU–9',label:'ФУРГОН AX 0417 TB / ОБ’ЄКТ 007',number:'07',category:'ТРАНСПОРТ',detail:'ФУРГОН 1993',description:'Мобільна база розвідки бюро: змодельований салон із кріслами, кермом, радіостійкою, ящиками обладнання та розсувними дверима евакуації.',target:[0,1.3,0],position:[5.2,2.8,5.5],max:16},
  courtyard:{title:'Внутрішній двір та огорожа',label:'ДВІР / ЕВАКУАЦІЯ 008',number:'08',category:'ЛОКАЦІЯ',detail:'44 × 30 М',description:'Асфальтований майданчик перед станцією: фургон, трансформаторний сарай 380V, щогла антени, калюжі та периметр огорожі з воротами.',target:[0,2,20],position:[-16,14,38],max:70},
  facade:{title:'Фасад станції та щогла антени',label:'ГОЛОВНИЙ ПОРТАЛ / 009',number:'09',category:'АРХІТЕКТУРА',detail:'ВИСОТА 10 М',description:'Технічний вхід із козирком, вивіскою BLACKPINE BROADCAST 04, ліхтарем 29W, масивною антеною та червоним навігаційним маяком.',target:[0,3.2,6],position:[0,4.5,17],max:40},
  camera:{title:'Студійна камера ТК–04',label:'КАМЕРА ТК–04 / ОБ’ЄКТ 010',number:'10',category:'ОБЛАДНАННЯ',detail:'LOW POLY',description:'Важкий корпус на тринозі, окремий видошукач, об’єктив та червоний індикатор запису. Спрямовані на порожній дикторський стіл.',target:[0,1,0],position:[2.8,2,3.4],max:7},
  tape:{title:'Майстер-касета Артура Коула',label:'VHS / ДОКАЗ 011',number:'11',category:'ПРЕДМЕТ',detail:'МАСШТАБ ×4',description:'Одна з трьох архівних касет нічного ефіру. Золотаве сяйво допомагає помітити запис у темряві.',target:[0,.85,0],position:[2,2.25,2.8],max:6},
  fuse:{title:'Промисловий запобіжник 380V',label:'ЗАПОБІЖНИК / ДЕТАЛЬ 012',number:'12',category:'ЕЛЕКТРИКА',detail:'МАСШТАБ ×4',description:'Керамічний корпус, масивні латунні контакти та червоне індикаторне віконце. Потрібен для відновлення живлення у генераторній.',target:[0,.85,0],position:[1.8,1.8,2.2],max:6},
  monitor:{title:'Монітор ефіру',label:'КАНАЛ 04 / ОБ’ЄКТ 013',number:'13',category:'ОБЛАДНАННЯ',detail:'ЕЛП / 4:3',description:'Аналоговий монітор із грубим металевим корпусом, фізичними регуляторами та зеленим екраном. Очікує на відновлення трансляції.',target:[0,.7,0],position:[2,1.5,2.6],max:6},
  station:{title:'Телецентр Blackpine Broadcast',label:'ТЕХНІЧНИЙ ПОВЕРХ / МАКЕТ 014',number:'14',category:'ЛОКАЦІЯ',detail:'8 ПРИМІЩЕНЬ (2X)',description:'Архів, апаратна, генераторна, студія, вентвузол, серверна, техсклад та диспетчерська. З’єднані подовженим коридором та обхідними петлями.',target:[0,0,-24],position:[38,40,24],max:120},
  guardBooth:{title:'КПП / Будка охорони',label:'КПП ОХОРОНИ / ОБ’ЄКТ 015',number:'15',category:'АРХІТЕКТУРА',detail:'2,4 × 2,2 М',description:'Пост охорони при в’їзді: стіл вахтера, радіостанція, журнал чергувань, лампа та ключ-картка доступу.',target:[0,1.2,0],position:[3.6,2.2,4.4],max:12},
  barrier:{title:'Шлагбаум зі стійкою',label:'ШЛАГБАУМ / ОБ’ЄКТ 016',number:'16',category:'ОБЛАДНАННЯ',detail:'3,0 М',description:'Автоматичний шлагбаум КПП зі смугастою червоно-білою стрілою, противагою та опорною стійкою.',target:[0,0.8,0],position:[3.0,1.8,3.6],max:8},
  floodlightTower:{title:'Прожекторна вишка',label:'ВИШКА ОСВІТЛЕННЯ / ОБ’ЄКТ 017',number:'17',category:'СПОСТЕРЕЖЕННЯ',detail:'ВИСОТА 7,5 М',description:'Сталева гратчаста вишка з прожектором високої потужності, платформою та драбиною обслуговування.',target:[0,3.6,0],position:[6.0,4.5,7.0],max:22},
  cableSpool:{title:'Кабельна котушка',label:'КОТУШКА 380V / ОБ’ЄКТ 018',number:'18',category:'МАТЕРІАЛИ',detail:'ДІАМЕТР 1,2 М',description:'Важка дерев’яна котушка з намотаним високовольтним броньованим силовим кабелем.',target:[0,0.6,0],position:[2.2,1.6,2.6],max:6},
  abandonedCar:{title:'Покинутий транспорт ГАЗ-66',label:'ТРАНСПОРТ / ОБ’ЄКТ 019',number:'19',category:'ТЕХНІКА',detail:'4,2 × 2,1 М',description:'Покинута вантажівка станції з розбитими шинами, пробитими фарами та заіржавілим кузовом.',target:[0,1.0,0],position:[4.8,2.4,5.2],max:14},
  turnstile:{title:'Турнікети прохідної',label:'СИСТЕМА ДОСТУПУ / ОБ’ЄКТ 020',number:'20',category:'БЕЗПЕКА',detail:'2,8 × 0,8 М',description:'Турнікети-триподи з нержавіючої сталі, зчитувачем карток та світлодіодними індикаторами проходу.',target:[0,0.8,0],position:[2.8,1.8,3.5],max:8},
  archivistDesk:{title:'Стіл архіваріуса з лампою',label:'СТІЛ АРХІВУ / ОБ’ЄКТ 021',number:'21',category:'МЕБЛІ',detail:'2,3 × 1,3 М',description:'Масивний стіл з автентичною зеленою лампою банкіра, радянською друкарською машинкою та гросбухом.',target:[0,0.8,0],position:[2.6,1.8,3.4],max:8},
  anchorDesk:{title:'Дикторський стіл новин',label:'СТУДІЯ 04 / ОБ’ЄКТ 022',number:'22',category:'СТУДІЯ',detail:'3,4 × 1,5 М',description:'Вигнутий стіл новин із золотим гербом телеканалу 04, студійними мікрофонами та сценарієм нічного ефіру.',target:[0,0.8,0],position:[3.6,2.0,4.2],max:10},
  dieselGenerator:{title:'Дизель-генератор ДГ-200',label:'ЕНЕРГОБЛОК / ОБ’ЄКТ 023',number:'23',category:'ЕНЕРГЕТИКА',detail:'3,2 × 1,8 М',description:'Масивний промисловий дизель із радіатором охолодження, вентилятором, важким маховиком та вихлопною трубою.',target:[0,1.0,0],position:[4.2,2.4,4.8],max:12},
  fusePanel:{title:'Щит 380В з гніздами',label:'РОЗПОДІЛЬНИЙ ЩИТ / ОБ’ЄКТ 024',number:'24',category:'ЕЛЕКТРИКА',detail:'1,1 × 1,5 М',description:'Високовольтна шафа 380В з двома посадковими місцями під керамічні запобіжники та сигнальними неонами.',target:[0,1.5,0],position:[2.2,2.0,3.0],max:7},
  mixingConsole:{title:'Пульт прямого ефіру',label:'АПАРАТНА 02 / ОБ’ЄКТ 025',number:'25',category:'ЗВУК / ВІДЕО',detail:'2,6 × 1,4 М',description:'Багатоканальний мікшерний пульт із фейдерами, калібрувальними ручками 96.4 MHz та підсвіченими VU-метрами.',target:[0,0.8,0],position:[3.2,1.9,4.0],max:9},
  serverRack:{title:'Серверна стійка 90-х',label:'ОБЧИСЛЮВАЛЬНИЙ ЦЕНТР / ОБ’ЄКТ 026',number:'26',category:'ОБЛАДНАННЯ',detail:'ВИСОТА 2,5 М',description:'Стійка ЕОМ 1990-х із миготливими зеленими та бурштиновими світлодіодами і блоками живлення.',target:[0,1.25,0],position:[2.4,2.0,3.6],max:8},
  cardCatalog:{title:'Каталожна шафа архіву',label:'КАРТОТЕКА / ОБ’ЄКТ 027',number:'27',category:'АРХІВ',detail:'1,6 × 1,8 М',description:'16-шухлядна дерев’яна каталожна шафа з латунними ручками та маркованими картками обліку записів.',target:[0,1.0,0],position:[2.6,1.8,3.6],max:8},
  airDuct:{title:'Вентиляційний короб',label:'ВЕНТВУЗОЛ / ОБ’ЄКТ 028',number:'28',category:'ІНЖЕНЕРІЯ',detail:'ВИСОТА 3,5 М',description:'Вертикальний оцинкований короб вентиляції з фланцевими з’єднаннями та ревізійним люком.',target:[0,1.75,0],position:[2.6,2.4,3.8],max:10},
  monitorWall:{title:'Стійка відеоконтролю',label:'ДИСПЕТЧЕРСЬКА / ОБ’ЄКТ 029',number:'29',category:'МОНІТОРИНГ',detail:'2,2 × 2,2 М',description:'Матриця 3×3 з дев’яти ЕЛП-моніторів для контролю камер усіх приміщень телецентру Blackpine.',target:[0,1.2,0],position:[3.2,2.2,4.2],max:10},
  fireBoard:{title:'Пожежний щиток',label:'ПОЖЕЖНА БЕЗПЕКА / ОБ’ЄКТ 030',number:'30',category:'БЕЗПЕКА',detail:'1,3 × 1,1 М',description:'Червона настінна шафа з пожежним рукавом, брандспойтом, сокирою та маркуванням ПК-04.',target:[0,1.5,0],position:[2.2,1.8,2.8],max:7},
};
let renderer,scene,camera,controls,world,stage,current,lights,currentExterior;
let chosen='engineer',operatorColor='#4e7048';
const reducedMotion=window.matchMedia('(prefers-reduced-motion: reduce)').matches;
let walking=!reducedMotion,rotating=!reducedMotion;
let lastTime=performance.now(),time=0;

try{
  renderer=new THREE.WebGLRenderer({canvas:$('gallery-canvas'),antialias:false,powerPreference:'high-performance'});
  renderer.setPixelRatio(1);renderer.outputColorSpace=THREE.SRGBColorSpace;renderer.toneMapping=THREE.ACESFilmicToneMapping;renderer.toneMappingExposure=1.55;
  scene=new THREE.Scene();
  camera=new THREE.PerspectiveCamera(42,1,.025,160);
  controls=new OrbitControls(camera,renderer.domElement);controls.enableDamping=!reducedMotion;controls.dampingFactor=.08;controls.autoRotate=rotating;controls.autoRotateSpeed=.6;controls.minDistance=.8;controls.maxPolarAngle=Math.PI*.49;controls.enablePan=true;
  lights=new THREE.Group();scene.add(lights);
  const sky=new THREE.HemisphereLight(0xc4d5c1,0x403623,2.4);lights.add(sky);
  const key=new THREE.DirectionalLight(0xffdeaa,4.3);key.position.set(3,6,5);lights.add(key);
  const rim=new THREE.DirectionalLight(0x6cad91,3.5);rim.position.set(-4,3,-4);lights.add(rim);
  const fill=new THREE.DirectionalLight(0x9cae9b,.8);fill.position.set(-3,1,4);lights.add(fill);
  stage=new THREE.Group();scene.add(stage);
  const platform=new THREE.Mesh(new THREE.CylinderGeometry(1.33,1.43,.11,32),new THREE.MeshStandardMaterial({color:0x222d21,roughness:.9,metalness:.15}));platform.position.y=-.065;stage.add(platform);
  const rings=new THREE.Mesh(new THREE.TorusGeometry(1.3,.006,3,64),new THREE.MeshBasicMaterial({color:0x746c47}));rings.rotation.x=Math.PI/2;rings.position.y=-.005;stage.add(rings);
  const grid=new THREE.GridHelper(12,24,0x3f5036,0x202c20);grid.position.y=-.125;stage.add(grid);
  const line=new THREE.Mesh(new THREE.BoxGeometry(.035,.007,1.1),new THREE.MeshBasicMaterial({color:0x817349}));line.position.set(0,.002,0);stage.add(line);
  world=createWorld(scene);world.root.visible=false;world.setState({tapes:[],power:true,broadcast:false,flicker:false,reducedMotion:true});
  choose('engineer');
  new ResizeObserver(resize).observe(viewer);resize();
  requestAnimationFrame(tick);
}catch(error){
  console.error(error);const message=document.createElement('p');message.className='fallback';message.textContent='Не вдалося відкрити 3D-архів. Спробуйте Chrome або Edge з увімкненим апаратним прискоренням. '+error.message;viewer.append(message);
}

function resize(){if(!renderer)return;const width=viewer.clientWidth,height=viewer.clientHeight;renderer.setSize(Math.floor(width*.8),Math.floor(height*.8),false);camera.aspect=width/height;camera.updateProjectionMatrix();}
function resetView(){const item=info[chosen];controls.target.fromArray(item.target);camera.position.fromArray(item.position);controls.maxDistance=item.max;controls.minDistance=chosen==='station'?8:chosen==='courtyard'||chosen==='facade'?3:.8;controls.maxPolarAngle=chosen==='station'?Math.PI*.43:Math.PI*.49;controls.update();}
function updateButtons(){
  $('animation-toggle').setAttribute('aria-pressed',String(walking));$('animation-toggle').textContent=walking?'▷ ХОДА':'Ⅱ СПОКІЙ';
  $('rotation-toggle').setAttribute('aria-pressed',String(rotating));controls.autoRotate=rotating;
  $('animation-toggle').hidden=!['engineer','operator','journalist','dispatcher','monster','monster_bloody'].includes(chosen);
  $('operator-colors').hidden=true;
}
function choose(key){
  chosen=key;const item=info[key]||info.engineer;
  if(current){scene.remove(current);current.traverse(o=>{if(o.isMesh)o.geometry?.dispose?.();});current=null;}
  if(currentExterior){scene.remove(currentExterior.root);currentExterior=null;}
  const station=key==='station';
  const isExterior=key==='courtyard'||key==='facade';
  world.root.visible=station;stage.visible=!station&&!isExterior;
  lights.children[0].intensity=station||isExterior?2.2:2.4;
  lights.children[1].intensity=station||isExterior?2:4.3;
  renderer.clippingPlanes=station?[new THREE.Plane(new THREE.Vector3(0,-1,0),2.45)]:[];
  if(isExterior){
    currentExterior=createExterior(scene);
    currentExterior.setArrival(1);
  }else if(!station){
    if(key==='engineer')current=createCharacter('#1e423f',false,false,'engineer');
    else if(key==='operator')current=createCharacter('#4a1824',false,false,'operator');
    else if(key==='journalist')current=createCharacter('#242726',false,false,'journalist');
    else if(key==='dispatcher')current=createCharacter('#d95328',false,false,'dispatcher');
    else if(key==='monster')current=createCharacter('#272c24',true,false);
    else if(key==='monster_bloody')current=createCharacter('#4a1815',true,true);
    else if(key==='van'){
      current=new THREE.Group();
      const v=createVan(current);
      v.van.position.set(0,0,0);
      v.door.position.set(1.235,0,1.64); // sliding passenger door open
    }
    else {
      current=createProp(key);
      if(key==='tape'){current.scale.setScalar(4);current.position.y=.85;current.rotation.z=-.08;}
      else if(key==='fuse'){current.scale.setScalar(4);current.position.y=.85;}
      else if(key==='monitor'){current.scale.setScalar(1.5);}
    }
    if(current)scene.add(current);
  }
  $('model-title').textContent=item.title;$('model-description').textContent=item.description;$('model-category').textContent=item.category;$('model-detail').textContent=item.detail;
  $('model-number').textContent=item.number;$('view-label').textContent=item.label;$('gallery-canvas').setAttribute('aria-label',`3D-модель: ${item.title}`);
  $('view-subtitle').textContent=station?'ВИД ЗІ ЗНЯТОЮ СТЕЛЕЮ · МАСШТАБ У МЕТРАХ':isExterior?'BLACKPINE BROADCAST · ЕКСТЕР’ЄР':'BLACKPINE BROADCAST · СТАНЦІЯ 04 · 1996';
  document.querySelectorAll('[data-model]').forEach(button=>button.setAttribute('aria-pressed',String(button.dataset.model===key)));
  resetView();updateButtons();
}
function tick(now){
  const dt=Math.min((now-lastTime)/1000,.05);lastTime=now;time+=dt;
  if(current?.userData.animate)current.userData.animate(reducedMotion?0:time,walking);
  if(chosen==='station')world.update(time,dt);
  if(currentExterior)currentExterior.update(time,dt,{arrivalProgress:1});
  controls.update(dt);renderer.render(scene,camera);requestAnimationFrame(tick);
}
document.querySelectorAll('[data-model]').forEach(button=>button.addEventListener('click',()=>choose(button.dataset.model)));
document.querySelectorAll('[data-color]').forEach(button=>button.addEventListener('click',()=>{operatorColor=button.dataset.color;choose('operator');document.querySelectorAll('[data-color]').forEach(b=>b.setAttribute('aria-pressed',String(b===button)));}));
$('animation-toggle').addEventListener('click',()=>{walking=!walking;updateButtons();});
$('rotation-toggle').addEventListener('click',()=>{rotating=!rotating;updateButtons();});
$('reset-view').addEventListener('click',resetView);

export async function exportModel(modelKey, download = true) {
  const isStation = modelKey === 'station';
  const isExt = modelKey === 'courtyard' || modelKey === 'facade';
  const target = current || (isStation ? world.root : (currentExterior ? currentExterior.root : null));
  if (!target) return null;

  const exporter = new GLTFExporter();
  return new Promise((resolve, reject) => {
    exporter.parse(
      target,
      async (gltf) => {
        try {
          const blob = new Blob([gltf], { type: 'model/gltf-binary' });
          // Save directly to server's unity_assets directory
          try {
            await fetch(`/api/export-glb?name=${modelKey}`, {
              method: 'POST',
              headers: { 'Content-Type': 'application/octet-stream' },
              body: blob
            });
          } catch (e) {
            console.warn('[EXPORT] Server save failed:', e);
          }

          if (download) {
            const a = document.createElement('a');
            a.href = URL.createObjectURL(blob);
            a.download = `${modelKey}.glb`;
            a.click();
            URL.revokeObjectURL(a.href);
          }
          resolve(gltf);
        } catch (err) {
          reject(err);
        }
      },
      reject,
      { binary: true }
    );
  });
}

export async function exportAllUnityAssets() {
  const keys = Object.keys(info);
  const statusEl = $('view-hint');
  const btn = $('export-all-glb');
  if (btn) btn.disabled = true;
  for (let i = 0; i < keys.length; i++) {
    const k = keys[i];
    if (btn) btn.textContent = `⏳ [${i + 1}/${keys.length}] ${k}`;
    if (statusEl) statusEl.textContent = `ЕКСПОРТ ДЛЯ UNITY: ${k} (${i + 1}/${keys.length})...`;
    choose(k);
    await new Promise(r => setTimeout(r, 150));
    await exportModel(k, false);
  }
  if (btn) {
    btn.textContent = '✓ ВСІ 30 ЗБЕРЕЖЕНО!';
    setTimeout(() => { btn.textContent = '📦 ВСІ 30 МОДЕЛЕЙ (UNITY)'; btn.disabled = false; }, 3000);
  }
  if (statusEl) statusEl.textContent = '✓ ВСІ 30 МОДЕЛЕЙ ЗБЕРЕЖЕНО В ПАПКУ unity_assets/';
  return { ok: true, count: keys.length };
}

window.exportAllUnityAssets = exportAllUnityAssets;
window.exportModel = exportModel;

$('export-single-glb')?.addEventListener('click', async () => {
  const btn = $('export-single-glb');
  const prevText = btn.textContent;
  btn.textContent = '⏳ ЕКСПОРТ...';
  btn.disabled = true;
  try {
    await exportModel(chosen, true);
    btn.textContent = '✓ ЗБЕРЕЖЕНО!';
    setTimeout(() => { btn.textContent = prevText; btn.disabled = false; }, 1800);
  } catch (e) {
    console.error(e);
    btn.textContent = 'ПОМИЛКА';
    setTimeout(() => { btn.textContent = prevText; btn.disabled = false; }, 2000);
  }
});

$('export-all-glb')?.addEventListener('click', () => exportAllUnityAssets());
