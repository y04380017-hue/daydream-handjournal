"""Deterministic original game art. Pillow/numpy; no downloaded or AI imagery.
Run from any cwd. Seed and editable drawing code are retained for reuse.
"""
from pathlib import Path
import json, math, shutil, hashlib, wave
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter

ROOT = Path(__file__).resolve().parents[1]
A = ROOT / 'Assets'
rng = np.random.default_rng(7611)
for f in ['Textures/Tapes','Textures/Normals','Resources/Catalog','Sprites/Stickers','Sprites/Notes','Sprites/Decorations','UI','Data','Audio']:
    (A/f).mkdir(parents=True, exist_ok=True)
FONT = 'C:/Windows/Fonts/georgia.ttf'
def font(n):
    try: return ImageFont.truetype(FONT,n)
    except OSError: return ImageFont.load_default()
def save(im,path):
    p=A/path; p.parent.mkdir(parents=True,exist_ok=True); im.save(p)
def texture(name,base,kind):
    n=1024; y,x=np.mgrid[0:n,0:n]/(n-1)
    grain=rng.normal(0,1.5,(n,n))
    if kind=='wood':
        # Fine longitudinal pores and slow irregular figure, rather than broad
        # computer-generated waves. Periodic coarse noise avoids tile discontinuity.
        coarse=np.asarray(Image.fromarray(rng.integers(0,255,(18,7),dtype='uint8')).resize((n,n),Image.Resampling.BICUBIC),dtype=float)/255-.5
        fiber=np.asarray(Image.fromarray(rng.integers(0,255,(512,24),dtype='uint8')).resize((n,n),Image.Resampling.BICUBIC),dtype=float)/255-.5
        phase=y*210+.13*np.sin(x*2*math.pi)+.12*coarse
        v=1.7*np.sin(phase*math.pi*2)+coarse*7+fiber*12+grain*.7
        v-=3*np.maximum(0,np.sin(phase*math.pi*6))**16
    elif kind=='linen': v=grain+3*np.sin(x*math.pi*512)+3*np.sin(y*math.pi*512)
    elif kind=='leather':
        v=grain*1.2+2*np.sin(x*400+np.sin(y*230))*np.sin(y*490)+2*np.sin(x*60)*np.sin(y*50)
    elif kind=='brass': v=grain*.8+2*np.sin(y*1800)+4*np.sin(x*2*math.pi)
    else: v=grain+1.2*np.sin(x*math.pi*8)*np.cos(y*math.pi*12)
    arr=np.clip(np.array(base)[None,None,:]+v[:,:,None],0,255).astype('uint8')
    arr[-1]=arr[0]; arr[:,-1]=arr[:,0]
    save(Image.fromarray(arr),f'Textures/{name}.png')
    gy,gx=np.gradient(v*.008)
    norm=np.stack((-gx,-gy,np.ones_like(v)),axis=2); norm/=np.linalg.norm(norm,axis=2)[:,:,None]
    nm=np.uint8(np.clip(norm*.5+.5,0,1)*255);nm[-1]=nm[0];nm[:,-1]=nm[:,0]
    save(Image.fromarray(nm),f'Textures/Normals/{name}_Normal.png')

for name,base,kind in [('Walnut',(117,81,54),'wood'),('Paper',(236,223,195),'paper'),('Linen',(200,191,161),'linen'),('Leather',(83,48,38),'leather'),('Wall',(222,214,192),'wall'),('Brass',(166,127,66),'brass')]:texture(name,base,kind)

def leaf(d,x,y,s=1,col=(87,113,82,240),angle=0):
    pts=[(-3,8),(6,2),(12,-10),(2,-7)]
    c,si=math.cos(angle),math.sin(angle)
    pts=[(x+(a*c-b*si)*s,y+(a*si+b*c)*s) for a,b in pts]
    d.polygon(pts,fill=col)
    d.line([pts[0],pts[2]],fill=(172,167,114,200),width=max(1,int(s)))
