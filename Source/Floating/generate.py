"""Original paper-cut stationery illustrations. Regenerate with Pillow and NumPy.

All output uses new paths. The previously supplied floral PNGs are retained.
"""
from pathlib import Path
import json
import math
import random
import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
PAPERS = ROOT / 'Assets/Resources/DaydreamPapers'
ART = ROOT / 'Assets/Daydream/FloatingV2'
for folder in (PAPERS, ART / 'Textures', ROOT / 'docs/floating'):
    folder.mkdir(parents=True, exist_ok=True)
W, H = 1056, 1496
PALETTES = [('#e3f2f7', '#75afce', '#adcfcf', '#f2ba9f'),
            ('#f0eaf6', '#a293c6', '#c4bce1', '#ebc87c'),
            ('#e5f1e9', '#83b6a5', '#b8d9cd', '#edb8ad'),
            ('#fbecdf', '#d7a98c', '#edcbb8', '#a5c9d4')]
NAMES = ['云海来信','月亮渡船','乘风气球','星光轨道','漂浮花岛','折纸远航',
         '晴空邮戳','月下灯笼','奶油风筝','云端车票','浮光涟漪','雾蓝山屿',
         '薄荷云笺','半月潮汐','花瓣航线','星屑边框','庭院漂流','纸鸟归途',
         '晚霞邮局','灯火星河','软糖云岛','夜航手记','晨光气泡','月光山谷']

def color(hexcode):
    return tuple(int(hexcode[i:i+2], 16) for i in (1,3,5))

def cloud(d, x, y, size, fill):
    for dx, dy, radius in [(-.60,.10,.32),(-.24,-.08,.43),(.23,-.14,.49),(.64,.14,.30)]:
        d.ellipse((x+(dx-radius)*size,y+(dy-radius*.7)*size,
                   x+(dx+radius)*size,y+(dy+radius*.7)*size),fill=fill)

def star(d,x,y,r,c):
    d.polygon([(x,y-r),(x+r*.23,y-r*.23),(x+r*.65,y),(x+r*.23,y+r*.23),
               (x,y+r),(x-r*.23,y+r*.23),(x-r*.65,y),(x-r*.23,y-r*.23)],fill=c)

def curve(d, points, fill, width=2):
    # Smooth cubic Bezier for flight trails and hanging threads.
    p0,p1,p2,p3=points
    line=[]
    for t in np.linspace(0,1,81):
        line.append(tuple((1-t)**3*p0[k]+3*(1-t)**2*t*p1[k]+3*(1-t)*t*t*p2[k]+t**3*p3[k] for k in (0,1)))
    d.line(line,fill=fill,width=width)

def boat(d,x,y,s,ink,soft):
    d.polygon([(x-s,y),(x+s,y),(x+s*.55,y+s*.38),(x-s*.58,y+s*.38)],fill=ink)
    d.polygon([(x,y-s*.9),(x-s*.7,y-s*.08),(x,y-s*.08)],fill='#fffaf0')
    d.polygon([(x+s*.05,y-s*.9),(x+s*.68,y-s*.08),(x+s*.05,y-s*.08)],fill=soft)
    d.line((x,y-s*.95,x,y),fill=ink,width=3)

def balloon(d,x,y,s,ink,soft):
    d.ellipse((x-s*.63,y-s,x+s*.63,y+s*.45),fill=ink)
    d.ellipse((x-s*.25,y-s,x+s*.25,y+s*.45),fill=soft)
    d.line((x-s*.33,y+s*.33,x-s*.18,y+s*.8),fill=ink,width=3)
    d.line((x+s*.33,y+s*.33,x+s*.18,y+s*.8),fill=ink,width=3)
    d.rounded_rectangle((x-s*.22,y+s*.70,x+s*.22,y+s*.98),radius=6,fill='#cbab84')

def motif(d,k,x,y,s,ink,soft,accent,bg):
    k%=12
    if k==0 or k==6:
        cloud(d,x,y,s,soft)
        d.rounded_rectangle((x-s*.42,y-s*.15,x+s*.46,y+s*.43),radius=6,fill='#fffaf0',outline=ink,width=3)
        d.line((x-s*.4,y-s*.1,x,y+s*.19,x+s*.43,y-s*.1),fill=ink,width=3)
        star(d,x+s*.22,y+s*.10,s*.1,accent)
    elif k==1:
        d.ellipse((x-s*.72,y-s*1.15,x+s*.38,y-s*.05),fill=accent)
        d.ellipse((x-s*.42,y-s*1.28,x+s*.48,y-s*.38),fill=bg)
        boat(d,x+s*.1,y+s*.2,s*.75,ink,soft)
    elif k==2 or k==8:
        balloon(d,x,y,s*.82,ink,accent)
        cloud(d,x+s*.52,y+s*.74,s*.65,soft)
    elif k==3 or k==9:
        d.arc((x-s,y-s*.43,x+s,y+s*.43),8,345,fill=ink,width=3)
        d.ellipse((x-s*.43,y-s*.43,x+s*.43,y+s*.43),fill=soft)
        d.arc((x-s,y-s*.43,x+s,y+s*.43),8,175,fill=ink,width=3)
        star(d,x+s*.83,y-s*.32,s*.22,accent)
    elif k==4 or k==10:
        d.polygon([(x-s,y+s*.1),(x+s,y+s*.1),(x+s*.4,y+s*.6),(x-s*.35,y+s*.72)],fill=ink)
        d.ellipse((x-s,y-s*.12,x+s,y+s*.25),fill=soft)
        for dx,dy in [(-.55,-.36),(.03,-.67),(.58,-.30)]:
            px,py=x+dx*s,y+dy*s
            d.line((px,y,px,py),fill=ink,width=3)
            for a in range(6):
                u=px+math.cos(a*math.tau/6)*s*.12;v=py+math.sin(a*math.tau/6)*s*.12
                d.ellipse((u-s*.09,v-s*.09,u+s*.09,v+s*.09),fill='#fff9e8')
            d.ellipse((px-s*.06,py-s*.06,px+s*.06,py+s*.06),fill=accent)
    elif k==5:
        d.polygon([(x-s,y-s*.28),(x+s,y-s*.6),(x-s*.1,y+s*.5)],fill=soft)
        d.polygon([(x+s,y-s*.6),(x-s*.35,y+s*.02),(x-s*.1,y+s*.5)],fill=ink)
        curve(d,[(x-s*.9,y+s*.2),(x-s*1.5,y+s*.8),(x-s*.2,y+s*1.1),(x-s*.8,y+s*1.6)],ink)
    elif k==7:
        for dx,dy,scale in [(-.6,.15,.55),(.1,-.35,.8),(.7,.4,.4)]:
            px=x+dx*s;py=y+dy*s;r=s*scale
            d.rounded_rectangle((px-r*.35,py-r*.58,px+r*.35,py+r*.42),radius=int(r*.18),fill=accent,outline=ink,width=2)
            d.line((px-r*.18,py+r*.42,px,py+r*.68,px+r*.18,py+r*.42),fill=ink,width=2)
    else:
        for i,c in enumerate([soft,ink,accent]):
            d.polygon([(x-s,y+s*.5),(x-s*.3+i*s*.35,y-s*(.7-i*.15)),(x+s,y+s*.5)],fill=c)
        cloud(d,x,y+s*.55,s*.9,'#fff9ed')

