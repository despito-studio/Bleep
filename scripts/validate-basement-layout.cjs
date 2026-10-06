const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '..');
const savedPath = path.join(root, 'Assets/Resources/world.json');
let world;
if (process.argv.includes('--saved')) world = JSON.parse(fs.readFileSync(savedPath, 'utf8'));
else {
  // Exercise the authored generator without replacing data beneath a live Editor.
  const saved = fs.readFileSync(savedPath, 'utf8');
  vm.runInNewContext(fs.readFileSync(path.join(__dirname, 'build-office-data.cjs'), 'utf8'), {
    require(name) {
      assert.equal(name, 'fs');
      return {
        readFileSync(file) { assert.equal(file, 'Assets/Resources/world.json'); return saved; },
        writeFileSync(file, json) { assert.equal(file, 'Assets/Resources/world.json'); world = JSON.parse(json); }
      };
    },
    console: { log() {} }
  }, { filename: 'build-office-data.cjs' });
}
assert.ok(world, 'Generator did not produce world data.');
const H = 4.2;
const basement = world.rooms.filter(room => room.floorY === -H && room.mapLabel !== 'HALL BASEMENT STAIRS');
const cells = basement.filter(room => room.mapLabel.startsWith('CELL '));
assert.equal(cells.length, 6, 'Six prison cells are required.');
const area = basement.reduce((sum, room) => sum + (room.maxX-room.minX)*(room.maxZ-room.minZ), 0);
assert.ok(Math.abs(area - 450) < .001, `Basement area ${area} must equal three times the previous 150 m².`);
for (const room of basement) assert.ok(room.minX >= -18 && room.maxX <= 18 && room.minZ >= -39.5 && room.maxZ <= -27);
for (let i=0; i<basement.length; i++) for (let j=i+1; j<basement.length; j++) {
  const a=basement[i], b=basement[j];
  assert.ok(Math.min(a.maxX,b.maxX)-Math.max(a.minX,b.minX) < .001 ||
    Math.min(a.maxZ,b.maxZ)-Math.max(a.minZ,b.minZ) < .001, `${a.name} overlaps ${b.name}.`);
}
const book = world.objects.find(object => object.id === 'prison-book');
assert.equal(book?.type, 'book');
const inRoom = (room, point) => point.x > room.minX && point.x < room.maxX && point.z > room.minZ && point.z < room.maxZ;
const bookCell = cells.find(room => inRoom(room, book));
assert.equal(bookCell?.mapLabel, 'CELL 01');
const adjacency=world.navigationNodes.map(() => []);
for (const edge of world.navigationLinks) {
  assert.ok(adjacency[edge.a] && adjacency[edge.b], 'Invalid navigation link.');
  adjacency[edge.a].push(edge.b);adjacency[edge.b].push(edge.a);
}
function nearest(point, filter=()=>true) {
  let index=-1, best=Infinity;
  world.navigationNodes.forEach((node,i) => {
    if (!filter(node)) return;
    const distance=(point.x-node.x)**2+(point.y-node.y)**2+(point.z-node.z)**2;
    if (distance<best) { best=distance;index=i; }
  });
  return index;
}
const start=nearest(world.spawn), seen=new Set([start]), queue=[start];
for(let at=0;at<queue.length;at++) for(const next of adjacency[queue[at]]) if(!seen.has(next)) {seen.add(next);queue.push(next);}
for (const cell of cells) {
  const target=nearest({x:(cell.minX+cell.maxX)/2,y:-H,z:(cell.minZ+cell.maxZ)/2}, node => Math.abs(node.y+H)<.01 && inRoom(cell,node));
  assert.ok(target>=0 && seen.has(target), `Prison cell has no stair-connected route: ${cell.name}.`);
  const doorway=world.doors.find(door => door.id === 'door-prison-'+Number(cell.mapLabel.slice(5)));
  assert.ok(doorway && !doorway.requires, 'Each cell needs its own accessible door.');
}
assert.ok(seen.has(nearest({x:book.x,y:-H,z:book.z}, node => Math.abs(node.y+H)<.01 && inRoom(bookCell,node))), 'The diary must have a route from the entrance.');
assert.ok(world.navigationNodes.some(node => seen.has(world.navigationNodes.indexOf(node)) && node.y > -H+.3 && node.y < -.3 && node.z >= -49.5 && node.z <= -39.5), 'The basement stair flight is disconnected.');
const basementNodes=world.navigationNodes.filter(node => node.y < -.1).length;
console.log(`BASEMENT LAYOUT PASSED: ${area.toFixed(0)} m², ${cells.length} cells, ${basementNodes} basement/stair nodes; every cell and prison-book connected to the entrance. Saved data unchanged.`);