def sprig(d,x,y,s=1,col=(94,112,79,255)):
    d.line((x,y+36*s,x+6*s,y-35*s),fill=col,width=max(1,int(2*s)))
    for k in range(5):leaf(d,x+(2+k)*s,y+(25-k*13)*s,s,col,(-1 if k%2 else 1)*.7)
def rose(d,x,y,s=1):
    for i in range(14):
        t=i*2.399; rr=(i+1)**.5*2.7*s; px=x+math.cos(t)*rr;py=y+math.sin(t)*rr
        d.ellipse((px-6*s,py-4*s,px+6*s,py+5*s),fill=(168+int(i*2),107+int(i*2),111+int(i*2),245),outline=(133,85,84,230),width=max(1,int(s)))
    leaf(d,x-10*s,y+14*s,1.5*s,angle=-1)
def flower(d,x,y,s=1):
    for i in range(10):
        t=i*math.tau/10;dx,dy=math.cos(t)*12*s,math.sin(t)*12*s
        d.ellipse((x+dx-5*s,y+dy-5*s,x+dx+5*s,y+dy+5*s), fill=(248,238,209,255),outline=(207,184,139,255),width=max(1,int(s)))
    d.ellipse((x-5*s,y-5*s,x+5*s,y+5*s),fill=(183,137,53,255))

TAPES=[('复古玫瑰',(230,202,185)),('植物图鉴',(218,222,193)),('旧报纸',(223,210,173)),('古地图',(213,198,155)),('复古邮票',(219,204,181)),('维多利亚蕾丝',(236,221,199)),('古典乐谱',(231,216,181)),('复古瓷砖',(196,212,201)),('暖色几何拼贴',(225,194,158)),('蓝色月光',(94,123,155)),('秋日枫叶',(225,198,155)),('咖啡手账',(214,190,161))]
catalog=[]
def item(id,name,cat,path,desc,prefab=''):
    product_text={'tape':'将旧日的花纹与柔和的色彩，留在今天的纸边。','sticker':'收藏一枚小小图案，记录日常里值得停留的瞬间。','note':'一角温柔的纸，留给忽然浮现的心事与灵感。','page':'复古花饰围住纸页，为今天留一处安静的空白。','decoration':'一件有旧时光气息的小物，为纸间故事添一笔。'}
    catalog.append(dict(id=id,name=name,category=cat,texture='Catalog/'+id,description=product_text[cat],prefab=prefab,source=path))

