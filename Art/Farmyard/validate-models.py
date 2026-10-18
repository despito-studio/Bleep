"""Validate bundled baked meshes independently of a Unity import or GPU."""
from pathlib import Path
import math, struct, hashlib, json
folder=Path(__file__).parent/'../../Assets/Resources/Farmyard'
report=[]
for name in ('goat','rooster'):
 blob=(folder/f'{name}.bytes').read_bytes();at=0
 def take(fmt):
  global at
  values=struct.unpack_from('<'+fmt,blob,at);at+=struct.calcsize('<'+fmt);return values
 magic,vertices,indices=take('iii');assert magic==0x44414631
 assert 0<vertices<2000 and 0<indices<10000 and indices%3==0
 triangle=take(f'{indices}i');assert all(0<=i<vertices for i in triangle)
 uv=take(f'{vertices*2}f');assert all(math.isfinite(x) for x in uv)
 clips=take('i')[0];assert clips==3
 names=[];bounds=[]
 for _ in range(clips):
  size=take('i')[0];clip=blob[at:at+size].decode('utf8');at+=size
  count,fps=take('if');assert count==30 and fps==24
  frames=[]
  for frame in range(count):
   positions=take(f'{vertices*3}f');normals=take(f'{vertices*3}f')
   assert all(math.isfinite(x) and abs(x)<5 for x in positions)
   assert all(math.isfinite(x) and abs(x)<=1.01 for x in normals)
   frames.append(positions)
  movement=max(abs(a-b) for a,b in zip(frames[0],frames[12]));assert movement>.0001,(name,clip,movement)
  if clip=='idle':
   actual_height=max(frames[0][1::3])-min(frames[0][1::3]);expected=1.22 if name=='goat' else .47
   assert abs(actual_height-expected)<.0001,(name,actual_height,expected)
   assert abs(min(frames[0][1::3]))<.0001,(name,'hooves must remain grounded')
  names.append(clip);bounds.append({'clip':clip,'movement':round(movement,4),'lowestY':round(min(f[i] for f in frames for i in range(1,len(f),3)),4)})
 assert set(names)=={'idle','walk','attack'} and at==len(blob)
 report.append({'model':name,'vertices':vertices,'triangles':indices//3,'bytes':len(blob),'sha256':hashlib.sha256(blob).hexdigest(),'clips':bounds})
print(json.dumps(report,indent=2))
