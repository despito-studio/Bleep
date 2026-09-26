import * as THREE from 'three';
import { io } from 'socket.io-client';
import { createWorld, createCharacter } from './world.js';
import { createTapeEffect } from './post.js';
import { HorrorAudio } from './audio.js';
import { objects, lockers, isWalkable, roomAt, spawn, updateObstacles } from '../shared/world-data.js';
import { CHARACTER_CLASSES, CLASS_KEYS, DEFAULT_CLASS, getClassData } from '../shared/classes.js';
import './style.css';
import './minimal.css';
import { interfaceMarkup } from './interface.js';
import { createExterior } from './exterior.js';
import { createLobby } from './lobby.js';
import { extraction } from '../shared/world-data.js';

export function gameLog(category, action, details = null) {
  const time = new Date().toLocaleTimeString('uk-UA', { hour12: false });
  const badgeStyle = 'color: #ffd05b; font-weight: bold; background: #131b14; padding: 2px 6px; border: 1px solid #3c5239; border-radius: 3px;';
  const textStyle = 'color: #9cd48b; font-weight: 500;';
  const prefix = `%c[DEAD AIR // ${category.toUpperCase()}]%c [${time}] ${action}`;
  if (details !== null && details !== undefined) {
    console.log(prefix, badgeStyle, textStyle, details);
  } else {
    console.log(prefix, badgeStyle, textStyle);
  }
}

