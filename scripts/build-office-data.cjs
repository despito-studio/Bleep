/* Source coordinates: Unity reflects X only when building the visual assets. */
const fs = require('fs');
const file = 'Assets/Resources/world.json';
const old = JSON.parse(fs.readFileSync(file, 'utf8'));
// Role copy describes implemented mechanics; light cannot stun the blind enemy.
const vivian = old.classes.find(role => role.id === 'operator');
if (vivian) {
  vivian.perk = 'Стійкий розсудок (на 25% повільніша втрата) та пачка сигарет';
  vivian.description = 'Хронікерка телецентру з платиновим каре, золотими сережками-кільцями, перлинним кольє та сигаретою. Повільніше втрачає розсудок; у її інвентарі є пачка з 12 сигарет.';
}
const pi = Math.PI, H = 4.2;
const rooms = [], doors = [], props = [], objects = [], lockers = [];
let serial = 0;
function room(name,mapLabel,minX,maxX,minZ,maxZ,floorY) {const r={name,mapLabel,minX,maxX,minZ,maxZ,floorY};rooms.push(r);return r;}
function prop(type,x,y,z,rotation=0,w=1,d=1,label='',id='') {const p={id:id||type+'-'+(++serial),type,label:label||type,x,y,z,rotation,w,d};props.push(p);return p;}
function door(id,label,x,y,z,requires=null,rotation=0,w=2.2,openAngle=90) {doors.push({id:'door-'+id,label:'Двері: '+label,x,y:y+1.4,z,requires,rotation,w,openAngle});}
function obj(id,type,label,x,y,z) {objects.push({id,type,label,x,y,z});}
function locker(id,label,x,y,z,ry) {const l={id,label,x,y,z,ry,exitX:x+Math.sin(ry)*1.95,exitY:y,exitZ:z+Math.cos(ry)*1.95};lockers.push(l);prop('locker',x,y,z,ry,1.12,.95,label,id);}
function desk(x,y,z,label,rotation=0) {prop('workstation',x,y,z,rotation,2.3,1.25,label);prop('chair',x-Math.sin(rotation)*1.3,y,z-Math.cos(rotation)*1.3,rotation,.65,.65);}
function shelves(xs,y,z,label) {for(const x of xs)prop('shelf',x,y,z,0,2.1,.6,label);}