for k,(name,base) in enumerate(TAPES):
    id=f'tape_{k+1:02}'; w,h=1024,256
    pix=np.clip(np.array(base)[None,None,:]+rng.normal(0,2,(h,w,1)),0,255).astype('uint8')
    im=Image.fromarray(pix).convert('RGBA');d=ImageDraw.Draw(im)
    ink=(101,80,58,230)
    for j in range(-1,6):
        x=j*205+60;y=128+(j%2)*15
        if k==0:rose(d,x,y,2.3);sprig(d,x+45,y+30,1.4)
        elif k==1:
            sprig(d,x,y,2.3);d.text((x-38,219),'HERBARIUM',font=font(12),fill=ink)
        elif k==2:
            d.text((x-65,25),'THE DAILY',font=font(22),fill=ink)
            for t in range(8):d.text((x-65,65+t*21),'Letters from a quiet afternoon'[:18+(t%3)*2],font=font(11),fill=ink)
        elif k==3:
            for t in range(5):
                pts=[(x-110+q*13,35+t*42+12*math.sin(q*.75+t)) for q in range(19)]
                d.line(pts,fill=(140,120,77,220),width=2)
            d.ellipse((x-18,105,x+18,141),outline=ink,width=2);d.line((x,94,x,153),fill=ink,width=2);d.line((x-28,123,x+28,123),fill=ink,width=2)
        elif k==4:
            d.rectangle((x-70,31,x+70,217),outline=(124,98,72,230),width=3)
            for t in range(15):
                for xx in (x-72,x+72):d.ellipse((xx-3,31+t*13-3,xx+3,31+t*13+3),fill=tuple(base)+(255,))
            d.rectangle((x-56,44,x+56,184),outline=(148,119,89,255),width=2);sprig(d,x,120,1.6);d.text((x-32,190),'POST 24',font=font(15),fill=ink)
        elif k==5:
            for t in range(9):
                xx=x-90+t*23
                d.ellipse((xx-20,22,xx+20,67),outline=(163,141,110,200),width=2)
                d.ellipse((xx-20,189,xx+20,234),outline=(163,141,110,200),width=2)
            for yy in (82,92,164,174):d.line((x-106,yy,x+106,yy),fill=(163,141,110,200),width=2)
            flower(d,x,128,1.2)
        elif k==6:
            for off in (38,146):
                for t in range(5):d.line((x-110,off+t*11,x+110,off+t*11),fill=(101,80,58,200),width=1)
                for t in range(4):
                    xx=x-76+t*46; yy=off+(t%3)*11
                    d.ellipse((xx,yy,xx+13,yy+8),fill=ink);d.line((xx+12,yy+4,xx+12,yy-30),fill=ink,width=2)
        elif k==7:
            for yy in (64,192):
                for xx in (x-45,x+55):
                    d.regular_polygon((xx,yy,48),4,rotation=45,fill=(179,193,174,255),outline=(105,136,126,255),width=3)
                    flower(d,xx,yy,.9)
        elif k==8:
            for t,col in enumerate([(180,114,85,210),(191,151,95,210),(133,151,125,210)]):
                xx=x-80+t*56;d.polygon([(xx,38),(xx+60,60),(xx+40,220),(xx-15,196)],fill=col)
            for yy in range(50,210,17):d.line((x-65,yy,x+70,yy+22),fill=(230,210,173,130),width=2)
        elif k==9:
            d.ellipse((x-32,80,x+32,144),fill=(231,218,174,255));d.ellipse((x-13,65,x+45,128),fill=tuple(base)+(255,))
            for t in range(7):
                xx=x-85+(t*37)%180; yy=28+(t*67)%199
                d.line((xx-5,yy,xx+5,yy),fill=(231,218,174,255),width=2);d.line((xx,yy-5,xx,yy+5),fill=(231,218,174,255),width=2)
        elif k==10:
            for t in range(3):
                xx=x-70+t*65;yy=100+(t%2)*68
                d.regular_polygon((xx,yy,32),5,rotation=17,fill=(164+t*15,105+t*12,57,240));d.line((xx-9,yy+42,xx+8,yy-21),fill=ink,width=2)
        else:
            d.ellipse((x+8,96,x+55,150),outline=(117,82,55,255),width=6)
            d.rounded_rectangle((x-46,78,x+29,166),12,fill=(240,224,192,255),outline=(117,82,55,255),width=3)
            d.ellipse((x-42,76,x+25,96),fill=(102,71,47,255));d.line((x-60,178,x+51,178),fill=ink,width=4)
            d.text((x-55,203),'SLOW MORNINGS',font=font(13),fill=ink)
    arr=np.array(im);arr[:,-1]=arr[:,0];arr[-1]=arr[0]
    pattern=Image.fromarray(arr);save(pattern,f'Textures/Tapes/{id}_pattern.png')
    strip=pattern.copy(); ar=np.array(strip); ar[:,:,3]=231
    for y in range(h):
        edge=min(y,h-1-y)
        if edge<5:ar[y,:,3]=np.minimum(ar[y,:,3],rng.integers(10+edge*35,40+edge*38,w))
    for x in (0,w-1):ar[:,x,3]=100
    save(Image.fromarray(ar),f'Textures/Tapes/{id}_strip.png')
    # Orthographic annulus product thumbnail derived from same pattern, preserving identity.
    yy,xx=np.mgrid[0:512,0:512];cx=256;cy=210;dx=(xx-cx)/1.05;dy=(yy-cy)/.62
    rr=np.sqrt(dx*dx+dy*dy);ang=np.mod(np.arctan2(dy,dx)/math.tau,1)
    tp=np.array(pattern);sx=(ang*1023).astype(int);sy=np.clip(((rr-82)/105*255),0,255).astype(int)
    out=np.zeros((512,512,4),dtype=np.uint8)
    # hanging cylinder side
    side=(abs(dx)<187)&(yy>210)&(yy<305+70*np.sqrt(np.maximum(0,1-(dx/187)**2)))
    out[side]=tp[((yy[side]-210)*2%256).astype(int),np.clip(((xx[side]-60)/395*1023),0,1023).astype(int)]
    out[side,:3]=(out[side,:3]*.72).astype('uint8')
    ring=(rr<187)&(rr>79);out[ring]=tp[sy[ring],sx[ring]]
    core=(rr<79)&(rr>59);out[core]=[180,151,110,255]
    thumb=Image.fromarray(out);td=ImageDraw.Draw(thumb)
    td.rectangle((48,404,464,465),fill=tuple(base)+(235,))
    thumb.alpha_composite(pattern.resize((416,61)),(48,404));save(thumb,f'Resources/Catalog/{id}.png')
    item(id,name,'tape',f'Textures/Tapes/{id}_pattern.png','原创可循环纹样；同款卷面、展开条和商品缩略图一致。和纸边缘具有真实透明度。',f'Assets/Prefabs/TapeRoll_{k+1:02}.prefab')

