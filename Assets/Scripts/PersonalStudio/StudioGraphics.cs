using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VintageJournal
{
    /// <summary>Small deterministic canvases and offscreen copies. Never changes an interactive camera transform.</summary>
    public static class StudioGraphics
    {
        public static readonly Color[] Colors = { new Color(.45f,.77f,.68f), new Color(.55f,.75f,.91f), new Color(.96f,.69f,.66f), new Color(.73f,.65f,.90f), new Color(.97f,.82f,.43f), new Color(.95f,.93f,.85f) };
        public static Texture2D Readable(Texture source, int width = 256, int height = 256)
        {
            var previous = RenderTexture.active; var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            try
            {
                Graphics.Blit(source, target); RenderTexture.active = target;
                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                texture.ReadPixels(new Rect(0,0,width,height),0,0); texture.Apply(); return texture;
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
        }
        public static Texture2D Scene(Camera lens, int filter)
        {
            var target = RenderTexture.GetTemporary(768, 432, 24); var previous = RenderTexture.active;
            try
            {
                RenderPipeline.SubmitRenderRequest(lens, new RenderPipeline.StandardRequest { destination = target });
                RenderTexture.active = target; var texture = new Texture2D(768,432,TextureFormat.RGBA32,false);
                texture.ReadPixels(new Rect(0,0,768,432),0,0); texture.Apply();
                var pixels = texture.GetPixels();
                for (int i=0;i<pixels.Length;i++)
                {
                    Color c=pixels[i];
                    if(filter==1)c=Color.Lerp(c,new Color(c.r*.96f+.08f,c.g*.91f+.05f,c.b*.84f+.04f),.65f);
                    if(filter==2)c=Color.Lerp(c,new Color(c.r*.85f+.08f,c.g*.88f+.06f,c.b*1.06f+.03f),.65f);
                    if(filter==3)c=Color.Lerp(c,new Color(c.grayscale,c.grayscale,c.grayscale),.8f);
                    pixels[i]=c;
                }
                texture.SetPixels(pixels);texture.Apply();return texture;
            }
            finally { RenderTexture.active=previous;RenderTexture.ReleaseTemporary(target); }
        }
        public static Texture2D Stamp(int shape, Color ink)
        {
            var texture=new Texture2D(256,256,TextureFormat.RGBA32,false);var pixels=new Color[65536];
            for(int y=0;y<256;y++)for(int x=0;x<256;x++)
            {
                Vector2 p=new Vector2((x-127.5f)/128f,(y-127.5f)/128f);float d;
                if(shape==1){float a=Mathf.Atan2(p.y,p.x);d=p.magnitude-(.52f+.19f*Mathf.Cos(a*5));}
                else if(shape==2){p.y+=.12f;float a=p.x*p.x+p.y*p.y-.35f;d=a*a*a-p.x*p.x*p.y*p.y*p.y;}
                else if(shape==3){d=Mathf.Abs(p.magnitude-.55f)-.035f;d=Mathf.Min(d,(p-new Vector2(.43f,.43f)).magnitude-.11f);}
                else d=p.magnitude-.52f;
                float alpha=1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(-.015f,.015f,d));
                pixels[y*256+x]=new Color(ink.r,ink.g,ink.b,alpha*.92f);
            }
            texture.SetPixels(pixels);texture.Apply();return texture;
        }
        public static Texture2D DieCut(Texture2D source)
        {
            int width=source.width,height=source.height;var input=source.GetPixels();var output=new Color[input.Length];
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                int index=y*width+x;Color c=input[index];
                if(c.a>.05f){output[index]=c;continue;}
                bool near=false;
                for(int dy=-3;dy<=3&&!near;dy++)for(int dx=-3;dx<=3;dx++)
                {int px=x+dx,py=y+dy;if(dx*dx+dy*dy<=10&&px>=0&&py>=0&&px<width&&py<height&&input[py*width+px].a>.2f){near=true;break;}}
                output[index]=near?new Color(1,.99f,.95f,1):Color.clear;
            }
            var texture=new Texture2D(width,height,TextureFormat.RGBA32,false);texture.SetPixels(output);texture.Apply();return texture;
        }
        public static Texture2D Text(string content,int style,Color ink,int width=512,int height=256)
        {
            var texture=new Texture2D(width,height,TextureFormat.RGBA32,false);var pixels=new Color[width*height];
            Font font=Font.CreateDynamicFontFromOSFont(style==1?new[]{"KaiTi","Microsoft YaHei"}:style==2?new[]{"FangSong","Microsoft YaHei"}:new[]{"Microsoft YaHei","Arial"},32);
            try
            {
                font.RequestCharactersInTexture(content,32,FontStyle.Normal);
                var atlas=Readable(font.material.mainTexture,font.material.mainTexture.width,font.material.mainTexture.height);
                try
                {
                    int penX=14,baseline=height-44;
                    foreach(char ch in content)
                    {
                        if(ch=='\n'||penX>width-42){penX=14;baseline-=42;if(ch=='\n')continue;}
                        if(baseline<20)break;
                        if(!font.GetCharacterInfo(ch,out var glyph,32,FontStyle.Normal)){penX+=24;continue;}
                        int gw=Mathf.Max(1,glyph.maxX-glyph.minX),gh=Mathf.Max(1,glyph.maxY-glyph.minY);
                        for(int gy=0;gy<gh;gy++)for(int gx=0;gx<gw;gx++)
                        {
                            int x=penX+glyph.minX+gx,y=baseline+glyph.minY+gy;if(x<0||y<0||x>=width||y>=height)continue;
                            Vector2 uv=glyph.uvBottomLeft+(glyph.uvBottomRight-glyph.uvBottomLeft)*((gx+.5f)/gw)+(glyph.uvTopLeft-glyph.uvBottomLeft)*((gy+.5f)/gh);
                            float alpha=atlas.GetPixelBilinear(uv.x,uv.y).a;pixels[y*width+x]=new Color(ink.r,ink.g,ink.b,alpha);
                        }
                        penX+=glyph.advance;
                    }
                }
                finally{UnityEngine.Object.Destroy(atlas);}
            }
            finally{UnityEngine.Object.Destroy(font);}
            texture.SetPixels(pixels);texture.Apply();return texture;
        }
        public static Texture2D Framed(Texture2D photo)
        {
            var small=Readable(photo,208,180);var texture=new Texture2D(256,256,TextureFormat.RGBA32,false);var pixels=new Color[65536];
            for(int y=0;y<256;y++)for(int x=0;x<256;x++)pixels[y*256+x]=(x<8||x>247||y<8||y>247)?Color.clear:new Color(1,.99f,.95f,1);
            var image=small.GetPixels();for(int y=0;y<180;y++)for(int x=0;x<208;x++)pixels[(y+54)*256+x+24]=image[y*208+x];
            texture.SetPixels(pixels);texture.Apply();UnityEngine.Object.Destroy(small);return texture;
        }
    }
}
