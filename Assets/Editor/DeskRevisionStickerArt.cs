using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace VintageJournal.Editor
{
    public static partial class DeskRevisionSetup
    {
        static readonly string[] StickerNames={"云端小门","月亮枕头","糖果星球","薄荷风筝","雨后云朵","日光拱窗","下午茶杯","草莓牛奶","笑脸吐司","小雏菊","午睡小猫","灵感小本","云上车票","旅途拍立得","山间邮票","周末行李","远方来信","口袋相机"};
        static void GenerateStickerAssets()
        {
            const string folder="Assets/Resources/DreamStickers/";Directory.CreateDirectory(folder);Directory.CreateDirectory("Source/Generated/DreamStickers");
            var catalog=DecorationCatalog.Load();var definitions=catalog.assets.ToList();var itemCatalog=ItemCatalog.Load();var items=itemCatalog.items.ToList();
            var commission=CommissionCatalog.Load();var tags=commission.tags.ToList();
            var manifest=new List<string>{"# 白日梦贴纸 · 原创程序绘制", "1024 × 1024 RGBA PNG；每款真实透明 Alpha。矢量构图与栅格生成源：Assets/Editor/DeskRevisionStickerArt.cs。原贴纸源图及 ID 保留。", ""};
            for(int i=0;i<18;i++)
            {
                string id="dream_sticker_"+(i+1).ToString("D2"),path=folder+id+".png";
                var canvas=new StickerCanvas();DrawSticker(canvas,i);var texture=canvas.Texture();
                File.WriteAllBytes(path,texture.EncodeToPNG());Object.DestroyImmediate(texture);AssetDatabase.ImportAsset(path);
                var importer=(TextureImporter)AssetImporter.GetAtPath(path);importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.alphaIsTransparency=true;importer.mipmapEnabled=true;importer.maxTextureSize=1024;importer.isReadable=true;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();
                var tex=AssetDatabase.LoadAssetAtPath<Texture2D>(path);var definition=new DecorationDefinition{assetId=id,displayName=StickerNames[i],category=DecorationCategory.Sticker,texture=tex,thumbnail=tex,defaultSize=new Vector2(.257f,.181f),minScale=.2f,maxScale=2f,maskWidth=64,maskHeight=64,alphaMask=new byte[4096]};
                for(int y=0;y<64;y++)for(int x=0;x<64;x++)definition.alphaMask[y*64+x]=(byte)Mathf.RoundToInt(tex.GetPixelBilinear((x+.5f)/64,(y+.5f)/64).a*255);
                definitions.Add(definition);items.Add(new CatalogItem{id=id,name=StickerNames[i],category="sticker",texture="DreamStickers/"+id,description="云上手作原创贴纸 · "+(i<6?"白日梦":i<12?"可爱生活":"旅行票据")});
                tags.Add(new CommissionAssetTags{assetId=id,tags=i<6?new[]{"蓝色","浪漫",i==1?"夜晚":"自然"}:i<12?new[]{"日常","暖色",i==9?"花草":"咖啡"}:new[]{"旅行","书信","回忆"}});
                manifest.Add("- "+id+"："+StickerNames[i]+"；38 × 38 mm 画布；PNG："+path);
            }
            catalog.assets=definitions.ToArray();EditorUtility.SetDirty(catalog);
            itemCatalog.items=items.ToArray();File.WriteAllText("Assets/Resources/Catalog.json",JsonUtility.ToJson(itemCatalog,true));
            commission.tags=tags.ToArray();File.WriteAllText("Assets/Resources/Commissions/commission-catalog.json",JsonUtility.ToJson(commission,true));
            File.WriteAllLines("Source/Generated/DreamStickers/资产清单.md",manifest);AssetDatabase.Refresh();
        }
        static void DrawSticker(StickerCanvas c,int i)
        {
            var white=C("FFFDF7");var pink=C("F4ADB9");var blue=C("99CBDF");var mint=C("ACD8C4");var lilac=C("BEB1DF");var gold=C("E9C67E");var ink=C("596A86");
            void Star(float x,float y,float r,Color color){c.Poly(Enumerable.Range(0,8).Select(n=>new Vector2(x+Mathf.Sin(n*Mathf.PI/4)*(n%2==0?r:r*.3f),y+Mathf.Cos(n*Mathf.PI/4)*(n%2==0?r:r*.3f))).ToArray(),color);}
            void Cloud(float x,float y,float s,Color color){c.Ellipse(x,y,s*1.1f,s*.45f,color);c.Ellipse(x-s*.40f,y-s*.23f,s*.47f,s*.47f,color);c.Ellipse(x+s*.25f,y-s*.40f,s*.56f,s*.56f,color);}
            void Badge(float x,float y,float r,Color color){c.Ellipse(x,y,r+5,r+5,white);c.Ellipse(x,y,r,r,color);}
            void Line(float x,float y,float a,float b,Color color,float width=2){c.Line(x,y,a,b,width,color);}
            if(i==0||i==5)
            {
                c.Ellipse(128,90,69,65,white);c.Rect(59,87,138,133,white);c.Ellipse(128,91,59,55,i==0?lilac:pink);c.Rect(69,87,118,123,i==0?lilac:pink);
                c.Ellipse(128,96,43,43,blue);c.Rect(85,96,86,104,blue);Badge(141,106,17,gold);Cloud(119,154,27,white);c.Rect(87,181,82,19,mint);
                if(i==5){Line(128,60,128,200,white,5);Line(84,124,172,124,white,5);}else{for(int j=0;j<3;j++)c.Rect(75-j*5,202+j*8,106+j*10,7,j%2==0?pink:mint);}
                Star(43,81,10,gold);Star(207,141,12,pink);
            }
            else if(i==1){Badge(137,103,65,white);c.Ellipse(137,103,59,59,gold);c.Ellipse(163,83,51,51,white);Cloud(107,176,61,white);Cloud(107,176,54,blue);Star(57,65,12,lilac);Star(200,162,13,pink);}
            else if(i==2){Badge(128,128,68,lilac);c.Ellipse(128,128,104,27,white);c.Ellipse(128,128,98,20,pink);c.Ellipse(128,128,77,12,lilac);c.Ellipse(128,100,63,43,lilac);c.Ellipse(111,92,13,10,C("D8CFF0"));Star(45,51,11,gold);Star(217,204,16,mint);}
            else if(i==3){c.Poly(new[]{new Vector2(128,30),new Vector2(199,105),new Vector2(128,178),new Vector2(57,105)},white);c.Poly(new[]{new Vector2(128,40),new Vector2(189,105),new Vector2(128,165),new Vector2(67,105)},mint);c.Poly(new[]{new Vector2(128,40),new Vector2(189,105),new Vector2(128,105)},pink);Line(128,40,128,165,ink);Line(67,105,189,105,ink);Line(128,165,146,198,ink);Line(146,198,117,228,ink);for(int j=0;j<3;j++)c.Ellipse(135-j*5,181+j*15,11,4,j%2==0?pink:gold);}
            else if(i==4){Cloud(128,118,86,white);Cloud(128,118,77,blue);for(int j=0;j<4;j++)Line(82+j*29,177,73+j*29,198+j%2*12,j%2==0?pink:mint,6);Star(197,55,15,gold);Line(105,123,111,123,ink,3);Line(144,123,150,123,ink,3);c.Ellipse(127,135,6,4,white);}
            else if(i==6){c.Ellipse(127,203,91,19,white);c.Ellipse(127,203,83,11,pink);c.Ellipse(190,133,35,39,white);c.Ellipse(190,133,27,30,mint);c.Ellipse(190,133,17,22,white);c.Rect(56,83,120,105,white);c.Ellipse(116,184,60,19,white);c.Rect(63,91,106,86,mint);c.Ellipse(116,175,53,20,mint);c.Ellipse(116,93,52,14,ink);c.Ellipse(116,91,47,10,gold);Star(114,139,17,white);Line(88,65,96,45,pink,4);Line(128,65,137,37,blue,4);}
            else if(i==7){c.Rect(65,66,126,155,white);c.Poly(new[]{new Vector2(65,66),new Vector2(91,29),new Vector2(162,29),new Vector2(191,66)},white);c.Rect(72,72,112,141,pink);c.Poly(new[]{new Vector2(74,64),new Vector2(96,36),new Vector2(157,36),new Vector2(180,64)},blue);c.Rect(88,99,80,91,white);c.Poly(new[]{new Vector2(98,129),new Vector2(158,129),new Vector2(128,175)},pink);c.Ellipse(112,128,16,12,pink);c.Ellipse(144,128,16,12,pink);Star(127,121,16,mint);for(int j=0;j<6;j++)c.Ellipse(114+j%3*13,140+j/3*12,1.5f,2.5f,gold);}
            else if(i==8){c.Rect(53,82,150,133,white);c.Ellipse(128,77,84,44,white);c.Rect(61,87,134,120,gold);c.Ellipse(128,80,76,38,gold);c.Rect(72,92,112,99,white);c.Ellipse(128,84,64,26,white);c.Rect(101,99,54,37,C("F2D990"));Line(106,153,109,153,ink,4);Line(147,153,150,153,ink,4);Line(120,168,137,168,pink,4);c.Ellipse(91,163,9,4,pink);c.Ellipse(165,163,9,4,pink);}
            else if(i==9){Line(126,104,126,228,white,10);Line(126,104,126,226,mint,5);c.Ellipse(104,190,24,10,mint);c.Ellipse(150,169,25,11,mint);for(int j=0;j<8;j++){float a=j*Mathf.PI/4;c.Ellipse(128+Mathf.Cos(a)*40,96+Mathf.Sin(a)*40,22,22,white);}Badge(128,96,23,gold);Star(203,162,10,pink);}
            else if(i==10){c.Ellipse(128,172,85,48,white);c.Ellipse(128,170,77,42,lilac);c.Poly(new[]{new Vector2(70,120),new Vector2(65,48),new Vector2(106,74),new Vector2(153,74),new Vector2(191,48),new Vector2(187,123)},white);c.Ellipse(128,119,61,46,white);c.Poly(new[]{new Vector2(78,115),new Vector2(74,62),new Vector2(109,84),new Vector2(151,84),new Vector2(181,62),new Vector2(179,115)},gold);c.Ellipse(128,117,53,40,gold);Line(94,117,109,121,ink,3);Line(147,121,162,117,ink,3);c.Ellipse(128,132,5,4,pink);Line(113,139,121,142,ink,2);Line(135,142,142,139,ink,2);Star(211,65,10,pink);}
            else if(i==11){c.Rect(56,37,151,191,white);c.Rect(63,44,137,176,mint);c.Rect(73,44,8,176,pink);c.Rect(96,81,79,63,white);Star(135,111,22,gold);for(int j=0;j<3;j++)Line(104,166+j*9,166,166+j*9,white,3);c.Rect(174,44,5,176,blue);}
            else if(i==12){c.Rect(27,67,202,125,white);c.Rect(34,74,188,111,pink);c.Rect(41,82,174,95,white);for(int j=0;j<10;j++)Line(170,84+j*9,170,88+j*9,pink,2);c.Text("CLOUD",54,96,4,ink);c.Text("EXPRESS",54,128,3,ink);for(int j=0;j<12;j++)c.Rect(183+j*2,101,j%2+1,51,ink);Star(75,158,8,gold);}
            else if(i==13){c.Rect(48,29,160,205,white);c.Rect(58,39,140,144,blue);c.Ellipse(156,80,24,24,gold);c.Poly(new[]{new Vector2(58,178),new Vector2(97,95),new Vector2(143,178)},mint);c.Poly(new[]{new Vector2(100,178),new Vector2(159,116),new Vector2(198,178)},lilac);c.Text("DAY 01",75,199,3,ink);}
            else if(i==14){c.Rect(51,32,154,194,white);for(int j=0;j<7;j++){c.Ellipse(51,46+j*27,8,8,white);c.Ellipse(205,46+j*27,8,8,white);}c.Rect(62,42,132,174,pink);c.Rect(70,50,116,155,blue);c.Poly(new[]{new Vector2(72,169),new Vector2(127,83),new Vector2(181,169)},lilac);c.Poly(new[]{new Vector2(109,111),new Vector2(127,83),new Vector2(145,111)},white);c.Text("AIR MAIL",81,184,2,ink);Star(164,76,12,gold);}
            else if(i==15){c.Ellipse(128,69,40,26,white);c.Ellipse(128,69,31,17,gold);c.Ellipse(128,69,19,9,white);c.Rect(38,74,180,134,white);c.Rect(45,81,166,120,blue);for(int s=0;s<2;s++)c.Rect(76+s*91,82,12,118,gold);c.Rect(108,111,41,47,pink);Star(128,134,15,white);c.Ellipse(69,214,10,8,ink);c.Ellipse(189,214,10,8,ink);}
            else if(i==16){c.Rect(28,62,200,141,white);c.Rect(35,69,186,127,blue);c.Poly(new[]{new Vector2(35,69),new Vector2(128,141),new Vector2(221,69)},pink);Line(37,194,101,135,white,3);Line(219,194,155,135,white,3);Badge(128,143,19,gold);Star(128,143,10,white);c.Rect(180,83,25,31,white);c.Rect(184,87,17,23,lilac);}
            else{c.Rect(30,72,196,137,white);c.Rect(37,79,182,123,pink);c.Rect(72,48,101,37,white);c.Rect(80,55,84,25,mint);c.Ellipse(131,140,50,50,white);c.Ellipse(131,140,41,41,ink);c.Ellipse(131,140,31,31,blue);c.Ellipse(117,126,10,10,white);c.Rect(184,95,23,15,white);c.Rect(49,91,18,6,gold);}
        }
        static Color C(string hex){ColorUtility.TryParseHtmlString("#"+hex,out var c);return c;}
        sealed class StickerCanvas
        {
            const int N=1024;readonly Color[] pixels=new Color[N*N];
            void Raster(float x0,float y0,float x1,float y1,Func<float,float,float> sdf,Color color)
            {
                for(int y=Mathf.Max(0,Mathf.FloorToInt(y0*4-2));y<Mathf.Min(N,Mathf.CeilToInt(y1*4+2));y++)
                for(int x=Mathf.Max(0,Mathf.FloorToInt(x0*4-2));x<Mathf.Min(N,Mathf.CeilToInt(x1*4+2));x++)
                {
                    float a=Mathf.Clamp01(.5f-sdf((x+.5f)/4,(y+.5f)/4)*4);if(a<=0)continue;
                    int index=(N-1-y)*N+x;var old=pixels[index];float opacity=a+old.a*(1-a);
                    var rgb=opacity>0?(color*a+old*old.a*(1-a))/opacity:Color.clear;rgb.a=opacity;pixels[index]=rgb;
                }
            }
            public void Rect(float x,float y,float w,float h,Color c)=>Raster(x,y,x+w,y+h,(a,b)=>Mathf.Max(Mathf.Abs(a-x-w/2)-w/2,Mathf.Abs(b-y-h/2)-h/2),c);
            public void Ellipse(float x,float y,float rx,float ry,Color c)=>Raster(x-rx,y-ry,x+rx,y+ry,(a,b)=>(Mathf.Sqrt((a-x)*(a-x)/(rx*rx)+(b-y)*(b-y)/(ry*ry))-1)*Mathf.Min(rx,ry),c);
            public void Line(float x,float y,float x2,float y2,float width,Color c)
            {var a=new Vector2(x,y);var v=new Vector2(x2-x,y2-y);Raster(Mathf.Min(x,x2)-width,Mathf.Min(y,y2)-width,Mathf.Max(x,x2)+width,Mathf.Max(y,y2)+width,(px,py)=>{var p=new Vector2(px,py)-a;return (p-v*Mathf.Clamp01(Vector2.Dot(p,v)/v.sqrMagnitude)).magnitude-width*.5f;},c);}
            public void Poly(Vector2[] v,Color c)
            {Raster(v.Min(p=>p.x),v.Min(p=>p.y),v.Max(p=>p.x),v.Max(p=>p.y),(x,y)=>{bool inside=false;float dist=999;var p=new Vector2(x,y);for(int j=0,k=v.Length-1;j<v.Length;k=j++){var d=v[j]-v[k];var q=p-v[k];dist=Mathf.Min(dist,(q-d*Mathf.Clamp01(Vector2.Dot(q,d)/d.sqrMagnitude)).magnitude);if((v[j].y>y)!=(v[k].y>y)&&x<(v[k].x-v[j].x)*(y-v[j].y)/(v[k].y-v[j].y)+v[j].x)inside=!inside;}return inside?-dist:dist;},c);}
            public void Text(string value,float x,float y,float scale,Color c)
            {
                const string chars="ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
                string[] glyphs={"0111010001111111000110001","1111010001111101000111110","0111110000100001000001111","1111010001100011000111110","1111110000111101000011111","1111110000111101000010000","0111110000101111000101111","1000110001111111000110001","1111100100001000010011111","0011100010000101001001100","1000110010111001001010001","1000010000100001000011111","1000111011101011000110001","1000111001101011001110001","0111010001100011000101110","1111010001111101000010000","0111010001101011001001101","1111010001111101001010001","0111110000011100000111110","1111100100001000010000100","1000110001100011000101110","1000110001100010101000100","1000110001101011101110001","1000101010001000101010001","1000101010001000010000100","1111100010001000100011111","0111010001101011000101110","0010001100001000010001110","1111000001011101000011111","1111000001011100000111110","1001010010111110001000010","1111110000111100000111110","0111110000111101000101110","1111100010001000100001000","0111010001011101000101110","0111010001011110000101110"};
                foreach(char ch in value){int index=chars.IndexOf(ch);if(index>=0)for(int row=0;row<5;row++)for(int col=0;col<5;col++)if(glyphs[index][row*5+col]=='1')Rect(x+col*scale,y+row*scale,scale*.85f,scale*.85f,c);x+=6*scale;}
            }
            public Texture2D Texture(){var t=new Texture2D(N,N,TextureFormat.RGBA32,false);t.SetPixels(pixels);t.Apply();return t;}
        }
    }
}