STICKERS=['植物标本','玫瑰花束','旧邮票','蝴蝶','蘑菇','咖啡杯','复古钥匙','黄铜怀表','古典信封','白色雏菊','蕨叶','月亮星辰','旧相框','打字机','复古茶壶','蜡封信','旅行标签','银杏叶','书籍','复古相机']
def draw_sticker(k,d):
    ink=(106,81,56,255);paper=(234,217,184,255); green=(103,125,85,255)
    if k==0:sprig(d,244,252,4)
    elif k==10:
        d.line((201,438,280,77),fill=green,width=5)
        for j in range(15):
            yy=120+j*19;xx=271-j*4;ss=math.sin((j+1)/17*math.pi)*65
            for side in (-1,1):
                d.line((xx,yy,xx+side*ss,yy-26),fill=green,width=3)
                for q in range(1,7):leaf(d,xx+side*ss*q/7,yy-26*q/7, .65,green,side*.9)
    elif k==1:
        for x,y,s in [(180,190,3.3),(290,230,3.1),(230,310,2.8)]:rose(d,x,y,s)
        d.polygon([(178,280),(300,310),(275,435),(221,421)],fill=(185,155,111,255));d.line((225,364,288,376),fill=ink,width=7)
    elif k==2:
        d.rectangle((125,65,387,445),fill=paper,outline=ink,width=4)
        for y in range(67,445,21):
            for x in (124,387):d.ellipse((x-6,y-6,x+6,y+6),fill=(0,0,0,0))
        d.rectangle((147,91,365,382),outline=(152,111,76,255),width=4);sprig(d,245,241,3.2);d.text((177,397),'POSTAGE',font=font(24),fill=ink)
    elif k==3:
        for side in (-1,1):
            d.ellipse((256+side*83-86,110,256+side*83+86,295),fill=(175,143,99,255),outline=ink,width=6)
            d.ellipse((256+side*60-57,259,256+side*60+57,392),fill=(198,169,121,255),outline=ink,width=5)
            for y in (179,227,304):d.ellipse((256+side*95-10,y-10,256+side*95+10,y+10),fill=(231,210,159,255))
        d.line((256,158,256,383),fill=ink,width=11)
    elif k==4:
        for x,y,s in [(211,213,1),(325,298,.65)]:
            d.rounded_rectangle((x-16*s,y,x+16*s,y+169*s),12,fill=paper,outline=ink,width=3)
            d.pieslice((x-107*s,y-113*s,x+107*s,y+81*s),180,360,fill=(171,98,76,255),outline=ink,width=4)
            for xx,yy in [(-53,-21),(17,-49),(55,-10)]:d.ellipse((x+xx*s-9,y+yy*s-8,x+xx*s+9,y+yy*s+8),fill=paper)
    elif k==5:
        d.ellipse((300,185,424,331),outline=ink,width=18);d.rounded_rectangle((115,159,337,360),32,fill=paper,outline=ink,width=6)
        d.ellipse((121,143,333,196),fill=(112,75,47,255),outline=ink,width=5);d.ellipse((91,355,387,396),outline=ink,width=7)
        for x in (174,227,281):d.arc((x,64,x+36,139),90,270,fill=(145,127,94,210),width=4)
    elif k==14:
        d.ellipse((307,194,435,355),outline=ink,width=15)
        d.polygon([(163,269),(106,193),(76,178),(92,228),(126,327),(176,342)],fill=paper,outline=ink,width=4)
        d.ellipse((139,201,365,386),fill=paper,outline=ink,width=5)
        d.ellipse((167,175,336,228),fill=paper,outline=ink,width=4);d.ellipse((235,153,269,189),fill=(170,132,72,255))
        rose(d,247,280,2);d.arc((173,331,333,386),0,180,fill=ink,width=5)
    elif k==6:
        d.ellipse((119,70,282,233),outline=(160,122,58,255),width=19);d.line((253,211,376,403),fill=(160,122,58,255),width=21)
        for t in range(3):d.line((330+t*17,346+t*23,297+t*17,370+t*23),fill=(160,122,58,255),width=19)
    elif k==7:
        d.ellipse((149,50,361,159),outline=ink,width=13);d.ellipse((95,131,417,453),fill=(173,133,68,255),outline=ink,width=6);d.ellipse((117,153,395,431),fill=paper,outline=ink,width=4)
        for t in range(12):
            a=t*math.tau/12; x,y=256+115*math.sin(a),292-115*math.cos(a);d.ellipse((x-3,y-3,x+3,y+3),fill=ink)
        d.line((256,292,256,213),fill=ink,width=6);d.line((256,292,310,325),fill=ink,width=6)
    elif k in (8,15):
        d.rectangle((74,134,438,385),fill=paper,outline=ink,width=4);d.line((74,134,256,288,438,134),fill=ink,width=4);d.line((74,385,210,256),fill=ink,width=3);d.line((438,385,304,256),fill=ink,width=3)
        if k==15:d.ellipse((219,244,293,318),fill=(147,70,52,255),outline=(112,52,43,255),width=5)
    elif k==9:
        for x,y in [(185,205),(304,260),(211,359)]:sprig(d,x,y+40,1.1);flower(d,x,y,3)
    elif k==11:
        d.ellipse((112,70,409,367),fill=(188,154,89,255));d.ellipse((196,33,442,293),fill=(0,0,0,0))
        for x,y in [(98,385),(396,361),(161,422)]:d.regular_polygon((x,y,22),4,rotation=45,fill=(188,154,89,255))
    elif k==12:
        d.rectangle((93,73,419,439),fill=(162,123,68,255),outline=ink,width=6);d.rectangle((115,95,397,417),outline=paper,width=9);d.rectangle((144,124,368,388),fill=(0,0,0,0))
        for x in (112,400):
            for y in (90,422):flower(d,x,y,.9)
    elif k==13:
        d.rounded_rectangle((85,217,427,410),22,fill=(94,113,104,255),outline=ink,width=7);d.rectangle((134,82,382,278),fill=paper,outline=ink,width=5)
        d.text((153,108),'DEAR DIARY',font=font(24),fill=ink)
        d.rectangle((100,240,412,265),fill=ink)
        for row in range(3):
            for col in range(9):d.ellipse((116+col*31,282+row*32,136+col*31,302+row*32),fill=paper,outline=ink,width=2)
    elif k==16:
        d.polygon([(102,143),(358,143),(426,256),(358,369),(102,369)],fill=paper,outline=ink,width=5);d.ellipse((355,242,378,265),outline=ink,width=5);d.text((126,223),'BON VOYAGE',font=font(27),fill=ink)
    elif k==17:
        d.pieslice((94,76,421,398),205,335,fill=(185,155,74,255),outline=ink,width=4)
        for t in range(8):d.line((256,353,110+t*40,144+abs(t-3)*12),fill=(143,119,62,255),width=2)
        d.line((256,353,285,450),fill=ink,width=6)
    elif k==18:
        for y,col in [(310,(140,89,73,255)),(235,(107,124,97,255)),(160,(171,133,75,255))]:
            d.rounded_rectangle((94,y,413,y+68),8,fill=col,outline=ink,width=4);d.rectangle((126,y+12,405,y+55),fill=paper);d.line((152,y,152,y+68),fill=ink,width=4)
    else:
        d.rounded_rectangle((77,153,435,379),20,fill=(100,115,105,255),outline=ink,width=7);d.rectangle((127,121,244,162),fill=ink);d.ellipse((187,184,371,368),fill=ink,outline=(173,141,89,255),width=11);d.ellipse((216,213,342,339),fill=(63,82,76,255),outline=paper,width=4);d.rectangle((105,188,166,232),fill=paper)

