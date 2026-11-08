"""Read-only asset validation and delivery inventory; does not regenerate art."""
from pathlib import Path
import json, hashlib, csv
import numpy as np
from PIL import Image

root=Path(__file__).resolve().parents[1];assets=root/'Assets'
items=json.loads((assets/'Resources/Catalog.json').read_text(encoding='utf-8-sig'))['items']
checks=[]
def check(name,ok,detail):checks.append(dict(name=name,passed=bool(ok),detail=detail))
for cat,count in [('tape',12),('sticker',20),('note',8),('decoration',6),('page',24)]:
    check('catalog '+cat,sum(i['category']==cat for i in items)==count,str(count))
for i in items:
    tex=assets/'Resources'/(i['texture']+'.png')
    check(i['id']+' thumbnail',tex.is_file(),str(tex.relative_to(root)))
    if i['category'] in ('sticker','note','decoration'):
        im=Image.open(assets/i['source']); ar=np.array(im)
        check(i['id']+' actual alpha',im.mode=='RGBA' and np.any(ar[:,:,3]==0) and np.any(ar[:,:,3]==255),'transparent exterior and opaque ink')
for p in sorted((assets/'Textures/Tapes').glob('*_pattern.png')):
    ar=np.array(Image.open(p));check(p.stem+' seamless edges',np.array_equal(ar[:,0],ar[:,-1]) and np.array_equal(ar[0],ar[-1]),'both opposing border rows identical; artistic interior periodicity visually reviewed in contact sheet')
    strip=Image.open(p.with_name(p.name.replace('_pattern','_strip')))
    check(p.stem+' translucent strip',strip.mode=='RGBA' and 0<strip.getextrema()[3][0]<255,'actual partial alpha')
original_hashes=json.loads((root/'Source/original-pages-sha256.json').read_text(encoding='utf8'))
for k in range(1,25):
    file=f'PAGE_{k:02}.png'; a=assets/'Sprites/JournalPages'/file
    check(file+' preserves original bytes',hashlib.sha256(a.read_bytes()).hexdigest()==original_hashes[file],'original SHA256 retained inside project')
records=[]
for p in sorted(assets.rglob('*')):
    if not p.is_file() or p.suffix=='.meta':continue
    record=dict(path=p.relative_to(root).as_posix(),type=p.suffix[1:],bytes=p.stat().st_size,sha256=hashlib.sha256(p.read_bytes()).hexdigest())
    if p.suffix=='.png':
        im=Image.open(p);record.update(width=im.width,height=im.height,mode=im.mode)
    records.append(record)
(root/'docs/资产清单.json').write_text(json.dumps(records,ensure_ascii=False,indent=2),encoding='utf8')
with (root/'docs/商品资产关联.csv').open('w',newline='',encoding='utf-8-sig') as f:
    writer=csv.DictWriter(f,fieldnames=['id','name','category','texture','source','prefab','description']);writer.writeheader();writer.writerows(items)
md=['# 游戏资产目录与关联清单','', '命名：`tape_01…12`、`sticker_01…20`、`note_01…08`、`decoration_01…06`、`page_01…24`。所有文件位于本项目，详细哈希见 `资产清单.json`，商品到预览/原图/Prefab 的关联见 `商品资产关联.csv`。','',f'本轮共 {len(records)} 个非 meta 资产文件；{len(list((assets/"Prefabs").glob("*.prefab")))} 个 Prefab。模型为 Unity 原生 Mesh `.asset`，可直接复用；建模源为 `Assets/Editor/DeskSceneBuilder.cs`。','', '| 类别 | 数量 | 位置与关联 |','|---|---:|---|','| 胶带 | 12款 | Textures/Tapes/{id}_pattern.png、{id}_strip.png；Resources/Catalog/{id}.png；Prefabs/TapeRoll_XX.prefab |','| 原有内页 | 24张 | Sprites/JournalPages/PAGE_XX.png，字节保持原样；对应Resources/Catalog/page_XX.png |','| 透明贴纸 | 20张 | Sprites/Stickers；Resources/Catalog 同编号 |','| 透明便签 | 8张 | Sprites/Notes；Resources/Catalog 同编号 |','| 透明装饰 | 6张 | Sprites/Decorations；Resources/Catalog 同编号 |','| 表面贴图 | 6组 | Textures/{Walnut,Paper,Linen,Leather,Wall,Brass}.png及Normals对应法线 |','| 窗外背景 | 1张 | Textures/WindowAtmosphere.png，单独天空面，非整张场景背景 |','| UI | 3张 | UI/PanelPaper、IconBack、IconSettings；界面代码生成可缩放面板 |','| 环境音 | 1段 | Audio/AfternoonRoom.wav，24秒原创合成可循环音 |','', '## 商品表','', '| 编号 | 名称 | 类别 |','|---|---|---|']
md += [f'| {i["id"]} | {i["name"]} | {i["category"]} |' for i in items]
md += ['','## 复用与限制','','原有内页是用户既有生成图裁切资源，无单独授权证明；新增纹样、贴纸、声音和模型均保留程序源。本轮未调用 AI 图像生成服务，也没有使用付费模型或素材。贴纸偏平面插画，模型与玻璃采用轻量近似，并非扫描级实物。透明边缘与平铺边界检查日志见 logs/final-asset-validation.json。']
(root/'docs/资产目录.md').write_text('\n'.join(md)+'\n',encoding='utf8')
report=dict(passed=all(c['passed'] for c in checks),checks=checks,assetFiles=len(records),prefabs=len(list((assets/'Prefabs').glob('*.prefab'))),models=len(list((assets/'Models').glob('*.asset'))),materials=len(list((assets/'Materials').glob('*.mat'))))
(root/'docs/logs/final-asset-validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps({k:v for k,v in report.items() if k!='checks'}),f'checks={len(checks)}')
if not report['passed']:raise SystemExit(1)