// Each floor is a circulation loop around an occupied core, with a second
// cross hall and side galleries beside the stairs. No 60 m axial corridor.
for(let f=0;f<2;f++) {
  const y=f*H, suffix=f?' · 2 ПОВЕРХ':'';
  room('ВХІДНИЙ ХОЛ'+suffix,'HALL ENTRY',-6,6,-7,6,y);
  room('ПОПЕРЕЧНИЙ ХОЛ'+suffix,'HALL FRONT',-18,18,-11,-7,y);
  room('ЗАХІДНА ГАЛЕРЕЯ'+suffix,'HALL WEST',-10,-6,-24,-11,y);
  room('СХІДНА ГАЛЕРЕЯ'+suffix,'HALL EAST',6,10,-24,-11,y);
  room('СЛУЖБОВИЙ ХОЛ'+suffix,'HALL CROSS',-18,18,-28,-24,y);
  room('СХОДОВИЙ ХОЛ'+suffix,'HALL STAIRS',-3,3,-51,-28,y);
  room('ЗАХІДНИЙ СЛУЖБОВИЙ ПРОХІД'+suffix,'HALL SERVICE W',-8,-3,-51,-28,y);
  room('СХІДНИЙ СЛУЖБОВИЙ ПРОХІД'+suffix,'HALL SERVICE E',3,8,-51,-28,y);
  room('ЗАДНІЙ ХОЛ'+suffix,'HALL REAR',-18,18,-54,-51,y);
  const specs=f?[
    ['director','КАБІНЕТ ДИРЕКТОРА','DIRECTOR',-18,-6,-7,6,-6,-.5,null],
    ['apparatus','АПАРАТНА','CONTROL',6,18,-7,6,6,-.5,null],
    ['editorial','РЕДАКЦІЯ','EDITORIAL',-6,6,-24,-11,0,-11,null,pi/2],
    ['meeting','ПЕРЕГОВОРНА','MEETING',-18,-10,-17,-11,-10,-14,null],
    ['dispatch','ДИСПЕТЧЕРСЬКА','RADIO',-18,-10,-24,-17,-10,-20.5,null],
    ['studio','СТУДІЯ 04','STUDIO',10,18,-17,-11,10,-14,null],
    ['server','СЕРВЕРНА','SERVERS',10,18,-24,-17,10,-20.5,'keycard-engineer'],
    ['editing','МОНТАЖНА','EDIT SUITE',-18,-8,-39,-28,-8,-33.5,null],
    ['library','ФОНОТЕКА','TAPE LIBRARY',-18,-8,-51,-39,-8,-45,null],
    ['vent','ВЕНТВУЗОЛ','VENT',8,18,-39,-28,8,-33.5,'keycard-guard'],
    ['greenroom','ГРИМЕРНА ТА ВІДПОЧИНОК','GREEN ROOM',8,18,-51,-39,8,-45,null]
  ]:[
    ['reception','РЕЦЕПЦІЯ','RECEPTION',-18,-6,-7,6,-6,-.5,null],
    ['security','ОХОРОНА','SECURITY',6,18,-7,6,6,-.5,null],
    ['archive','АРХІВ','ARCHIVE',-6,6,-24,-11,0,-11,'keycard-guard',pi/2],
    ['generator','ІНЖЕНЕРНА','ENGINEER',-18,-10,-17,-11,-10,-14,'keycard-engineer'],
    ['tech','МАЙСТЕРНЯ','WORKSHOP',-18,-10,-24,-17,-10,-20.5,'keycard-engineer'],
    ['break','КУХНЯ ТА ЇДАЛЬНЯ','KITCHEN',10,18,-17,-11,10,-14,null],
    ['wc','САНВУЗОЛ','WC',10,18,-24,-17,10,-20.5,null],
    ['storage','СКЛАД РЕКВІЗИТУ','STORAGE',-18,-8,-39,-28,-8,-33.5,null],
    ['mail','ПОШТА Й КОПІЮВАННЯ','MAIL / PRINT',-18,-8,-51,-39,-8,-45,null],
    ['staff','КІМНАТА ПЕРСОНАЛУ','STAFF ROOM',8,18,-39,-28,8,-33.5,null],
    ['maintenance','ГОСПОДАРСЬКА','MAINTENANCE',8,18,-51,-39,8,-45,null]
  ];
  for(const [id,name,map,a,b,c,d,x,z,key,rot=0] of specs){room(name,map,a,b,c,d,y);door(id,name,x,y,z,key,rot,2.2,x>0?-90:90);}
  door(f?'editorial-rear':'archive-rear',f?'РЕДАКЦІЯ · СЛУЖБОВИЙ ВХІД':'АРХІВ · СЛУЖБОВИЙ ВХІД',0,y,-24,f?null:'keycard-guard',pi/2,2.4,90);
}
room('ПЕРЕДНІЙ ДВІР','FRONT YARD',-30,30,6,38,0);room('ЗАДНІЙ ДВІР · ГЕНЕРАТОР','GENERATOR',-30,30,-76,-54,0);
room('СЛУЖБОВИЙ ПРОХІД','PATH',-30,-18,-54,6,0);room('БОКОВИЙ ПРОХІД','PATH',18,30,-54,6,0);
door('front','Головний вхід',0,0,6,'key-main',pi/2,2.8,-90);door('rear','Вихід у задній двір',0,0,-54,'key-main',pi/2,2.4,90);
room('ПІДВАЛ · СЛУЖБОВИЙ ВХІД','HALL BASEMENT ENTRY',-1.8,1.8,-51,-49.5,0);
door('basement','Підвал під сходами',0,0,-51,'key-basement',pi/2,2.0,90);
room('ПІДВАЛ · ЕЛЕКТРОЩИТОВА','BASEMENT',-6,6,-39.5,-27,-H);
// Retain the electrical room and stair entrance, extending each side by 12 m.
// The occupied basement grows from 150 to 450 m²; galleries keep the cells
// reachable without routing through furniture or an adjacent locked cell.
room('ПІДВАЛ · СХОДОВИЙ СПУСК','HALL BASEMENT STAIRS',-1.6,1.6,-49.5,-39.5,-H);
room('ПІДВАЛ · ЗАХІДНА ГАЛЕРЕЯ','HALL BASEMENT WEST',-11,-6,-39.5,-27,-H);
room('ПІДВАЛ · СХІДНА ГАЛЕРЕЯ','HALL BASEMENT EAST',6,11,-39.5,-27,-H);
door('basement-west','Західні камери',-6,-H,-35.5,null,0,2.2,90);
door('basement-east','Східні камери',6,-H,-35.5,null,0,2.2,-90);
for(const side of [-1,1])for(let i=0;i<3;i++) {
  const cell=i+1+(side>0?3:0),maxZ=-27-i*12.5/3,minZ=-27-(i+1)*12.5/3;
  const name='ПІДВАЛ · КАМЕРА '+String(cell).padStart(2,'0');
  room(name,'CELL '+String(cell).padStart(2,'0'),side<0?-18:11,side<0?-11:18,minZ,maxZ,-H);
  door('prison-'+cell,name,side*11,-H,(minZ+maxZ)/2,null,0,2.0,side<0?90:-90);
  prop('prisonBed',side*15,-H,maxZ-1.45,0,2.4,.95,'Ліжко · Камера '+cell,'prison-bed-'+cell);
  prop('prisonSanitary',side*16.75,-H,minZ+.85,side<0?pi/2:-pi/2,.75,1.05,'Санітарний куток · Камера '+cell,'prison-sanitary-'+cell);
}