for k,name in enumerate(STICKERS):
    im=Image.new('RGBA',(512,512));draw_sticker(k,ImageDraw.Draw(im))
    # Warm die-cut edge, no baked lighting or background.
    alpha=im.getchannel('A');edge=alpha.filter(ImageFilter.MaxFilter(13));border=Image.new('RGBA',im.size,(242,229,203,0));border.putalpha(edge);border.alpha_composite(im)
    id=f'sticker_{k+1:02}';save(border,f'Sprites/Stickers/{id}.png');save(border,f'Resources/Catalog/{id}.png')
    item(id,name,'sticker',f'Sprites/Stickers/{id}.png','原创独立插画，暖白刀模边缘与真实 Alpha，可作为 Sprite 复用。')

for k in range(8):
    id=f'note_{k+1:02}';im=Image.new('RGBA',(768,768));d=ImageDraw.Draw(im)
    base=[(231,216,179,255),(205,215,187,255),(229,195,182,255),(206,212,205,255)][k%4]
    if k<4:d.rectangle((52,45,716,723),fill=base)
    else:d.polygon([(70,58),(693,47),(712,695),(660,711),(610,700),(560,721),(510,703),(447,718),(385,700),(310,722),(240,704),(170,720),(54,701)],fill=base)
    d.rectangle((78,77,686,686),outline=(155,130,89,170),width=2)
    if k%3==0:
        for y in range(208,643,48):d.line((122,y,644,y),fill=(155,130,89,100),width=2)
    if k%3==1:
        for x in range(126,658,40):
            for y in range(175,661,40):d.ellipse((x,y,x+2,y+2),fill=(155,130,89,100))
    if k%2:rose(d,128,121,1.8)
    else:sprig(d,626,602,2)
    d.text((116,114),'NOTES' if k%2==0 else 'MEMORIES',font=font(24),fill=(110,93,67,230))
    save(im,f'Sprites/Notes/{id}.png');save(im,f'Resources/Catalog/{id}.png');item(id,f'复古便签 {k+1:02}','note',f'Sprites/Notes/{id}.png','透明边缘；中央保留书写区域，当前阶段仅提供预览。')

