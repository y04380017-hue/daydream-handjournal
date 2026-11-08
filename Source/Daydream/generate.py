"""Original layered gouache window illustration, printed stationery and quiet surface textures."""
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont
import math, random
out=Path(__file__).resolve().parents[2]/'Assets/Daydream/Textures'
out.mkdir(exist_ok=True,parents=True)
rng=np.random.default_rng(716); random.seed(716)
def grain(im,noise=.9):
    a=np.asarray(im.convert('RGB'),dtype=float)
    return Image.fromarray(np.uint8(np.clip(a+rng.normal(0,noise,(*a.shape[:2],1)),0,255)))
def write(im,name,noise=.9): grain(im,noise).save(out/(name+'.png'))
def font(size,serif=False):
    return ImageFont.truetype('C:/Windows/Fonts/'+('georgia.ttf' if serif else 'segoeui.ttf'),size)
h,w=1024,1536
y,x=np.mgrid[:h,:w]
def surface(name,base,effect):
    arr=np.clip(np.array(base)+effect[:,:,None],0,255).astype('uint8')
    write(Image.fromarray(arr),name,.65)
wave=np.sin(2*np.pi*y/h*6+1.2*np.sin(2*np.pi*x/w)+.45*np.sin(4*np.pi*y/h))
surface('Maple', [220,193,161],1.9*wave+1.6*np.cos(14*wave)+1.3*np.sin(55*wave)+.7*np.cos(y*2*np.pi/h*184))
y,x=np.mgrid[:1024,:728]
surface('CottonIvory',[241,229,206],rng.normal(0,.8,x.shape)+1.1*np.sin(x*1.51+np.sin(y*.11))+.55*np.cos(y*1.8))
im=Image.new('RGB',(1024,1024),'#faf2e5');d=ImageDraw.Draw(im)
for i in range(0,1024,128):
    d.rectangle((i,0,i+30,1024),fill='#f4eadf')
    d.line((i+36,0,i+36,1024),fill='#eae0d8',width=2)
write(im,'WallLinen')
def fleur(d,x,y,s,col,core,petals=6):
    for i in range(petals):
        a=i*math.tau/petals
        u=x+s*.55*math.cos(a);v=y+s*.55*math.sin(a)
        d.ellipse((u-s*.38,v-s*.38,u+s*.38,v+s*.38),fill=col)
    d.ellipse((x-s*.24,y-s*.24,x+s*.24,y+s*.24),fill=core)
def star(d,x,y,r,c):
    d.polygon([(x,y-r),(x+r*.22,y-r*.22),(x+r*.83,y),(x+r*.22,y+r*.22),(x,y+r),(x-r*.22,y+r*.22),(x-r*.83,y),(x-r*.22,y-r*.22)],fill=c)
# The isolated window plate is seen only through the modeled opening.
w,h=1536,1152
im=Image.new('RGB',(w,h));d=ImageDraw.Draw(im)
for j in range(h):
    t=j/h; d.line((0,j,w,j),fill=tuple(int(a*(1-t)+b*t) for a,b in zip((135,198,228),(246,236,215))))
for cx,cy,rad in [(240,402,88),(920,240,116),(1400,470,100),(580,605,58),(80,122,87)]:
    for k in range(7):
        a=(k-3)*.48;r=rad*(1-.13*abs(k-3))
        u=cx+a*rad;v=cy+.16*rad*abs(k-3)
        d.ellipse((u-r,v-r*.66,u+r,v+r*.66),fill='#fff5df')
for k,(base,amp,c) in enumerate([(737,73,'#bdcfd8'),(805,56,'#abcac4'),(893,70,'#95bfac'),(1003,51,'#a4c596')]):
    pts=[(x,base+amp*math.sin(x/260+k*2)+amp*.39*math.cos(x/113+k))for x in range(0,w+8,8)]
    d.polygon(pts+[(w,h),(0,h)],fill=c)
for i in range(260):
    px=random.randrange(w);py=random.randrange(931,h);s=random.uniform(2,9)*(py-820)/300
    d.line((px,py+s*2.8,px+s*.3,py),fill='#81ac94',width=2)
    fleur(d,px,py,s,random.choice(['#edd6bd','#e3c8cb','#e9ddac']),'#d0af7e',5)
write(im.filter(ImageFilter.GaussianBlur(1.3)),'WindowGarden',.55)
# Paired letterpress prints have the same border and type scale, and different cut-paper motifs.
for typ in (0,1):
    im=Image.new('RGB',(650,800),'#f8f0de');d=ImageDraw.Draw(im)
    d.rectangle((26,26,623,773),outline='#aea990',width=2)
    d.text((53,52),'ATELIER   /   FIELD SERIES',font=font(19),fill='#4d645c')
    d.text((553,52),'0'+str(typ+1),font=font(19),fill='#4d645c')
    d.rectangle((52,110,598,613),fill=['#a8d3cf','#efc8b2'][typ])
    if not typ:
        d.ellipse((300,160,510,370),fill='#ffe59e')
        for off,col in [(0,'#e9d8d2'),(80,'#b4c3d9'),(155,'#7daea1')]:
            pts=[(i,380+off+33*math.sin(i/73+off)) for i in range(52,599,3)]
            d.polygon(pts+[(598,613),(52,613)],fill=col)
        star(d,150,228,37,'#fff8df')
    else:
        for px,py,sz in [(230,300,92),(411,395,71),(307,217,59)]:
            d.line((321,613,px,py),fill='#567e69',width=9)
            d.ellipse((px-25,py+100,px+59,py+135),fill='#74a48c')
            fleur(d,px,py,sz,'#fff0d7','#e5ad57')
    d.text((50,628),['Daylight study','Garden notes'][typ],font=font(55,True),fill='#405b53')
    d.text((53,713),['CLOUDS  /  MORNING LIGHT','PAPER  /  PRESSED FLOWERS'][typ],font=font(16),fill='#60726a')
    write(im,'Print'+str(typ+1))