// Quest IDs remain stable; locations now follow the working rooms rather than the old rows.
obj('tape-1','tape','Касета 01 · Нічна зміна',0,.865,-21);
obj('tape-2','tape','Касета 02 · Зайвий голос',14,5.07,-14.5);
obj('tape-3','tape','Касета 03 · Останній ефір',-14,5.065,-20.5);
obj('fuse-1','fuse','Запобіжник 380В · Інженерна',-14,.855,-14.5);
obj('fuse-2','fuse','Запобіжник 380В · Серверна',14,5.055,-20.5);
obj('power','power','Електрощит генератора · задній двір',-11,.95,-65.985);
obj('broadcast','broadcast','Передати запис · Апаратна, 2 поверх',11,5.4,-2);
obj('exit','exit','Сісти у фургон · покинути станцію',-8.1,1.2,26);
obj('keycard-guard','keycard','Картка доступу · Охорона',11,.83,-2);
obj('keycard-engineer','keycard','Картка головного інженера · Рецепція',-11,.83,-2.4);
obj('fuel-canister','canister','Каністра з паливом · задній двір',-14,.7,-62);
obj('pressure-valve','valve','Вентиль тиску · Вентвузол, 2 поверх',17.66,5.5,-35);
obj('ladder','ladder','Складна драбина · біля фургона',-8.35,0,27);
obj('key-main','keycard','Головна картка доступу · Кабінет директора',-11.22,4.72,-1.37);
obj('key-basement','key','Ключ від підвалу · Кімната персоналу',13.3,.52,-35.5);
obj('breaker','breaker','Рубильник освітлення · Підвал',0,-H+1.4,-27.2);
obj('prison-book','book','Щоденник нічного наглядача · Камера 01',-15,-H+.595,-28.45);
locker('locker-1','Шафа в архіві',-4.8,0,-21.5,pi/2);
locker('locker-2','Шафа в інженерній',-16.9,0,-12.4,pi/2);
locker('locker-3','Шафа в серверній',16.9,H,-22.5,-pi/2);
locker('locker-4','Шафа охорони',16.9,0,1,-pi/2);
locker('locker-5','Шафа майстерні',-16.9,0,-22.5,pi/2);
locker('locker-6','Шафа диспетчерської',-16.9,H,-22.5,pi/2);
locker('locker-7','Шафа редакції',-4.8,H,-20.2,pi/2);
locker('locker-8','Шафа у студії',16.9,H,-12.4,-pi/2);