manifest=[]
thumbs=[]
for i,name in enumerate(NAMES):
    bg,ink,soft,accent=PALETTES[(i//6+i%2)%4]
    im=Image.new('RGB',(W,H),bg);d=ImageDraw.Draw(im)
    # Nearly three quarters of the sheet remain a quiet, unobstructed writing surface.
    for y in range(H):
        tint=max(0,1-y/(H*.28))*.24+max(0,(y-H*.83)/(H*.17))*.16
        base=np.array(color(bg));shade=base*(1-tint)+np.array(color(soft))*tint
        d.line((0,y,W,y),fill=tuple(np.uint8(shade)))
    d.rounded_rectangle((98,270,W-98,H-185),radius=28,fill='#fbf5e9')
    if i%3==0:
        for y in range(340,H-210,54):d.line((133,y,W-133,y),fill=color(bg),width=2)
    elif i%3==1:
        for x in range(140,W-135,42):
            for y in range(325,H-215,42): d.ellipse((x,y,x+2,y+2),fill=soft)
    motif(d,i,270,154,115,ink,soft,accent,bg)
    motif(d,i+5,865,1350,76,ink,soft,accent,bg)
    for x,y,r in [(650,105,10),(825,198,16),(930,80,9),(100,1280,12),(235,1400,10)]:star(d,x,y,r,accent)
    curve(d,[(480,192),(660,62),(798,274),(978,177)],ink,2)
    d.line((112,252,945,252),fill=soft,width=2)
    font=ImageFont.truetype('C:/Windows/Fonts/georgia.ttf',18)
    d.text((135,H-151),'DAYDREAM  /  '+f'{i+1:02d}',fill=ink,font=font)
    rng=np.random.default_rng(519+i)
    pixels=np.asarray(im,dtype=np.float32)
    noise=rng.normal(0,.85,(H,W,1))
    im=Image.fromarray(np.uint8(np.clip(pixels+noise,0,255)))
    namefile=f'page_{i+1:02d}.png';im.save(PAPERS/namefile)
    manifest.append(dict(id=f'page_{i+1:02d}',name=name,texture='DaydreamPapers/'+namefile[:-4]))
    thumb=im.resize((176,249),Image.Resampling.LANCZOS);thumbs.append(thumb)
sheet=Image.new('RGB',(6*192,4*282),'#e9e9e8');d=ImageDraw.Draw(sheet)
for i,im in enumerate(thumbs):
    x=(i%6)*192+8;y=(i//6)*282+8;sheet.paste(im,(x,y))
    d.text((x,y+253),f'{i+1:02d}',fill='#3f4f66')
sheet.save(ROOT/'docs/floating/paper-contact-sheet.png')
(ART/'papers.json').write_text(json.dumps({'items':manifest},ensure_ascii=False,indent=2),encoding='utf-8')

for night in (False,True):
    w,h=2048,1024
    sky=Image.new('RGB',(w,h));d=ImageDraw.Draw(sky)
    stops=[(0,'#252f55'),(.5,'#424e7c'),(1,'#121b3b')] if night else [(0,'#d9c8e8'),(.5,'#e8eff4'),(1,'#85bee3')]
    for y in range(h):
        v=1-y/(h-1);idx=0 if v<.5 else 1;t=(v-stops[idx][0])/.5
        rgb=np.array(color(stops[idx][1]))*(1-t)+np.array(color(stops[idx+1][1]))*t
        d.line((0,y,w,y),fill=tuple(np.uint8(rgb)))
    if night:
        rng=random.Random(90)
        for j in range(280):
            x=rng.randrange(w);y=rng.randrange(20,610);r=rng.choice([1,1,2,3])
            d.ellipse((x-r,y-r,x+r,y+r),fill=rng.choice(['#a3b7df','#bfcbe5','#f4dfac']))
    sky.save(ART/'Textures'/('SkyNight.png' if night else 'SkyDay.png'))
print('Generated 24 original 1056 x 1496 papers and two sky textures.')
