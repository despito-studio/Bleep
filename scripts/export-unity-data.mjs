import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { spawn, courtyard, extraction, exteriorObstacles, rooms, objects, doors, lockers } from '../shared/world-data.js';
import { DEFAULT_PROPS } from '../shared/props.js';
import { CHARACTER_CLASSES } from '../shared/classes.js';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const destination = path.join(root, 'DeadAirUnityProject', 'Assets', 'Resources', 'world.json');
const custom = JSON.parse(fs.readFileSync(path.join(root, 'shared', 'custom-map.json'), 'utf8'));
const props = Array.isArray(custom.props) ? custom.props : DEFAULT_PROPS;
const customLockers = props.filter((prop) => prop.type === 'locker').map((prop) => ({
  id: prop.id, x: prop.x, y: 1.1, z: prop.z, ry: prop.rotation || 0,
  exitX: prop.exitX ?? prop.x + Math.sin(prop.rotation || 0) * 1.3,
  exitZ: prop.exitZ ?? prop.z + Math.cos(prop.rotation || 0) * 1.3,
  label: prop.label || 'Шафа',
}));
const data = {
  spawn, courtyard, extraction, exteriorObstacles, rooms, objects,
  doors: Object.values(doors), lockers: customLockers.length ? customLockers : lockers,
  props,
  classes: Object.values(CHARACTER_CLASSES),
};
fs.mkdirSync(path.dirname(destination), { recursive: true });
fs.writeFileSync(destination, `${JSON.stringify(data, null, 2)}\n`);
console.log(`Exported ${data.props.length} props and ${data.objects.length} objectives to ${destination}`);