// Arrival: a real reception desk, visitor seating, coat storage and security station.
prop('archivistDesk',-11,0,-2,0,2.6,1.35,'Стійка рецепції');prop('chair',-11,0,-3.5,0,.65,.65);
prop('table',11,0,-2,0,2.4,1.3,'Стіл охорони');prop('chair',11,0,-3.5,0,.65,.65);
for(const x of [-15,-11])prop('sofa',x,0,3.9,pi,2.7,.95,'Диван для відвідувачів');
prop('coffeeTable',-13,0,1.7,0,2.1,.9,'Журнальний столик');prop('coatRack',-16.7,0,-4.5,0,1.3,.55);
prop('cardCatalog',-16.8,0,-1.5,pi/2,1.5,.6);prop('officePlant',-7.2,0,4.5,0,.7,.7);
prop('monitorWall',14.5,0,-5.9,0,3,.7);prop('cardCatalog',16.4,0,-3.8,-pi/2,1.5,.6);
prop('corkboard',8.2,0,-6.82,0,2.4,.1);prop('bench',10.5,0,4.3,pi,2.4,.65);
// The occupied central archive interrupts the old straight sightline.
for(const x of [-3,0,3])for(const z of [-14,-17])prop('shelf',x,0,z,0,2.2,.6,'Архівні стелажі');
prop('archivistDesk',0,0,-21,0,2.6,1.35,'Стіл архіваріуса');prop('chair',0,0,-22.5,0,.65,.65);
prop('cardCatalog',4.85,0,-21,-pi/2,1.7,.65);prop('paperCart',4.8,0,-12.7,0,.8,.7);
// Compact technical rooms make objects reachable from the side gallery.
prop('table',-14,0,-14.5,0,2.4,1.3,'Верстак електрика');prop('toolboard',-14,0,-16.78,0,2.5,.1);
prop('fusePanel',-12.2,0,-11.22,pi,.7,.25);prop('shelf',-11.9,0,-16.4,0,1.8,.55,'Запчастини');
prop('table',-14,0,-20.2,0,2.6,1.3,'Ремонтний верстак');prop('toolboard',-14,0,-17.18,pi,2.5,.1);
prop('shelf',-11.9,0,-23.3,0,1.8,.55,'Інструменти');prop('cableSpool',-16.5,0,-18.3,0,1,.95);
// Kitchen and two usable sanitary cubicles, with clear washbasin access.
prop('kitchen',14.2,0,-11.6,pi,5.6,.8,'Кухонна стійка');prop('fridge',16.9,0,-13.3,-pi/2,.9,.8);
prop('table',14.2,0,-15.1,0,2.2,1.2,'Обідній стіл');prop('chair',12.6,0,-15.1,pi/2,.65,.65);prop('chair',15.8,0,-15.1,-pi/2,.65,.65);
for(const x of [13.2,16.1])prop('toilet',x,0,-22.2,0,2.7,3.1,'Туалетна кабіна');
for(const x of [12.1,14.2])prop('sink',x,0,-17.55,pi,.85,.6);prop('mirror',13.1,0,-17.18,pi,3,.08);
// Rear service rooms: stock, print/mail, staff lounge and janitor supplies.
for(const x of [-16,-13,-10])for(const z of [-29,-38])prop('shelf',x,0,z,0,2.1,.6,'Реквізит');
for(const x of [-15,-11])for(const z of [-32,-36])prop('crate',x,0,z,0,1.1,1.1,'Реквізит у ящиках');
prop('copier',-15.8,0,-41.2,0,1.4,1.1);prop('paperCart',-13.4,0,-41.2,0,.9,.75);desk(-13.3,0,-47,'Стіл поштового відділу');
shelves([-16,-12,-9.5],0,-50.2,'Поштова шафа');prop('cardCatalog',-17.3,0,-45.5,pi/2,1.5,.6);
prop('sofa',12,0,-37.7,0,3.2,.95);prop('sofa',16.5,0,-34.8,-pi/2,2.6,.95);prop('coffeeTable',13.3,0,-35.5,0,2,.9);
prop('kitchen',13.2,0,-28.7,pi,3.6,.7);prop('officePlant',16.8,0,-30,0,.7,.7);
for(const z of [-40.3,-49.9])shelves([10,13,16],0,z,'Господарський інвентар');prop('sink',17.1,0,-46,-pi/2,.85,.6);prop('cleaningCart',12,0,-46,0,1.2,.75);
// Upstairs: management and control at the front, six inhabited editing desks in the core.
desk(-12,H,-1.9,'Кабінет директора');prop('sofa',-14.5,H,3.8,pi,2.8,.95);prop('coffeeTable',-14.5,H,1.8,0,1.8,.8);
shelves([-16,-12,-8],H,-6.15,'Документи директора');prop('officePlant',-7.2,H,4.5,0,.7,.7);
prop('mixingConsole',11,H,-2,0,2.6,1.4,'Пульт прямого ефіру');prop('chair',11,H,-3.6,0,.65,.65);
for(const x of [8,11,14,16.5])prop('monitorWall',x,H,-6.1,0,2.2,.7);desk(15,H,2,'Оператор апаратної');
for(const x of [-3,3])for(const z of [-14,-18,-21.7])desk(x,H,z,'Робочий стіл редактора');
prop('copier',4.8,H,-12.3,-pi/2,1.1,.8);prop('corkboard',3.1,H,-11.18,pi,3,.1);
prop('meetingTable',-14,H,-14,0,4.2,1.5);for(const x of [-15.6,-14,-12.4])for(const z of [-12.65,-15.35])prop('chair',x,H,z,z<-14?0:pi,.65,.65);
prop('whiteboard',-14.5,H,-16.82,0,3.6,.1);
prop('table',-14,H,-20.5,0,2.4,1.3,'Стіл диспетчера');prop('chair',-14,H,-21.9,0,.65,.65);
prop('monitorWall',-14,H,-17.6,pi,2.7,.7);prop('radioCabinet',-11.4,H,-23.1,0,1.1,.55);
prop('anchorDesk',14,H,-14.5,0,3,1.3,'Стіл Студії 04');prop('chair',14,H,-15.85,0,.65,.65);
prop('chromakeyBackdrop',14,H,-16.65,0,5.5,.25);prop('camera',11.7,H,-12.2,pi,1,1);
prop('table',14,H,-20.5,0,2.4,1.3,'Стіл серверної');for(const x of [12,14,16])prop('serverRack',x,H,-17.65,pi,1,.8);prop('serverRack',11.4,H,-23.1,0,1,.8);
for(const x of [-15.5,-11])for(const z of [-30.5,-35.7])desk(x,H,z,'Монтажне робоче місце');prop('corkboard',-13,H,-38.82,0,3,.1);
for(const x of [-16,-13,-10])for(const z of [-40.3,-43.5,-49.7])prop('shelf',x,H,z,0,2.1,.65,'Фонотека');prop('paperCart',-16,H,-46.8,0,.9,.7);
for(const x of [10,13,16])prop('airDuct',x,H,-30,0,1.3,1.3);prop('fan',15.5,H,-38.78,0,1.2,.15);
prop('table',11.3,H,-36.8,0,2.3,1.2,'Сервісний стіл вентиляції');
prop('sofa',13,H,-49.5,0,3.1,.95);prop('coffeeTable',13,H,-47.6,0,1.9,.9);prop('wardrobe',16.9,H,-43.2,-pi/2,2.2,.65);prop('mirror',14.3,H,-39.18,pi,2.4,.08);prop('kitchen',10.5,H,-40,pi,3.2,.65);
// Basement electrical switchgear props
prop('cableSpool',-3.5,-H,-30,0,1.2,1.2,'Кабельний барабан · Підвал');
prop('table',3.5,-H,-32,0,2.4,1.3,'Верстак чергового електрика');
prop('shelf',-4.8,-H,-34,0,2.2,.6,'Стелаж з інструментами');
prop('shelf',-4.8,-H,-37,0,2.2,.6,'Стелаж із запчастинами');
// Wayfinding, waiting niches and shared office equipment make circulation useful.
for(let f=0;f<2;f++){const y=f*H;prop('directory',-3.8,y,-10.82,0,2.2,.12);prop('officePlant',4.8,y,3.8,0,.75,.75);prop('bench',4.9,y,-4.2,-pi/2,2.3,.65);
  prop('bench',-6.8,y,-31,pi/2,2.4,.65);prop('vendingMachine',6.75,y,-30.7,-pi/2,1.1,.8);prop('officePlant',-6.9,y,-37.2,0,.75,.75);
  prop('corkboard',-5.82,y,-18,pi/2,2.8,.1);prop('corkboard',5.82,y,-18,-pi/2,2.8,.1);
}
// Front: arrival, parking, pedestrian route, benches, planters and lights.
prop('van',-10,0,26,0,2.7,5.3,'Фургон знімальної групи');
for(const x of [10,15,20])prop('abandonedCar',x,0,28,pi,2.1,4.2,'Службовий автомобіль');
for(const x of [-8,8])for(const z of [11,17])prop('planter',x,0,z,0,2,1.2,'Клумба перед входом');
for(const x of [-15,15])prop('bench',x,0,13,0,2.2,.65,'Лавка у дворі');
for(const x of [-24,24])for(const z of [12,32,-62,-72])prop('yardLamp',x,0,z,0,.25,.25);
prop('trashBin',-15,0,10,0,.5,.5);prop('trashBin',15,0,10,0,.5,.5);
prop('gateBarrier',0,0,35.5,0,6,.45,'Шлагбаум');
// Rear: generator under a weather shelter, fuel store, loading area.
prop('dieselGenerator',-12,0,-67,0,3.2,1.8,'Генератор ДГ-200 · задній двір');
prop('generatorShelter',-12,0,-67,0,8,7,'Навіс генератора');
prop('fuelStore',-15,0,-61.5,0,3,1.5,'Паливо і сервісний інвентар');
for(const x of [11,14,17])prop('crate',x,0,-62,0,1.1,1.1,'Вантажний майданчик');
prop('dumpster',21,0,-70,0,2.4,1.7,'Сервісний контейнер');
prop('cableSpool',8,0,-70,0,1.2,1.2);
// Board backs meet the actual wall face. The source plan used a generic .18 m
// offset for decorations with different depths, leaving paper panels suspended.
const mountedDepth={corkboard:.09,toolboard:.09,directory:.09,mirror:.08,whiteboard:.06,fusePanel:.22,fan:.10};
for(const p of props)if(mountedDepth[p.type]) {
  const half=mountedDepth[p.type]/2;
  const nx=Math.sin(p.rotation),nz=Math.cos(p.rotation);
  const candidates=[];
  for(const r of rooms.filter(r=>r.floorY===p.y&&r.minX>=-18&&r.maxX<=18&&r.minZ>=-54&&r.maxZ<=6&&!r.mapLabel.startsWith('HALL'))){
    if(Math.abs(nx)>.99&&p.z>=r.minZ&&p.z<=r.maxZ)for(const x of [r.minX,r.maxX])candidates.push({distance:Math.abs(x-p.x),x:x+nx*(.11+half),z:p.z});
    if(Math.abs(nz)>.99&&p.x>=r.minX&&p.x<=r.maxX)for(const z of [r.minZ,r.maxZ])candidates.push({distance:Math.abs(z-p.z),x:p.x,z:z+nz*(.11+half)});
  }
  candidates.sort((a,b)=>a.distance-b.distance);
  if(candidates[0]&&candidates[0].distance<.6){p.x=candidates[0].x;p.z=candidates[0].z;}
}
// Portable navigation grid uses the same rectangular zones, door apertures and
// furniture footprints as this authored plan. The flight supplies intermediate Y.
const stairs=[
  {minX:-1.8,maxX:1.8,minZ:-51,maxZ:-39,lowerY:0,upperY:H,steps:24},
  {minX:-1.6,maxX:1.6,minZ:-49.5,maxZ:-39.5,lowerY:-H,upperY:0,steps:20}
];
const circulation=r=>r.mapLabel.startsWith('HALL');
const inside=(r,x,z,pad=0)=>x>=r.minX+pad&&x<=r.maxX-pad&&z>=r.minZ+pad&&z<=r.maxZ-pad;
const interior=(x,z)=>x>-18&&x<18&&z>-54&&z<6;
let navigationNodes=[], navigationLinks=[];
const navInfo=[], nodeBuckets=new Map();
function inFurniture(x,y,z){
  return props.some(p=>{
    if(Math.abs(p.y-y)>.2||['corkboard','whiteboard','mirror','toolboard','directory','fan'].includes(p.type))return false;
    const dx=x-p.x,dz=z-p.z,c=Math.cos(p.rotation),s=Math.sin(p.rotation);
    // A conservative capsule allowance leaves the space around desks usable.
    const px=dx*c-dz*s,pz=dx*s+dz*c;
    if(p.type==='generatorShelter') {
      // The roof is above the player's body. Its floor is walkable; only the
      // real weather wall and four support posts obstruct a route to the controls.
      if(Math.abs(pz+p.d/2)<.07+.28&&Math.abs(px)<p.w/2+.28)return true;
      return Math.abs(Math.abs(px)-(p.w/2-.2))<.06+.28&&Math.abs(Math.abs(pz)-(p.d/2-.2))<.06+.28;
    }
    return Math.abs(px)<p.w/2+.28&&Math.abs(pz)<p.d/2+.28;
  });
}
for(let f=-1;f<2;f++)for(let ix=-29;ix<30;ix++)for(let iz=-75;iz<38;iz++){
  const x=ix+.5,z=iz+.5;
  if(f>0&&!interior(x,z))continue;
  if(f<0&&!inside({minX:-18,maxX:18,minZ:-51,maxZ:-27},x,z))continue;
  const stair=stairs.find(s=>inside(s,x,z)&&(Math.abs(s.lowerY-f*H)<.1||Math.abs(s.upperY-f*H)<.1));
  if(f>=-1&&stair&&Math.abs(stair.upperY-f*H)<.1)continue; // real opening in the upper slab
  const y=stair?stair.lowerY+(stair.upperY-stair.lowerY)*((stair.maxZ-z)/(stair.maxZ-stair.minZ)):f*H;
  if(!stair&&inFurniture(x,y,z))continue;
  const r=rooms.find(r=>Math.abs(r.floorY-f*H)<.1&&inside(r,x,z));
  if(!r)continue;
  const id=navigationNodes.length;navigationNodes.push({x,y,z});navInfo.push({r,stair});
  const key=x+','+z;if(!nodeBuckets.has(key))nodeBuckets.set(key,[]);nodeBuckets.get(key).push(id);
}
// The ground landing lies below the upper flight and above the basement flight.
// Both flights share X/Z, so the ordinary one-node-per-floor pass selects the
// upper stair here. Preserve the separate landing at y=0 to connect the basement
// door to its first tread; there is over 3.8 m of physical head clearance.
for(let ix=-1;ix<=0;ix++) {
  const x=ix+.5,z=-50.5,y=0;
  const r=rooms.find(r=>r.floorY===0&&inside(r,x,z));
  const id=navigationNodes.length;
  navigationNodes.push({x,y,z});navInfo.push({r,stair:null});
  const key=x+','+z;if(!nodeBuckets.has(key))nodeBuckets.set(key,[]);
  nodeBuckets.get(key).push(id);
}
function aperture(a,b){
  const x=(a.x+b.x)/2,z=(a.z+b.z)/2,y=(a.y+b.y)/2;
  return doors.some(d=>Math.abs(d.y-1.4-y)<.3&&Math.abs((x-d.x)*Math.cos(d.rotation)-(z-d.z)*Math.sin(d.rotation))<.05&&Math.abs((x-d.x)*Math.sin(d.rotation)+(z-d.z)*Math.cos(d.rotation))<d.w/2-.27);
}
for(let i=0;i<navigationNodes.length;i++){
  const a=navigationNodes[i],ai=navInfo[i];
  for(const [dx,dz] of [[1,0],[0,1]])for(const j of nodeBuckets.get((a.x+dx)+','+(a.z+dz))||[]){
    const b=navigationNodes[j],bi=navInfo[j];
    // The basement flight rises .42 m per 1 m grid step; the upper flight
    // rises .35 m. Use the authored slope instead of cutting its route apart.
    const flight=ai.stair||bi.stair;
    const maxRise=flight?(flight.upperY-flight.lowerY)/(flight.maxZ-flight.minZ)+.02:.36;
    if(Math.abs(a.y-b.y)>maxRise)continue;
    if(ai.stair||bi.stair){
      const s=ai.stair||bi.stair,other=ai.stair?b:a;
      if(!(ai.stair&&bi.stair)&&other.z>s.minZ&&other.z<s.maxZ)continue;
    }else if(ai.r!==bi.r){
      const sameCirculation=circulation(ai.r)&&circulation(bi.r);
      const bothOutside=!interior(a.x,a.z)&&!interior(b.x,b.z);
      if(!sameCirculation&&!bothOutside&&!aperture(a,b))continue;
    }
    navigationLinks.push({a:i,b:j});
  }
}
// Tiny pockets behind furniture are not destinations. Retain the connected
// campus component so nearest-node selection never targets an isolated cell.
const adjacency=Array.from({length:navigationNodes.length},()=>[]);
for(const edge of navigationLinks){adjacency[edge.a].push(edge.b);adjacency[edge.b].push(edge.a);}
const visited=new Set();let largest=[];
for(let start=0;start<navigationNodes.length;start++)if(!visited.has(start)){
  const component=[],queue=[start];visited.add(start);
  for(let at=0;at<queue.length;at++){const i=queue[at];component.push(i);for(const j of adjacency[i])if(!visited.has(j)){visited.add(j);queue.push(j);}}
  if(component.length>largest.length)largest=component;
}
const keep=new Set(largest),remap=new Map();navigationNodes=navigationNodes.filter((node,index)=>{if(!keep.has(index))return false;remap.set(index,remap.size);return true;});
navigationLinks=navigationLinks.filter(edge=>keep.has(edge.a)&&keep.has(edge.b)).map(edge=>({a:remap.get(edge.a),b:remap.get(edge.b)}));
const exteriorObstacles=props.filter(p=>p.y===0&&(p.z>6||p.z<-54)&&['van','abandonedCar','planter','bench','dieselGenerator','fuelStore','crate','dumpster'].includes(p.type)).map(p=>({id:p.id,minX:p.x-p.w/2,maxX:p.x+p.w/2,minZ:p.z-p.d/2,maxZ:p.z+p.d/2}));
const data={spawn:{x:0,y:0,z:19},courtyard:{minX:-30,maxX:30,minZ:-76,maxZ:38},extraction:{x:-8.1,y:1.2,z:26},stairs,exteriorObstacles,rooms,objects,doors,lockers,props,navigationNodes,navigationLinks,classes:old.classes};
const outputFile = typeof process !== 'undefined' && process.argv[2] ? process.argv[2] : file;
fs.writeFileSync(outputFile,JSON.stringify(data,null,2)+'\n');
console.log(`Branched office: ${rooms.length} zones, ${doors.length} doors, ${lockers.length} lockers, ${props.length} props, ${navigationNodes.length} navigation nodes, ${navigationLinks.length} links.`);