const icons = {
  tape: '<rect x="2" y="5" width="20" height="14" rx="2"/><path d="M6 15h12M6 19l2-4m10 4-2-4"/><circle cx="8" cy="10" r="2"/><circle cx="16" cy="10" r="2"/>',
  play: '<path d="m8 5 11 7-11 7Z"/>',
  arrow: '<path d="M4 12h16m-6-6 6 6-6 6"/>',
  people: '<circle cx="9" cy="8" r="3"/><path d="M3 20v-2a6 6 0 0 1 12 0v2m0-15a3 3 0 0 1 0 6m3 3a5 5 0 0 1 3 4v2"/>',
  sound: '<path d="M4 9h4l5-4v14l-5-4H4Zm12-2a8 8 0 0 1 0 10m3-13a12 12 0 0 1 0 16"/>',
  mute: '<path d="M4 9h4l5-4v14l-5-4H4Zm13 0 5 6m0-6-5 6"/>',
  full: '<path d="M8 3H3v5m13-5h5v5M3 16v5h5m13-5v5h-5"/>',
  headphones: '<path d="M4 14v-3a8 8 0 0 1 16 0v3"/><rect x="2" y="12" width="5" height="9" rx="2"/><rect x="17" y="12" width="5" height="9" rx="2"/>',
  close: '<path d="m6 6 12 12M6 18 18 6"/>',
  copy: '<rect x="8" y="8" width="12" height="13" rx="1"/><path d="M15 8V3H3v13h5"/>',
  radio: '<path d="M12 2v4m0 0a6 6 0 0 0-6 6v7a2 2 0 0 0 2 2h8a2 2 0 0 0 2-2v-7a6 6 0 0 0-6-6Zm-3 10h6m-5 4h4"/>',
};
const icon = name => `<svg viewBox="0 0 24 24" aria-hidden="true">${icons[name] || icons.tape}</svg>`;
const $ = id => document.getElementById(id);
const escape = value => String(value).replace(/[&<>"']/g, c => ({ '&':'&amp;', '<':'&lt;', '>':'&gt;', '"':'&quot;', "'":'&#39;' }[c]));
let selectedClass = localStorage.getItem('dead-air-class') || DEFAULT_CLASS;
if (!CHARACTER_CLASSES[selectedClass]) selectedClass = DEFAULT_CLASS;

const app = $('app');
app.innerHTML = interfaceMarkup(CHARACTER_CLASSES, selectedClass, icon);

let settings = { volume: 0.5, quality: 360, grain: true, flicker: true, sensitivity: 1 };
try { settings = { ...settings, ...JSON.parse(localStorage.getItem('dead-air-settings') || '{}') }; } catch {}
const audio = new HorrorAudio();
audio.setVolume(settings.volume);
const originalAudioPlay = audio.play.bind(audio);
audio.play = (kind, options = {}) => {
  gameLog('AUDIO', `Звуковий ефект: "${kind}"`);
  return originalAudioPlay(kind, options);
};
let audioOn = false;
let state = null, myId = null, playing = false, mode = 'menu', returnMode = 'menu';
let yaw = 0, pitch = 0, flashlightOn = true, cameraOn = false, stamina = 100, moving = false, sprinting = false;
let localPos = new THREE.Vector3(spawn.x, 1.65, spawn.z);
let currentBob = 0, footBob = 0;
let lastSend = 0, lastHud = 0, lastScare = 0, threat = 0, lastFrame = performance.now(), elapsed = 0;
let currentInteract = null, subtitleUntil = 0, toastUntil = 0, lobbySignature = '';
let handlingAction = false, prevTapes = [], lastRadioTime = 0, nextScreamerTime = 0;
let lastRoom = '';
let locationCardTimer = null;
const keys = new Set();
const socket = io({ autoConnect: true, reconnectionAttempts: 10, reconnectionDelay: 1000 });
const remotePlayers = new Map();

let exterior, lobby, arrivalStarted=0, arrivalProgress=1;
let frameErrorShown=false;
let customProps = null;
try {
  const mapRes = await fetch('/api/map').then(r => r.json());
  if (mapRes?.ok && Array.isArray(mapRes.props)) {
    customProps = mapRes.props;
    updateObstacles(customProps);
    gameLog('WORLD', `Синхронізовано фізичні перешкоди мапи: ${customProps.length} об'єктів`);
  }
} catch (e) {
  console.warn('Could not load custom map, using defaults:', e);
}

const customLockers = (customProps || []).filter(p => p.type === 'locker').map(p => ({
  id: p.id,
  x: p.x,
  y: 1.1,
  z: p.z,
  ry: p.rotation || 0,
  exitX: p.exitX ?? Number((p.x + Math.sin(p.rotation || 0) * 1.3).toFixed(2)),
  exitZ: p.exitZ ?? Number((p.z + Math.cos(p.rotation || 0) * 1.3).toFixed(2)),
  label: p.label || 'Шафа',
}));
const activeLockers = customLockers.length > 0 ? customLockers : lockers;

let renderer, scene, camera, effect, world, enemyModel, bloodyEnemyModel, phantomModel, flashlight, handFill, held;
try {
  renderer = new THREE.WebGLRenderer({ canvas: $('viewport'), antialias: false, powerPreference: 'high-performance' });
  renderer.setPixelRatio(Math.min(devicePixelRatio, 1.5));
  renderer.setSize(innerWidth, innerHeight);
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.3;
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;
  scene = new THREE.Scene();
  scene.background = new THREE.Color('#0a100a');
  scene.fog = new THREE.FogExp2('#0a100a', 0.032);
  camera = new THREE.PerspectiveCamera(68, innerWidth / innerHeight, 0.08, 85);
  camera.rotation.order = 'YXZ';
  scene.add(camera);
  world = createWorld(scene, customProps);
  exterior = createExterior(scene);
  exterior.setLobby(true);
  lobby = createLobby(scene);
  scene.add(new THREE.HemisphereLight(0x939f85, 0x292115, 0.34));

  flashlight = new THREE.SpotLight(0xffe9b7, 32, 24, Math.PI / 6.5, .6, 1.3);
  flashlight.position.set(.16,-.12,-.15); flashlight.target.position.set(0,0,-10);
  flashlight.castShadow = true; flashlight.shadow.mapSize.set(512,512); flashlight.shadow.bias=-.001;
  camera.add(flashlight, flashlight.target);
  handFill = new THREE.PointLight(0xacc99b, .5, 3, 1.5); camera.add(handFill);
  held = createHeldEquipment(); camera.add(held);

  // Normal stalker
  enemyModel = createCharacter('#272c24', true, false); scene.add(enemyModel); enemyModel.visible=false;
  // Bloody transformed stalker for nightmare escape phase
  bloodyEnemyModel = createCharacter('#4a1815', true, true); scene.add(bloodyEnemyModel); bloodyEnemyModel.visible=false;
  // Hallucination phantom shadow
  phantomModel = createCharacter('#141812', true, false); scene.add(phantomModel); phantomModel.visible=false;

  // Compile visible and hidden enemy materials before the escape phase.
  enemyModel.visible = bloodyEnemyModel.visible = phantomModel.visible = true;
  try { renderer.compile(scene, camera); } catch (error) { console.warn('Shader warmup:', error); }
  enemyModel.visible = bloodyEnemyModel.visible = phantomModel.visible = false;

  effect = createTapeEffect(renderer, scene, camera);
  applyClassSpecs(selectedClass);
  resize();

  lobby.sync([],null,selectedClass,true);
} catch (error) {
  console.error(error);
  const el=document.createElement('div'); el.className='fallback-error';
  const p=document.createElement('p'); p.textContent='Не вдалося запустити 3D-графіку. Відкрийте гру в Chrome або Edge з увімкненим апаратним прискоренням. '+error.message; el.append(p); app.append(el);
}

function createHeldEquipment() {
  const group = new THREE.Group();
  const cData = getClassData(selectedClass);
  const skin = new THREE.MeshLambertMaterial({color:0x85765c,flatShading:true});
  const coat = new THREE.MeshLambertMaterial({color:cData.color,flatShading:true});
  const metal = new THREE.MeshLambertMaterial({color:0x252b28});
  const makeBox=(w,h,d,mat,x,y,z)=>{const m=new THREE.Mesh(new THREE.BoxGeometry(w,h,d),mat);m.position.set(x,y,z);group.add(m);return m;};
  makeBox(.13,.16,.3,coat,.34,-.4,-.4).rotation.x=-.3;
  makeBox(.12,.12,.15,skin,.32,-.32,-.58);
  const body=makeBox(.24,.19,.34,metal,.3,-.25,-.65);
  const lens=new THREE.Mesh(new THREE.CylinderGeometry(.074,.087,.15,10),metal);lens.rotation.x=Math.PI/2;lens.position.set(.3,-.24,-.9);group.add(lens);
  const glass=new THREE.Mesh(new THREE.CircleGeometry(.064,12),new THREE.MeshBasicMaterial({color:0x8cab81}));glass.position.set(.3,-.24,-.98);glass.rotation.y=Math.PI;group.add(glass);
  const light=new THREE.Mesh(new THREE.BoxGeometry(.016,.016,.012),new THREE.MeshBasicMaterial({color:0xe76543}));light.position.set(.36,-.21,-.473);group.add(light);
  body.userData.name='VHS-C';group.visible=false;
  group.userData.coat = coat;
  return group;
}

function applyClassSpecs(classId) {
  const data = getClassData(classId);
  if (flashlight) {
    flashlight.distance = data.bonuses.flashlightDist;
    flashlight.angle = data.bonuses.flashlightAngle;
  }
  if (held?.userData?.coat) {
    held.userData.coat.color.set(data.color);
  }
}

function selectClass(cls) {
  if (!CHARACTER_CLASSES[cls]) return;
  selectedClass = cls;
  localStorage.setItem('dead-air-class', cls);
  $('selected-name').textContent = getClassData(cls).name;
  $('selected-role').textContent = getClassData(cls).title;
  $('selected-perk').textContent = getClassData(cls).perk;
  if (!playing && mode !== 'lobby') lobby?.sync([],null,cls,true);
  applyClassSpecs(cls);
  document.querySelectorAll('.class-card').forEach(card => {
    card.classList.toggle('selected', card.dataset.class === cls);
    card.setAttribute('aria-pressed', String(card.dataset.class === cls));
  });
}

document.querySelectorAll('#menu-class-grid .class-card').forEach(card => {
  card.onclick = () => selectClass(card.dataset.class);
});

function resize(){if(!renderer)return;camera.aspect=innerWidth/innerHeight;camera.updateProjectionMatrix();renderer.setSize(innerWidth,innerHeight);effect?.resize(innerWidth,innerHeight,Number(settings.quality));}
window.addEventListener('resize',resize);
function stamp(seconds){const s=Math.floor(seconds);return `${String(Math.floor(s/3600)).padStart(2,'0')}:${String(Math.floor(s/60)%60).padStart(2,'0')}:${String(s%60).padStart(2,'0')}`;}
function toast(message){$('toast').textContent=message;$('toast').hidden=false;toastUntil=performance.now()+4500;}
function subtitle(label,text,duration=8000){$('subtitle').innerHTML=`<b>${escape(label)}</b>${escape(text)}`;$('subtitle').hidden=false;subtitleUntil=performance.now()+duration;}
function clickSound(){if(audioOn)audio.play('click');}
async function enableAudio(){if(!audioOn){audioOn=await audio.start();audio.setMuted(false);$('sound-button').innerHTML=icon(audioOn?'sound':'mute');$('sound-button').setAttribute('aria-label',audioOn?'Вимкнути звук':'Увімкнути звук');}}
function emit(event,data) {return new Promise((resolve,reject)=>{const ack=(err,result)=>{if(err)reject(new Error('Сервер не відповідає. Перевірте з’єднання.'));else if(!result?.ok)reject(new Error(result?.error||'Не вдалося виконати дію.'));else resolve(result);};if(data===undefined)socket.timeout(6500).emit(event,ack);else socket.timeout(6500).emit(event,data,ack);});}
function panel(title,body,eyebrow='BUREAU–9 / АРХІВ 004',closable=true){$('overlay').innerHTML=`<section class="panel">${closable?`<button id="close-panel" class="icon-btn close" aria-label="Закрити">${icon('close')}</button>`:''}<div class="eyebrow">${eyebrow}</div><h2>${title}</h2>${body}</section>`;$('overlay').hidden=false;if(closable)$('close-panel').onclick=closePanel;requestAnimationFrame(()=>$('overlay').querySelector('button,input')?.focus());}
function releasePointer(){keys.clear();document.exitPointerLock?.();}
function openPanel(next){returnMode=playing?'pause':'menu';mode=next;releasePointer();if(playing&&state?.players.length===1)socket.emit('game:pause',{paused:true},()=>{});}
function closePanel(){
  $('overlay').hidden=true;
  $('overlay').innerHTML='';
  if(playing){
    if(mode==='map'){
      resume();
    }else if(returnMode==='pause'){
      showPause();
    }else{
      resume();
    }
  }else{
    mode='menu';
    $('menu').hidden=false;
    exterior?.setLobby(false);
  }
}
$('overlay').addEventListener('click',e=>{
  if(e.target===$('overlay')){
    if(mode!=='pause'&&mode!=='ending'&&mode!=='lobby'){
      closePanel();
    }
  }
});
async function lockPointer(){try{await $('viewport').requestPointerLock();}catch{toast('Клацніть у грі для огляду мишею. Стрілки також повертають камеру.');}}
async function resume(){if(!playing||state?.phase!=='playing')return;mode='game';$('overlay').hidden=true;keys.clear();gameLog('ACTION','Сеанс відновлено з паузи');await enableAudio();if(state?.players.length===1)socket.emit('game:pause',{paused:false},()=>{});await lockPointer();}

$('sound-button').onclick=async()=>{if(!audioOn)await enableAudio();else{audioOn=false;audio.setMuted(true);$('sound-button').innerHTML=icon('mute');$('sound-button').setAttribute('aria-label','Увімкнути звук');}};
$('fullscreen-button').onclick=async()=>{try{if(document.fullscreenElement)await document.exitFullscreen();else await document.documentElement.requestFullscreen();}catch{toast('Повноекранний режим недоступний у цьому переглядачі.');}};
$('solo-button').onclick=async()=>{
  if(handlingAction)return;handlingAction=true;$('solo-button').disabled=true;
  const isCreative = Boolean($('creative-mode-toggle')?.checked);
  try{
    await enableAudio();
    const charName = getClassData(selectedClass).name;
    const r=await emit('room:create',{name:charName,solo:true,creative:isCreative,characterClass:selectedClass});
    myId=r.playerId||socket.id;
    await emit('game:start');
  }
  catch(e){toast(e.message);}finally{handlingAction=false;$('solo-button').disabled=false;}
};
$('coop-button').onclick=showCoop;
$('dossier-button').onclick=showDossier;
$('settings-button').onclick=showSettings;
$('pause-button').onclick=showPause;
$('viewport').addEventListener('click',()=>{if(playing&&mode==='game')lockPointer();});

function showCoop(){
  openPanel('coop');
  const defaultName = localStorage.getItem('dead-air-name') || getClassData(selectedClass).name;
  panel('Ніхто не йде сам.',`
  <p>Зберіть до чотирьох операторів. Касети, запобіжники, шафи й переслідувач спільні для всієї групи.</p>
  <label for="player-name">ВАШ ПОЗИВНИЙ</label><input id="player-name" maxlength="20" placeholder="${escape(getClassData(selectedClass).name)}" autocomplete="off" value="${escape(defaultName)}">
  <div class="creative-toggle-wrap" style="margin-bottom:15px"><label class="toggle-label" for="coop-creative"><input type="checkbox" id="coop-creative"><span class="toggle-slider"></span><span class="toggle-text">РЕЖИМ ДОСЛІДЖЕННЯ (Креатив)</span></label></div>
  <button class="action primary" id="create-room"><span>${icon('people')} СТВОРИТИ КІМНАТУ</span>${icon('arrow')}</button>
  <div class="divider">АБО ПРИЄДНАТИСЯ</div><label for="room-code">КОД КІМНАТИ</label><div class="two-columns"><input id="room-code" maxlength="6" placeholder="XXXXXX" style="text-transform:uppercase;letter-spacing:4px" autocomplete="off"><button class="action" id="join-room" style="margin:0"><span>ПРИЄДНАТИСЯ</span>${icon('arrow')}</button></div><p class="error" id="room-error"></p><p class="pause-note">Усі гравці мають відкрити той самий сервер гри. Для гри через інтернет скористайтеся тунелем localtunnel/Cloudflare або Radmin VPN.</p>
  <button class="action" id="cancel-coop-btn" style="margin-top:14px">← ПОВЕРНУТИСЯ ДО МЕНЮ</button>`);
  const action=async(join)=>{
    if(handlingAction)return;
    const name=$('player-name').value.trim()||getClassData(selectedClass).name;
    const code=$('room-code').value.trim().toUpperCase();
    if(join&&code.length<4){$('room-error').textContent='Введіть код кімнати.';return;}
    handlingAction=true;$('create-room').disabled=true;$('join-room').disabled=true;
    try{
      localStorage.setItem('dead-air-name',name);
      await enableAudio();
      const isCreative=Boolean($('coop-creative')?.checked);
      const result=await emit(join?'room:join':'room:create',join?{name,code,characterClass:selectedClass}:{name,solo:false,creative:isCreative,characterClass:selectedClass});
      myId=result.playerId||socket.id;
      showLobby();
    }catch(e){if($('room-error'))$('room-error').textContent=e.message;}
    finally{handlingAction=false;if($('create-room')){$('create-room').disabled=false;$('join-room').disabled=false;}}
  };
  $('create-room').onclick=()=>action(false);$('join-room').onclick=()=>action(true);$('room-code').onkeydown=e=>{if(e.key==='Enter')action(true);};
  $('cancel-coop-btn').onclick=closePanel;
}

function showLobby(){
  mode='lobby';
  releasePointer();
  $('overlay').hidden=true;
  exterior?.setArrival(1);
  exterior?.setLobby(true);
  $('menu').hidden=true;
  $('hud').hidden=true;
  $('lobby-screen').hidden=false;
  lobbySignature='';
  drawLobby();
  gameLog('LOBBY', `Відкрито 3D кімнату очікування. Код сеансу: [${state?.code}]`);
}

function drawLobby(){
  if(!state||mode!=='lobby')return;
  const signature=JSON.stringify([state.code,state.hostId,selectedClass,(state.players||[]).map(p=>[p.id,p.name,p.characterClass])]);
  if(lobbySignature===signature)return;
  lobbySignature=signature;

  const host=state.hostId===myId;
  const codeEl=$('lobby-room-code');
  if(codeEl) codeEl.textContent=state.code;
  const countEl=$('lobby-player-count');
  if(countEl) countEl.textContent=`${state.players.length} / 4 НА ЗВ’ЯЗКУ`;
  const statusEl=$('lobby-status-text');
  if(statusEl){
    statusEl.innerHTML=host
      ? `Ви — хост ефіру. Коли всі оператори готові, натисніть <b>«ПОЧАТИ ЗАПИС»</b>.`
      : `Очікуємо, поки хост розпочне запис ефіру. Оберіть свій клас для балансу групи.`;
  }

  const startBtn=$('lobby-start-btn');
  const startLbl=$('lobby-start-label');
  if(startBtn&&startLbl){
    if(host){
      startBtn.disabled=false;
      startLbl.textContent='Почати запис';
    }else{
      startBtn.disabled=true;
      startLbl.textContent='Чекаємо на хоста';
    }
  }

  document.querySelectorAll('#lobby-picker-cards .picker-card').forEach(card => {
    card.classList.toggle('selected', card.dataset.class === selectedClass);
  });

  lobby?.sync(state.players,state.hostId,selectedClass,false);
}

$('btn-copy-code').onclick=async()=>{
  try{
    await navigator.clipboard.writeText(state.code);
    toast('Код кімнати скопійовано.');
    gameLog('LOBBY', `Код кімнати [${state.code}] скопійовано в буфер`);
  }catch{
    toast(`Код кімнати: ${state.code}`);
  }
};

$('lobby-start-btn').onclick=async()=>{
  gameLog('LOBBY', 'Хост натиснув «ПОЧАТИ ЗАПИС»');
  try{
    await enableAudio();
    await emit('game:start');
  }catch(e){
    toast(e.message);
  }
};

$('lobby-leave-btn').onclick=leaveGame;

document.querySelectorAll('#lobby-picker-cards .picker-card').forEach(card => {
  card.onclick = async () => {
    const cls = card.dataset.class;
    if (!cls) return;
    selectClass(cls);
    gameLog('CLASS', `Змінено клас персонажа на: "${cls}" (${CHARACTER_CLASSES[cls]?.name})`);
    try {
      await emit('player:class', { characterClass: cls });
    } catch (err) {
      toast(err.message);
    }
    lobbySignature = '';
    drawLobby();
  };
});

const controls=`<div class="control-grid"><div><kbd>W A S D</kbd> Рух</div><div><kbd>МИША</kbd> Огляд</div><div><kbd>E</kbd> Взяти / шафа</div><div><kbd>SHIFT</kbd> Біг</div><div><kbd>F</kbd> Ліхтарик</div><div><kbd>V</kbd> Підняти камеру</div><div><kbd>Q</kbd> Рація</div><div><kbd>M</kbd> План станції</div></div>`;
function showDossier(){
  openPanel('dossier');
  panel('Матеріали справи № 004 · BUREAU–9: BLACKPINE BROADCAST',`
    <div class="dossier-grid">
      <div class="dossier-entry">
        <h4>1. МІСІЯ БЮРО «BUREAU–9» (ОПЕРАЦІЯ 004)</h4>
        <p>«BUREAU–9» — міжнародне незалежне бюро журналістських розслідувань надзвичайних медіааномалій. О 02:17 з законсервованого ретранслятора «Blackpine Broadcast / Station 04» перехоплено несанкціонований сигнал біди. Оперативна група направлена на службовому фургоні для вилучення оригіналів записів та встановлення долі нічної зміни.</p>
      </div>
      <div class="dossier-entry">
        <h4>2. СУБ'ЄКТ РОЗСЛІДУВАННЯ: АРТУР КОУЛ ТА СТАНЦІЯ 04</h4>
        <p>Старший звукоінженер Артур Коул (Артем Коваль) залишив 3 майстер-касети з хронікою поглинання станції психоакустичним сигналом. Аварія вибила два високовольтні запобіжники на 380В у вентвузол та серверну. Персонал безслідно зник, а коридори захопила сутність «Диктор» (The Anchor).</p>
      </div>
      <div class="dossier-entry">
        <h4>3. СУТНІСТЬ «ДИКТОР» (THE ANCHOR)</h4>
        <p>Фізичне тіло деформоване частотним резонансом. Воно полює в темряві й реагує на радіохвилі — <strong>будь-яке слово в рацію привертає його увагу</strong>. Істоту можна стримувати об'єктивом увімкненої камери <kbd>V</kbd> з ліхтарем <kbd>F</kbd>. У разі небезпеки ховайтеся в шафи <kbd>E</kbd>.</p>
      </div>
      <div class="dossier-entry">
        <h4>4. ОПЕРАТИВНИЙ АЛГОРИТМ ЕВАКУАЦІЇ</h4>
        <p>1. Зберіть 3 майстер-касети Артура Коула (Архів, Студія 04, Техсклад).<br>
           2. Знайдіть 2 промислових керамічних запобіжники 380В (Вентвузол та Серверна).<br>
           3. Вставте запобіжники в щит генераторної та підніміть рубильник живлення.<br>
           4. В апаратній запустіть передачу на супутники BUREAU–9 через пульт трансляції.<br>
           5. Біжіть коридором у двір та сідайте у службовий фургон евакуації!</p>
      </div>
      <div class="dossier-entry">
        <h4>5. ОПЕРАТИВНА ГРУПА BUREAU–9</h4>
        <p>• <strong>Віктор (Victor)</strong> — Інженер / охорона. Потужний ліхтар (+25% дальність) та швидке відновлення витривалості.<br>
           • <strong>Вівіан (Vivian)</strong> — Операторка / кураторка. Збільшений світловий шок камери (+35% стан диктора) та висока психостійкість.<br>
           • <strong>Едґар (Edgar)</strong> — Журналіст / архівіст. Спринт (+15% швидкість) та інтуїтивне нічне чуття касет.<br>
           • <strong>Гейзел (Hazel)</strong> — Диспетчерка / зв'язківиця. Радіопередачі кожні 1.5 с та звуковий радар наближення ворога з 22м.</p>
      </div>
    </div>
    ${controls}
    <button class="action primary" id="close-dossier-btn" style="margin-top:16px">ЗАКРИТИ СПРАВУ ${icon('arrow')}</button>`);
  $('close-dossier-btn').onclick=closePanel;
}

function showSettings(){openPanel('settings');panel('Налаштувати сигнал.',`
  <div class="settings-row"><label for="quality">РОЗДІЛЬНІСТЬ<small>Низька — виразніші пікселі</small></label><select id="quality"><option value="240">240p · PS1</option><option value="360">360p · VHS</option><option value="540">540p · Чітка</option></select></div>
  <div class="settings-row"><label for="volume">ГУЧНІСТЬ</label><input id="volume" type="range" min="0" max="1" step=".05" value="${settings.volume}"></div>
  <div class="settings-row"><label for="sensitivity">ЧУТЛИВІСТЬ МИШІ</label><input id="sensitivity" type="range" min=".3" max="2" step=".1" value="${settings.sensitivity}"></div>
  <div class="settings-row"><label for="grain">ПЛІВКОВЕ ЗЕРНО<small>Шум, смуги й розділення кольорів</small></label><input id="grain" type="checkbox" ${settings.grain?'checked':''}></div>
  <div class="settings-row"><label for="flicker">МЕРЕХТІННЯ<small>Вимкніть для спокійніших ефектів</small></label><input id="flicker" type="checkbox" ${settings.flicker?'checked':''}></div><p class="pause-note">Налаштування зберігаються на цьому пристрої.</p>
  <button class="action primary" id="close-settings-btn" style="margin-top:14px">ЗБЕРЕГТИ ТА ЗАКРИТИ</button>`);
  $('quality').value=settings.quality;for(const id of ['quality','volume','sensitivity','grain','flicker'])$(id).oninput=()=>{settings[id]=['grain','flicker'].includes(id)?$(id).checked:Number($(id).value);localStorage.setItem('dead-air-settings',JSON.stringify(settings));audio.setVolume(settings.volume);resize();};
  $('close-settings-btn').onclick=closePanel;
}
function showPause(){if(!playing)return;mode='pause';releasePointer();if(state?.players.length===1)socket.emit('game:pause',{paused:true},()=>{});panel('Запис призупинено.',`${controls}<button class="action primary" id="resume-game"><span>${icon('play')} ПРОДОВЖИТИ ЗАПИС</span>${icon('arrow')}</button><div class="two-columns"><button class="action" id="pause-settings">НАЛАШТУВАННЯ</button><button class="action" id="pause-exit">ДО ГОЛОВНОГО МЕНЮ</button></div><p class="pause-note">${state?.creative?'РЕЖИМ ДОСЛІДЖЕННЯ АКТИВОВАНО.':state?.players.length>1?'Група продовжує гру. Істота не чекає.':'Одиночну гру призупинено.'}</p>`,'КАМЕРА / STANDBY',false);$('resume-game').onclick=resume;$('pause-settings').onclick=showSettings;$('pause-exit').onclick=leaveGame;}

function showMap(){
  openPanel('map');
  panel('План телестанції.',`
  <p>Ви зараз: <strong>${roomAt(localPos.x,localPos.z)}</strong>. Знайдіть 3 касети, 2 запобіжники та використовуйте шафи [Ш] для схованки.</p>
  <svg viewBox="0 -80 540 700" style="width:100%;max-height:60dvh;stroke:#829071;stroke-width:1;fill:#152016;font-family:monospace">
    <path d="M30 -70H510V15H30Z" fill="#233024"/><g fill="#cbd2b9" stroke="none" font-size="11"><text x="55" y="-42">ДВІР</text><text x="55" y="-16">ФУРГОН / ЕВАКУАЦІЯ</text></g><path d="M270 15V30" stroke="#d5bd77" stroke-width="6"/>
    <!-- Центральний довгий коридор -->
    <path d="M220 30h100v560H220Z" fill="#1b251c"/>
    <!-- Сектор 1 (верхній) -->
    <path d="M30 30h175v120H30ZM335 30h175v120H335ZM30 170h175v120H30ZM335 170h175v120H335Z"/>
    <!-- Сектор 2 (глибинний) -->
    <path d="M30 320h175v120H30ZM335 320h175v120H335ZM30 460h175v120H30ZM335 460h175v120H335Z"/>
    <!-- Обхідні бічні тунелі (петлі) -->
    <path d="M40 290h30v30H40ZM470 290h30v30H470Z" fill="#2d3b2a" stroke="#d5bd77"/>
    <!-- Дверні отвори -->
    <path d="M205 90h15m115 0h15M205 230h15m115 0h15M205 380h15m115 0h15M205 520h15m115 0h15" stroke="#d5bd77" stroke-width="6"/>
    <!-- Підписи -->
    <g fill="#cdd3bb" stroke="none" text-anchor="middle" font-size="11">
      <text x="117" y="75">АРХІВ</text><text x="117" y="98">① КАСЕТА</text>
      <text x="422" y="75">АПАРАТНА</text><text x="422" y="98">ПЕРЕДАВАЧ</text>
      <text x="117" y="215">ГЕНЕРАТОР</text><text x="117" y="238">⚡ ЩИТОК 380В</text>
      <text x="422" y="215">СТУДІЯ 04</text><text x="422" y="238">② КАСЕТА</text>
      <text x="117" y="365">ВЕНТВУЗОЛ</text><text x="117" y="388">⚙ ЗАПОБІЖНИК 1</text>
      <text x="422" y="365">СЕРВЕРНА</text><text x="422" y="388">⚙ ЗАПОБІЖНИК 2</text>
      <text x="117" y="505">ТЕХСКЛАД</text><text x="117" y="528">③ КАСЕТА</text>
      <text x="422" y="505">ДИСПЕТЧЕР</text><text x="422" y="528">РАДІОВЕЖА</text>
      <text x="270" y="58" fill="#d5bd77">ВИХІД ↓</text>
      <text x="55" y="310" font-size="8" fill="#d5bd77">ПЕТЛЯ</text>
      <text x="485" y="310" font-size="8" fill="#d5bd77">ПЕТЛЯ</text>
    </g>
  </svg>
  <button class="action primary" id="close-map">ПОВЕРНУТИСЯ ДО ЗАПИСУ ${icon('arrow')}</button>`);
  $('close-map').onclick=resume;
}

function getRoomSubtitle(roomName) {
  switch (roomName) {
    case 'ДВІР СТАНЦІЇ': return 'BLACKPINE BROADCAST / ФУРГОН БІЛЯ ВОРІТ';
    case 'ГОЛОВНИЙ КОРИДОР': return 'ЦЕНТРАЛЬНА ВІСЬ СТАНЦІЇ · СЕКТОР А/Б';
    case 'АРХІВ': return 'АРХІВ ПЛІВОК 1986–1996 · КАСЕТА 01';
    case 'АПАРАТНА': return 'ПУЛЬТ ТРАНСЛЯЦІЇ 96.4 MHz · ПЕРЕДАВАЧ';
    case 'ГЕНЕРАТОРНА': return 'ЕЛЕКТРОЩИТ 380В · ПОТРІБНІ 2 ЗАПОБІЖНИКИ';
    case 'СТУДІЯ 04': return 'ПАВІЛЬЙОН НОВИН · КАСЕТА 02';
    case 'ВЕНТВУЗОЛ': return 'ВЕНТИЛЯЦІЙНИЙ КОНТУР Б · ЗАПОБІЖНИК 01';
    case 'СЕРВЕРНА': return 'ОБЧИСЛЮВАЛЬНИЙ ЦЕНТР · ЗАПОБІЖНИК 02';
    case 'ТЕХСКЛАД': return 'РЕЗЕРВНИЙ АРХІВ · КАСЕТА 03';
    case 'ДИСПЕТЧЕРСЬКА': return 'ГОЛОВНИЙ ПОСТ РАДІОВЕЖІ № 3';
    default: return 'BLACKPINE BROADCAST · СТАНЦІЯ 04';
  }
}

function showLocationTitle(name, detail = '') {
  const card = $('location-card');
  if (!card) return;
  $('loc-card-name').textContent = name;
  $('loc-card-desc').textContent = detail;
  card.hidden = false;
  card.classList.remove('fade-out');
  card.classList.add('fade-in');
  if (locationCardTimer) clearTimeout(locationCardTimer);
  locationCardTimer = setTimeout(() => {
    card.classList.remove('fade-in');
    card.classList.add('fade-out');
    setTimeout(() => { card.hidden = true; }, 800);
  }, 3800);
}

async function leaveGame(){
  playing=false;mode='menu';releasePointer();
  $('lobby-screen').hidden=true;$('arrival').hidden=true;$('countdown-banner').hidden=true;$('radio-menu').hidden=true;
  exterior?.setArrival(1);lobby?.sync([],null,selectedClass,true);
  try{await emit('room:leave');}catch{}
  state=null;prevTapes=[];world?.setState({tapes:[],fuses:[],power:false,broadcast:false});
  $('menu').hidden=false;$('hud').hidden=true;$('overlay').hidden=true;$('locker-overlay').hidden=true;
  $('location-card').hidden=true;$('escape-banner').hidden=true;
  $('damage').style.opacity=0;for(const g of remotePlayers.values())scene.remove(g);remotePlayers.clear();
  enemyModel.visible=false;bloodyEnemyModel.visible=false;phantomModel.visible=false;
}

function beginGame(){
  exterior?.setLobby(false);
  playing=true;mode='arrival';keys.clear();yaw=0;pitch=0;
  localPos.set(spawn.x,1.65,spawn.z);
  const me=state.players.find(p=>p.id===myId);if(me)localPos.set(me.x,1.65,me.z);
  if(me?.characterClass)selectClass(me.characterClass);
  flashlightOn=true;cameraOn=false;stamina=100;lastScare=state.scare||0;prevTapes=[...state.tapes];
  lastRoom=roomAt(localPos.x,localPos.z);
  for(const id of ['menu','hud','overlay','lobby-screen','down-notice','locker-overlay','escape-banner','location-card','radio-menu'])$(id).hidden=true;
  lobby?.update(elapsed,false);
  arrivalStarted=performance.now();arrivalProgress=0;exterior?.setArrival(0);$('arrival').hidden=false;
  if((state.elapsed||0)>8)finishArrival();
  audio.play('radio');
}

function finishArrival(){
  if(!playing||state?.phase!=='playing')return;
  arrivalProgress=1;exterior?.setArrival(1);mode='game';$('arrival').hidden=true;$('hud').hidden=false;
  yaw=0;pitch=0;keys.clear();lockPointer();
  showLocationTitle('BLACKPINE BROADCAST','23.10.1996 / ОСТАННЯ НІЧНА ЗМІНА');
  subtitle('РАЦІЯ / HAZEL',state.creative?'Станцію можна дослідити без переслідування. Вхід попереду.':'Ми на місці. Заберіть три касети й знайдіть два запобіжники. Ключі лишаю у фургоні.',8500);
}
$('skip-arrival').onclick=finishArrival;

function showEnding(won){
  playing=false;mode='ending';$('arrival').hidden=true;$('lobby-screen').hidden=true;$('radio-menu').hidden=true;releasePointer();audio.play(won?'win':'lose');$('hud').hidden=true;$('locker-overlay').hidden=true;$('escape-banner').hidden=true;
  panel(won?'BUREAU–9: СИГНАЛ ПЕРЕХОПЛЕНО':'BUREAU–9: ЗВ’ЯЗОК ВТРАЧЕНО',`
    <div class="end-number">${won?'03:00':'NO SIGNAL'}</div>
    <p>${won?'Службовий фургон подолав ворота станції Blackpine Broadcast. Усі 3 майстер-касети Артура Коула передано на супутники штабу BUREAU–9. Проте на аналітичному екрані фургона в останніх секундах запису чути... як хтось із групи продовжує говорити голосом Диктора.':'Службовий фургон знайдено порожнім із відчиненими дверима. На частоті 96.4 MHz диктор Blackpine оголошує імена членів оперативної групи як чергових дикторів нічного ефіру. Справа 004 засекречена.'}</p>
    <p>Тривалість операції: ${stamp(state.elapsed||0)} &nbsp; · &nbsp; Зібрано касет: ${state.tapes.length}/3</p>
    <button class="action primary" id="restart-game" ${state.hostId===myId?'':'disabled'}>${state.hostId===myId?'ПОВТОРИТИ ОПЕРАЦІЮ':'ЧЕКАЄМО НА ХОСТА'} ${icon('arrow')}</button>
    <button class="action" id="end-menu">ДО ГОЛОВНОГО МЕНЮ</button>`,'КІНЕЦЬ ЗАПИСУ / СПРАВА 004',false);
  $('restart-game').onclick=async()=>{try{await emit('game:restart');}catch(e){toast(e.message);}};$('end-menu').onclick=leaveGame;
}

// Socket Network Handlers
socket.on('connect',()=>{
  $('connection-status').textContent='СИГНАЛ Є';myId=socket.id;
  if(playing&&state?.code){
    // Automatic quiet reconnection
    socket.emit('room:join',{code:state.code,name:localStorage.getItem('dead-air-name')||'Оператор'});
  }
});
socket.on('connect_error',()=>{$('connection-status').textContent='НЕМАЄ СИГНАЛУ';});
socket.on('disconnect',()=>{
  $('connection-status').textContent='НЕМАЄ СИГНАЛУ';
  if(playing||mode==='lobby'){
    toast('Зв’язок втрачено. Спроба перепідключення...');
  }
});

socket.on('room:state',next=>{
  const oldPhase=state?.phase;const oldPower=state?.power;const oldBroadcast=state?.broadcast;const oldHost=state?.hostId;
  const oldRadioTime=state?.radioTransmission?.time;
  state=next;myId ||= socket.id;
  if(next.phase==='lobby'){
    if(mode==='coop') showLobby();
    else if(mode==='lobby') drawLobby();
    return;
  }
  if(next.phase==='playing'&&(!playing&&mode!=='disconnected')){beginGame();}
  if(next.phase==='playing'){
    const me=next.players.find(p=>p.id===myId);
    if(me){
      if(me.hiding){
        const l = activeLockers.find(x => x.id === me.hiding);
        if(l) localPos.set(l.x, 1.65, l.z);
      } else {
        const err=Math.hypot(me.x-localPos.x,me.z-localPos.z);
        if(err>2.2)localPos.set(me.x,1.65,me.z);
        else if(err>0.8&&!moving){localPos.x=THREE.MathUtils.lerp(localPos.x,me.x,0.18);localPos.z=THREE.MathUtils.lerp(localPos.z,me.z,0.18);}
      }
      $('down-notice').hidden=!me.down;
      $('locker-overlay').hidden=!me.hiding;
    }
    for(const id of next.tapes.filter(id=>!prevTapes.includes(id))){
      audio.play('pickup');
      const lines={
        'tape-1':['КАСЕТА 01 / НІЧНА ЗМІНА','02:17. «У студії хтось є. Він читає прогноз погоди. Але губи не рухаються».'],
        'tape-2':['КАСЕТА 02 / ЗАЙВИЙ ГОЛОС','02:41. «Він чує рацію. Не кричіть у мікрофон, якщо світло згасло».'],
        'tape-3':['КАСЕТА 03 / ОСТАННІЙ ЕФІР','02:59. «Увімкніть передавач на пульті. І біжіть до виходу без оглядки».'],
      };
      subtitle(...(lines[id]||['КАСЕТА','Запис знайдено.']),11000);
    }
    prevTapes=[...next.tapes];
    if(next.power&&!oldPower){audio.play('power');toast('Живлення відновлено. Передавач в апаратній готовий.');}
    if(next.broadcast&&!oldBroadcast){
      audio.play('scare');
      audio.play('alarm');
      subtitle('ТРИВОГА / 03:00','Сигнал пішов. Залишайте обладнання. Повертайтеся до фургона — негайно.',12000);
    }
    if(next.scare>lastScare){audio.play('scare');if(settings.flicker)threat=Math.max(threat,.9);}
    lastScare=next.scare||0;

    // Incoming radio transmission
    if(next.radioTransmission&&next.radioTransmission.time!==oldRadioTime){
      audio.play('radio');
      const t=next.radioTransmission;
      subtitle(`РАЦІЯ / ${t.name}`,t.phrase,5000);
    }
    syncPlayers();
  }
  if((next.phase==='won'||next.phase==='lost')&&(oldPhase!==next.phase||oldHost!==next.hostId)){showEnding(next.phase==='won');}
});

function syncPlayers(){
  for(const p of state.players){
    if(p.id===myId)continue;
    let mesh=remotePlayers.get(p.id);
    const pClass = p.characterClass || 'engineer';
    if(!mesh || mesh.userData.charClass !== pClass){
      if(mesh) scene.remove(mesh);
      mesh=createCharacter(p.color, false, false, pClass);
      mesh.userData.charClass = pClass;
      scene.add(mesh);
      remotePlayers.set(p.id,mesh);
      mesh.position.set(p.x,0,p.z);
    }
    mesh.visible=!p.hiding;
    mesh.userData.target={...p};
  }
  for(const[id,mesh]of remotePlayers){
    if(!state.players.some(p=>p.id===id)){scene.remove(mesh);remotePlayers.delete(id);}
  }
}

// Interaction with Directional Gaze Check
function chooseInteraction(){
  if(!state)return null;
  const me=state.players.find(p=>p.id===myId);
  if(!me||me.down)return null;
  if(me.hiding)return{id:'exit-locker',label:'Вийти з шафи',type:'locker-exit'};

  let best=null,dist=2.45;
  const forwardX=-Math.sin(yaw),forwardZ=-Math.cos(yaw);

  // Lockers for hiding
  for(const l of activeLockers){
    const d=Math.hypot(l.x-localPos.x,l.z-localPos.z);
    if(d<2.0&&d<dist){
      const toX=(l.x-localPos.x)/(d||1),toZ=(l.z-localPos.z)/(d||1);
      const dot=forwardX*toX+forwardZ*toZ;
      if(dot>0.4||d<0.9){dist=d;best={id:l.id,label:`Сховатися: ${l.label}`,type:'locker'};}
    }
  }

  // Objectives (tapes, fuses, power, broadcast, exit)
  for(const o of objects){
    if(o.type==='tape'&&state.tapes.includes(o.id))continue;
    if(o.type==='fuse'&&(state.fuses||[]).includes(o.id))continue;
    if(o.id==='power'&&state.power)continue;
    if(o.id==='broadcast'&&state.broadcast)continue;
    const d=Math.hypot(o.x-localPos.x,o.z-localPos.z);
    if(d<2.45&&d<dist){
      const toX=(o.x-localPos.x)/(d||1),toZ=(o.z-localPos.z)/(d||1);
      const dot=forwardX*toX+forwardZ*toZ;
      if(dot>0.4||d<0.9){
        dist=d;
        if(o.id==='power'){
          const fuseCount=(state.fuses||[]).length;
          best={
            ...o,
            label: fuseCount >= 2
              ? 'Вставити 2 запобіжники та відновити живлення'
              : `Електрощит: потрібно 2 запобіжники 380В (${fuseCount}/2)`
          };
        } else {
          best=o;
        }
      }
    }
  }

  // Revive downed partner
  for(const p of state.players){
    if(!p.down||p.id===myId)continue;
    const d=Math.hypot(p.x-localPos.x,p.z-localPos.z);
    if(d<2.2&&d<dist){dist=d;best={id:`revive:${p.id}`,label:`Підняти ${p.name}`,type:'revive'};}
  }
  return best;
}

async function interact(){
  if(handlingAction||!currentInteract||mode!=='game')return;
  const target=currentInteract;
  handlingAction=true;
  try{
    if(target.type==='locker'){
      const l = activeLockers.find(x => x.id === target.id);
      if(l){
        localPos.set(l.x, 1.65, l.z);
        keys.clear();
        lastSend = performance.now();
        socket.emit('player:move', { x: l.x, z: l.z, yaw, pitch, flashlight: false, camera: false, sprinting: false });
      }
    }
    const res=await emit('game:interact',{id:target.id});
    if(target.type==='locker'||target.type==='locker-exit'){
      audio.play('locker');
      if(!res.hiding && Number.isFinite(res.exitX) && Number.isFinite(res.exitZ)){
        localPos.set(res.exitX, 1.65, res.exitZ);
      } else if(res.hiding){
        const l = activeLockers.find(x => x.id === (res.lockerId || target.id));
        if(l) localPos.set(l.x, 1.65, l.z);
        keys.clear();
      }
      lastSend = performance.now();
      toast(res.hiding?'Ви сховалися в шафу. Дихайте тихо.':'Ви вийшли з шафи.');
    }else if(target.id==='broadcast'){
      lastSend = performance.now();
    }else if(target.type==='fuse'){
      audio.play('pickup');
      toast(`Запобіжник 380В встановлено в інвентар (${state?.fuses?.length || 1}/2).`);
    }else if(target.type==='power'){
      audio.play('power');
      toast('Живлення відновлено! Запобіжники 380В замкнули ланцюг.');
    }else if(target.type==='revive'){
      audio.play('pickup');toast('Напарник знову на зв’язку.');
    }
  }catch(e){toast(e.message);audio.play('error');}
  finally{handlingAction=false;}
}

// Quick Radio Callout
function sendRadio(phraseIdx=0){
  const classData = getClassData(selectedClass);
  const cooldown = classData.bonuses.radioCooldown || 3000;
  if(performance.now()-lastRadioTime<cooldown)return;
  lastRadioTime=performance.now();
  $('radio-menu').hidden=true;
  audio.play('radio');
  socket.emit('player:radio',{phrase:phraseIdx},(res)=>{
    if(res?.heard)toast('⚠ ДИКТОР ПОЧУВ РАЦІЮ І ЙДЕ НА ВАС!');
  });
}

document.querySelectorAll('.radio-btn').forEach(btn=>{
  btn.onclick=()=>sendRadio(Number(btn.dataset.phrase));
});

// Controls & Keyboard Listeners
document.addEventListener('keydown',e=>{
  if(e.target instanceof HTMLInputElement||e.target instanceof HTMLSelectElement)return;
  if(['Space','ArrowUp','ArrowDown','ArrowLeft','ArrowRight','Tab'].includes(e.code)&&playing)e.preventDefault();
  if(mode==='arrival' && (e.code==='Space'||e.code==='Escape')){e.preventDefault();finishArrival();return;}
  if(e.code==='Escape'){
    if(!$('radio-menu').hidden){$('radio-menu').hidden=true;return;}
    if(mode==='game')showPause();
    else if(mode==='pause')resume();
    else if(mode==='map')resume();
    else if(mode!=='lobby'&&mode!=='ending')closePanel();
    return;
  }
  if(e.code==='KeyM'){
    if(mode==='map'){resume();return;}
  }
  if(mode!=='game')return;
  keys.add(e.code);
  if(e.repeat)return;

  if(e.code==='KeyF'){flashlightOn=!flashlightOn;audio.play('click');}
  if(e.code==='KeyV'){cameraOn=!cameraOn;audio.play('click');if(cameraOn)toast('Тримайте істоту в центрі кадру. Ліхтар має бути ввімкнений.');}
  if(e.code==='KeyE')interact();
  if(e.code==='KeyM')showMap();
  if(e.code==='KeyQ'){
    $('radio-menu').hidden=!$('radio-menu').hidden;
  }
  if(['Digit1','Digit2','Digit3','Digit4'].includes(e.code)&&!$('radio-menu').hidden){
    sendRadio(Number(e.code.slice(-1))-1);
  }
});

document.addEventListener('keyup',e=>keys.delete(e.code));

// Mouse Look with Spike Clamping (fixes camera jerk bug)
document.addEventListener('mousemove',e=>{
  if(document.pointerLockElement&&mode==='game'){
    const mx = Math.max(-90, Math.min(90, e.movementX));
    const my = Math.max(-90, Math.min(90, e.movementY));
    yaw -= mx * .002 * settings.sensitivity;
    pitch = THREE.MathUtils.clamp(pitch - my * .002 * settings.sensitivity, -1.35, 1.35);
  }
});
document.addEventListener('pointerlockchange',()=>{if(!document.pointerLockElement&&playing&&mode==='game')showPause();});
window.addEventListener('blur',()=>{keys.clear();if(playing&&mode==='game')showPause();});

function updateHud(now){
  $('game-time').textContent=stamp(state.elapsed||0);
  const currentRoom = roomAt(localPos.x,localPos.z);
  $('location').textContent=currentRoom;
  if(mode==='game' && currentRoom !== lastRoom){
    lastRoom = currentRoom;
    showLocationTitle(currentRoom, getRoomSubtitle(currentRoom));
  }

  $('room-tag').textContent=state.creative
    ? (state.players.length>1?`ГРУПА ${state.code} · КРЕАТИВ`:'РЕЖИМ ДОСЛІДЖЕННЯ')
    : (state.players.length>1?`ГРУПА ${state.code} · ${state.players.length}/4`:'ОДИНОЧНИЙ ЗАПИС');

  $('signal').textContent=state.broadcast?'⚠ ТРИВОГА: ЕФІР ВІДКРИТО':threat>.55?'У СИГНАЛІ ХТОСЬ Є':cameraOn?'КАМЕРА / TRACKING':'СИГНАЛ СТАБІЛЬНИЙ';
  $('signal').style.color=state.broadcast||threat>.55?'#d28771':'';

  let objTitle, objDetail;
  if(state.broadcast){
    objTitle='Повертайтеся до фургона';objDetail='Через головний вхід у двір.';
    $('escape-banner').hidden=false;
    $('escape-dist').textContent=Math.round(Math.hypot(extraction.x-localPos.x,extraction.z-localPos.z))+' м до фургона';
  }else{
    $('escape-banner').hidden=true;
    if(localPos.z>6 && state.tapes.length===0 && !(state.fuses||[]).length){objTitle='Увійдіть до телестанції';objDetail='Головний вхід перед вами.';}
    else if(state.tapes.length<3){objTitle='Знайдіть записи · '+state.tapes.length+'/3';objDetail='Архів / Студія 04 / Техсклад';}
    else if((state.fuses||[]).length<2){objTitle='Знайдіть запобіжники · '+(state.fuses||[]).length+'/2';objDetail='Вентвузол / Серверна';}
    else if(!state.power){objTitle='Відновіть живлення';objDetail='Електрощит у генераторній.';}
    else{objTitle='Передайте останній запис';objDetail='Пульт в апаратній.';}
  }
  $('objective').textContent=objTitle;$('objective-detail').textContent=objDetail;
  $('hud').classList.toggle('camera-active',cameraOn);

  const tapeIcons = $('inventory').querySelectorAll('.tape-icon');
  tapeIcons.forEach((el, i) => el.classList.toggle('collected', i < state.tapes.length));
  const fuseIcons = $('inventory').querySelectorAll('.fuse-icon');
  const fuseCount = (state.fuses || []).length;
  fuseIcons.forEach((el, i) => el.classList.toggle('collected', i < fuseCount));

  const me=state.players.find(p=>p.id===myId);
  const san = state.creative ? 100 : (me?.sanity ?? 100);
  $('stamina').style.width=`${stamina}%`;
  $('sanity').style.width=`${san}%`;
  $('sanity').style.background=san>60?'#8cc788':san>30?'#d4b76a':'#d84e3a';

  // The breach is signalled through sound, not a persistent countdown.
  $('countdown-banner').hidden=true;

  $('flash-status').classList.toggle('active',flashlightOn&&!me?.hiding);
  $('camera-status').classList.toggle('active',cameraOn&&!me?.hiding);
  $('camera-frame').hidden=!cameraOn||Boolean(me?.hiding);

  $('team-hud').innerHTML=state.players.filter(p=>p.id!==myId).map(p=>`<div class="${p.down?'down':''}">${escape(p.name)} ${p.hiding?'· В ШАФІ':p.down?'· ПОТРІБНА ДОПОМОГА':''}<i></i></div>`).join('');
  currentInteract=chooseInteraction();
  $('interaction').hidden=!currentInteract||mode!=='game';
  if(currentInteract)$('interaction').innerHTML=`<kbd>E</kbd> ${escape(currentInteract.label)}`;
  if(now>subtitleUntil)$('subtitle').hidden=true;
}

// Main Frame Render Loop
function tick(now){
  requestAnimationFrame(tick);
  if(!renderer||!world)return;
  const dt=Math.min((now-lastFrame)/1000,.05);
  lastFrame=now;
  elapsed+=dt;

  let down=false,hiding=false;
  moving=false;sprinting=false;

  if(playing&&state){
    const me=state.players.find(p=>p.id===myId);
    down=Boolean(me?.down);
    hiding=Boolean(me?.hiding);

    if(mode==='game'&&!down&&!hiding&&!state.paused){
      if(keys.has('ArrowLeft'))yaw+=dt*1.6;if(keys.has('ArrowRight'))yaw-=dt*1.6;
      if(keys.has('ArrowUp'))pitch=Math.min(1.3,pitch+dt);if(keys.has('ArrowDown'))pitch=Math.max(-1.3,pitch-dt);
      const forward=Number(keys.has('KeyW'))-Number(keys.has('KeyS')),side=Number(keys.has('KeyD'))-Number(keys.has('KeyA'));
      moving=Boolean(forward||side);
      sprinting=moving&&(keys.has('ShiftLeft')||keys.has('ShiftRight'))&&(state.creative || stamina>2);
      const classData = getClassData(selectedClass);
      const speedMult = classData.bonuses.speedMultiplier || 1.0;
      const stamRecovery = classData.bonuses.staminaRecovery || 1.0;
      stamina=state.creative ? 100 : THREE.MathUtils.clamp(stamina+(sprinting?-23:14 * stamRecovery)*dt,0,100);
      const sprintSpeed = 4.5 * speedMult;
      const speed=(sprinting?sprintSpeed:2.6)*dt/(forward&&side?Math.SQRT2:1);
      const dx=(-Math.sin(yaw)*forward+Math.cos(yaw)*side)*speed,dz=(-Math.cos(yaw)*forward-Math.sin(yaw)*side)*speed;
      if(isWalkable(localPos.x+dx,localPos.z))localPos.x+=dx;
      if(isWalkable(localPos.x,localPos.z+dz))localPos.z+=dz;
    }

    if(now-lastSend>66){
      socket.emit('player:move',{x:localPos.x,z:localPos.z,yaw,pitch,flashlight:flashlightOn&&!hiding,camera:cameraOn&&!hiding,sprinting});
      lastSend=now;
    }

    // Smooth headbob interpolation (fixes camera jerk when stopping)
    currentBob=THREE.MathUtils.lerp(currentBob,(moving&&!hiding)?(sprinting?.028:.018):0,dt*10);
    footBob+=dt*(moving?(sprinting?13:8):2);
    camera.position.set(localPos.x,down?.55:1.65+Math.sin(footBob)*currentBob,localPos.z);
    camera.rotation.set(pitch,yaw,down?.22:Math.sin(footBob*.5)*currentBob*.12,'YXZ');
    camera.fov=THREE.MathUtils.lerp(camera.fov,cameraOn?57:sprinting?74:68,dt*5);
    camera.updateProjectionMatrix();

    held.visible=cameraOn&&!down&&!hiding;
    held.position.y=Math.sin(footBob)*.008;

    // Flashlight zero-stutter: keep visible=true, modulate intensity only!
    flashlight.visible=true;
    const flashActive = flashlightOn && !down && !hiding;
    flashlight.intensity = flashActive ? 32*(settings.flicker&&threat>.7?.9+Math.sin(now*.04)*.1:1) : 0;

    // Stalker & Nightmare transformation
    if(state.enemy){
      const en=state.enemy;
      const distance=Math.hypot(en.x-localPos.x,en.z-localPos.z);
      threat=THREE.MathUtils.lerp(threat,en.active?Math.max(0,1-distance/16):0,dt*2);

      const activeModel = en.bloody ? bloodyEnemyModel : enemyModel;
      const inactiveModel = en.bloody ? enemyModel : bloodyEnemyModel;
      inactiveModel.visible=false;

      activeModel.visible=en.active&&(cameraOn||distance<3.6||state.broadcast);
      activeModel.position.lerp(new THREE.Vector3(en.x,0,en.z),dt*12);
      activeModel.lookAt(localPos.x,0,localPos.z);
      activeModel.userData.animate?.(elapsed,!en.stunned);
    }

    // Sanity Hallucinations: Phantom Stalker
    const sanityVal=me?.sanity??100;
    if(sanityVal<40&&Math.random()<0.005&&!phantomModel.visible){
      const angle=yaw+(Math.random()>.5?.8:-.8);
      phantomModel.position.set(localPos.x-Math.sin(angle)*6.5,0,localPos.z-Math.cos(angle)*6.5);
      phantomModel.visible=true;
    }
    if(phantomModel.visible){
      phantomModel.lookAt(localPos.x,0,localPos.z);
      phantomModel.userData.animate?.(elapsed,false);
      const d=Math.hypot(phantomModel.position.x-localPos.x,phantomModel.position.z-localPos.z);
      if(d<3||(flashlightOn&&cameraOn)){
        phantomModel.visible=false;audio.play('whisper');
      }
    }

    // Micro-screamer flash on severe psychosis
    if(settings.flicker&&sanityVal<22&&now>nextScreamerTime){
      $('screamer-flash').style.opacity='0.85';
      setTimeout(()=>{$('screamer-flash').style.opacity='0';},100);
      audio.play('screamer');
      nextScreamerTime=now+22000+Math.random()*15000;
    }

    // Remote Players
    for(const mesh of remotePlayers.values()){
      const p=mesh.userData.target;if(!p)continue;
      const walking=Math.hypot(mesh.position.x-p.x,mesh.position.z-p.z)>.04;
      mesh.position.lerp(new THREE.Vector3(p.x,p.down?.2:0,p.z),dt*12);
      mesh.rotation.y=p.yaw+Math.PI;
      mesh.rotation.z=p.down?Math.PI/2:0;
      mesh.userData.animate?.(elapsed,walking&&!p.down);
    }

    $('damage').style.opacity=down?.8:Math.max(0,(threat-.5)*.6);
    if(now-lastHud>120){updateHud(now);lastHud=now;}

    // 3D Spatial Audio calculation
    let monsterSpatial=null;
    if(state?.enemy?.active){
      const en=state.enemy;
      const dx=en.x-localPos.x, dz=en.z-localPos.z;
      const dist=Math.hypot(dx,dz);
      const rightX=Math.cos(yaw), rightZ=-Math.sin(yaw);
      const pan=(dx*rightX+dz*rightZ)/(dist+0.001);
      monsterSpatial={dist,pan:Math.max(-1,Math.min(1,pan))};
    }
    audio.update({
      moving, sprinting, threat, camera: cameraOn&&playing,
      power: Boolean(state.power), broadcast: Boolean(state.broadcast),
      time: elapsed, paused: !playing||mode!=='game',
      stamina, sanity: sanityVal,
      monsterSpatial, hiding,
    });
  }else{
    // The menu and co-op lineup share the playable courtyard.
    const isLobby=mode==='lobby';
    camera.position.set(isLobby?.7:1.7+Math.sin(elapsed*.08)*.15,isLobby?2.1:2.0,isLobby?28.5:28);
    camera.lookAt(isLobby?.7:1.5,1.25,19.2);
    camera.fov=isLobby?55:54;camera.updateProjectionMatrix();
    flashlight.visible=true;flashlight.intensity=0;held.visible=false;
    enemyModel.visible=false;bloodyEnemyModel.visible=false;phantomModel.visible=false;
    lobby?.update(elapsed,['menu','coop','lobby','settings','dossier'].includes(mode));
    $('menu-time').textContent=stamp(elapsed);threat=0;
    audio.update({paused:true});
  }

  if(now>toastUntil)$('toast').hidden=true;

  if(playing)lobby?.update(elapsed,false);
  if(playing&&mode==='arrival'){
    arrivalProgress=Math.min(1,(now-arrivalStarted)/6000);
    exterior?.setArrival(arrivalProgress);
    camera.position.set(-10.4+arrivalProgress*1.4,1.8+arrivalProgress*.25,29.7-arrivalProgress*2.5);
    camera.lookAt(-5.5,1.35,18.5-arrivalProgress*3.5);
    camera.fov=62;camera.updateProjectionMatrix();held.visible=false;flashlight.intensity=0;
    if(arrivalProgress>=1)finishArrival();
  }
  try {
    world.update(elapsed,dt,{...(state||{}),reducedMotion:!settings.flicker,flicker:settings.flicker});
    exterior?.update(elapsed,dt,state||{});
  } catch(error) {
    // Scene decoration must never prevent the next frame from being rendered.
    if(!frameErrorShown){console.error('Scene update failed:',error);toast('Помилка деталі сцени. Гра продовжується.');frameErrorShown=true;}
  }
  effect.uniforms.grain.value=settings.grain?.046:0;
  effect.uniforms.scanlines.value=settings.grain?.055:0;
  effect.uniforms.distortion.value=settings.grain?.08:0;
  effect.uniforms.danger.value=settings.flicker?threat:0;
  effect.uniforms.recording.value=cameraOn&&playing?1:0;
  effect.uniforms.nightmare.value=state?.broadcast?(settings.flicker?.65:.2):0;
  effect.uniforms.insanity.value=playing?Math.max(0,(50-(state?.players?.find(p=>p.id===myId)?.sanity??100))/50):0;
  effect.render(elapsed);
}
requestAnimationFrame(tick);
