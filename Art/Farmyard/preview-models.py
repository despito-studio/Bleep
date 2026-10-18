"""Offline geometry QA. One shared metres-to-pixels scale, including a human reference."""
import json, math
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

root=Path(__file__).parent
canvas=Image.new('RGB',(1180,680),(27,34,42));draw=ImageDraw.Draw(canvas)
font_path=Path('C:/Windows/Fonts/segoeui.ttf')
font=lambda size: ImageFont.truetype(str(font_path),size) if font_path.exists() else ImageFont.load_default()
pixels_per_metre=260
floor=570
draw.text((40,24),'Розмір у грі · однаковий масштаб',fill=(239,243,246),font=font(28))
draw.text((40,67),'Коза збільшена до 1,22 м. Курка — 0,47 м. Людина — 1,75 м.',fill=(169,185,197),font=font(18))
for metres in (0,.5,1,1.5):
 y=floor-metres*pixels_per_metre
 draw.line((53,y,1120,y),fill=(55,68,80),width=1)
 draw.text((9,y-12),f'{metres:g}',fill=(138,155,169),font=font(16))
draw.text((10,126),'м',fill=(138,155,169),font=font(16))

for cx,name,title,height in ((285,'Goat','Коза',1.22),(650,'Rooster','Курка',.47)):
 data=json.loads((root/(name+'-preview.json')).read_text());v=data['vertices'];verts=[v[i:i+3] for i in range(0,len(v),3)];uv=data['uv'];tri=data['indices']
 texture=Image.open(root/'../../Assets/Resources/Farmyard'/f'{name.lower()}.png').convert('RGB')
 # Identical orthographic camera and pixels-per-metre for both meshes.
 points=[(cx+pixels_per_metre*(p[0]*.8-p[2]*.6),floor-pixels_per_metre*p[1]) for p in verts]
 triangles=[]
 for i in range(0,len(tri),3):
  ids=tri[i:i+3];ps=[verts[k] for k in ids];depth=sum(p[0]*.6+p[2]*.8 for p in ps)/3
  u=sum(uv[k*2] for k in ids)/3;t=sum(uv[k*2+1] for k in ids)/3
  color=texture.getpixel((int((u%1)*(texture.width-1)),int((1-t%1)*(texture.height-1))))
  a=[ps[1][k]-ps[0][k] for k in range(3)];b=[ps[2][k]-ps[0][k] for k in range(3)]
  normal=[a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]];length=math.sqrt(sum(x*x for x in normal)) or 1
  light=.6+.4*abs(sum(normal[k]*[.3,.9,.2][k] for k in range(3))/length);color=tuple(int(x*light) for x in color)
  triangles.append((depth,ids,color))
 for _,ids,color in sorted(triangles):draw.polygon([points[k] for k in ids],fill=color)
 top=floor-height*pixels_per_metre
 draw.line((cx-150,top,cx-150,floor),fill=(147,182,201),width=2)
 draw.line((cx-157,top,cx-143,top),fill=(147,182,201),width=2)
 draw.line((cx-157,floor,cx-143,floor),fill=(147,182,201),width=2)
 label=f'{title} · {str(height).replace(".",",")} м'
 box=draw.textbbox((0,0),label,font=font(21));draw.text((cx-(box[2]-box[0])/2,590),label,fill=(231,238,243),font=font(21))

# Neutral silhouette drawn at exactly 1.75m, on the identical ground plane and scale.
cx=965;h=1.75;top=floor-h*pixels_per_metre
color=(110,131,147);edge=(148,167,182)
draw.ellipse((cx-25,top,cx+25,top+50),fill=color)
draw.polygon([(cx-19,top+57),(cx+19,top+57),(cx+50,top+79),(cx+63,top+196),(cx+44,top+201),(cx+28,top+108),(cx+26,top+241),(cx+35,floor),(cx+6,floor),(cx,top+273),(cx-6,floor),(cx-35,floor),(cx-26,top+241),(cx-28,top+108),(cx-44,top+201),(cx-63,top+196),(cx-50,top+79)],fill=color)
draw.line((cx+97,top,cx+97,floor),fill=edge,width=2)
draw.line((cx+90,top,cx+104,top),fill=edge,width=2)
draw.line((cx+90,floor,cx+104,floor),fill=edge,width=2)
draw.text((cx-85,590),'Людина · 1,75 м',fill=(231,238,243),font=font(21))
draw.text((40,637),'Коза у 2,6 раза вища за курку. Розміри відповідають ігровим мешам.',fill=(167,183,196),font=font(18))
canvas.save(root/'models-preview.png')