for k,idx in enumerate([2,16,15,12,6,11]):
    im=Image.open(A/f'Sprites/Stickers/sticker_{idx+1:02}.png').copy();id=f'decoration_{k+1:02}'
    # Separate useful decorative colors and editable link to motif source.
    ar=np.array(im);rgb=ar[:,:,:3].astype(float);rgb[:,:,1]*=.93 if k%2 else 1.04;ar[:,:,:3]=np.clip(rgb,0,255).astype('uint8');im=Image.fromarray(ar)
    save(im,f'Sprites/Decorations/{id}.png');save(im,f'Resources/Catalog/{id}.png');item(id,['邮票边饰','旅行标签','火漆信封','复古相框','黄铜钥匙','月相装饰'][k],'decoration',f'Sprites/Decorations/{id}.png','独立透明装饰元素，源绘图代码与关联贴纸均保留。')

for k in range(24):
    id=f'page_{k+1:02}';src=A/f'Sprites/JournalPages/PAGE_{k+1:02}.png'
    shutil.copy2(src,A/f'Resources/Catalog/{id}.png')
    item(id,f'典藏内页 {k+1:02}','page',str(src.relative_to(A)).replace('\\','/'),'原有图片原样导入。约210×295像素；中央大面积留白，特写清晰度有限。')

# UI textures are reusable native assets, not empty placeholders.
paper=Image.open(A/'Textures/Paper.png').convert('RGBA');d=ImageDraw.Draw(paper)
for b in (16,24):d.rectangle((b,b,1023-b,1023-b),outline=(164,130,70,255),width=2)
save(paper,'UI/PanelPaper.png')
for icon in ('Back','Settings'):
    im=Image.new('RGBA',(128,128));d=ImageDraw.Draw(im);col=(156,122,64,255)
    if icon=='Back':d.line([(79,25),(38,64),(79,103)],fill=col,width=9)
    else:
        d.regular_polygon((64,64,49),12,fill=col);d.ellipse((25,25,103,103),fill=col);d.ellipse((44,44,84,84),fill=(0,0,0,0))
    save(im,f'UI/Icon{icon}.png')

(A/'Resources/Catalog.json').write_text(json.dumps({'items':catalog},ensure_ascii=False,indent=2),encoding='utf8')
(A/'Data/Catalog.json').write_text(json.dumps({'items':catalog},ensure_ascii=False,indent=2),encoding='utf8')
# Very quiet synthesized room/air loop; original, no external recording.
sr=22050;n=sr*8;t=np.arange(n)/sr
noise=rng.normal(0,1,n);noise=np.convolve(noise,np.ones(150)/150,'same')
air=noise*.012+.0004*np.sin(math.tau*110*t)
fade=np.minimum(np.minimum(t/1,(8-t)/1),1);air*=fade
with wave.open(str(A/'Audio/AfternoonRoom.wav'),'wb') as wv:
    wv.setnchannels(1);wv.setsampwidth(2);wv.setframerate(sr);wv.writeframes(np.int16(np.clip(air,-1,1)*32767).tobytes())

inventory=[];checks=[]
for p in sorted(A.rglob('*')):
    if p.is_file() and p.suffix in ('.png','.wav','.json'):
        row={'path':str(p.relative_to(ROOT)).replace('\\','/'),'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()}
        if p.suffix=='.png':
            im=Image.open(p);row.update(width=im.width,height=im.height,mode=im.mode)
            if im.mode=='RGBA':row['transparentPixels']=int(np.sum(np.array(im)[:,:,3]<255))
            if p.name.endswith('_pattern.png') or p.parent.name=='Textures':
                ar=np.array(im);checks.append({'file':p.name,'horizontalEdgeExact':bool(np.array_equal(ar[:,0],ar[:,-1])),'verticalEdgeExact':bool(np.array_equal(ar[0],ar[-1]))})
        inventory.append(row)
(ROOT/'docs/资产清单.json').write_text(json.dumps(inventory,ensure_ascii=False,indent=2),encoding='utf8')
(ROOT/'docs/logs/asset-validation.json').write_text(json.dumps({'catalogCount':len(catalog),'counts':{c:sum(i['category']==c for i in catalog) for c in ['tape','sticker','page','note','decoration']},'seams':checks,'alphaAllIndependentSprites':all(Image.open(A/i['source']).getextrema()[3][0]==0 for i in catalog if i['category'] in ('sticker','note','decoration'))},ensure_ascii=False,indent=2),encoding='utf8')
# Contact sheet for human inspection, with explicit category boundaries.
thumbs=Image.new('RGB',(1400,1000),(237,226,202));d=ImageDraw.Draw(thumbs)
chosen=catalog[:32]+[i for i in catalog if i['category']=='note']
for n,i in enumerate(chosen):
    im=Image.open(A/f'Resources/{i["texture"]}.png').convert('RGBA');im.thumbnail((160,159))
    x=(n%8)*175+(175-im.width)//2;y=(n//8)*200+8
    thumbs.paste(im,(x,y),im);d.text(((n%8)*175+12,(n//8)*200+170),i['id'],font=font(17),fill=(99,78,55))
thumbs.save(ROOT/'docs/screenshots/asset-contact-sheet.png')
print(json.dumps({'catalog':len(catalog),'files':len(inventory),'allSeamsExact':all(c['horizontalEdgeExact'] and c['verticalEdgeExact'] for c in checks)},ensure_ascii=False))
