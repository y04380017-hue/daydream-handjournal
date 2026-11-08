using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace VintageJournal
{
    public static class StudioExports
    {
        public static string Folder(WorkspaceGameplayController workspace)
        { string path=Path.Combine(Path.GetDirectoryName(workspace.journal.SavePath),"studio-exports");Directory.CreateDirectory(path);return path; }
        public static string ExportPage(WorkspaceGameplayController workspace,CommissionPageTarget target)
        {
            if(target==null)throw new InvalidOperationException("请先打开一页手账或离型本。");
            var mail=CommissionDeskController.Instance;if(!mail||!mail.Ready)throw new InvalidOperationException("拍照功能正在准备。");
            string path=Path.Combine(Folder(workspace),"page-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".png");
            if(!mail.CapturePage(target,path))throw new IOException("页面拍照失败，作品未改变。");
            var source=new Texture2D(2,2);source.LoadImage(File.ReadAllBytes(path));
            float fit=Mathf.Min(480f/source.width,680f/source.height);int pictureWidth=Mathf.Max(1,Mathf.RoundToInt(source.width*fit)),pictureHeight=Mathf.Max(1,Mathf.RoundToInt(source.height*fit));
            var picture=StudioGraphics.Readable(source,pictureWidth,pictureHeight);var canvas=new Texture2D(560,800,TextureFormat.RGBA32,false);
            try
            {
                var pixels=Enumerable.Repeat(new Color(.99f,.98f,.95f,1),560*800).ToArray();canvas.SetPixels(pixels);canvas.SetPixels((560-pictureWidth)/2,80+(680-pictureHeight)/2,pictureWidth,pictureHeight,picture.GetPixels());
                var logo=StudioGraphics.Text("白日梦手作房 · JOURNAL ATELIER",0,DaydreamPalette.Ink,512,64);
                try{var letters=logo.GetPixels();for(int y=0;y<64;y++)for(int x=0;x<512;x++){Color c=letters[y*512+x];if(c.a>.05f)canvas.SetPixel(x+24,y+6,c);}}
                finally{UnityEngine.Object.Destroy(logo);}
                canvas.Apply();File.WriteAllBytes(path,canvas.EncodeToPNG());
                if(!workspace.Service.RecordAtelierShare(target.pageId,DateTime.UtcNow.Ticks,false))Debug.LogWarning("[StudioExport] PNG已保存，分享记录暂未保存："+workspace.Service.LastMessage);
                return path;
            }
            finally{UnityEngine.Object.Destroy(source);UnityEngine.Object.Destroy(picture);UnityEngine.Object.Destroy(canvas);}
        }
        public static string ExportYear(WorkspaceGameplayController workspace)
        {
            var doc=workspace.Service.Document;var studio=doc.personalStudio;
            int year=DateTime.Now.Year;
            string yearKey=year.ToString();
            var favorite=studio.pageNotes.Where(n=>n.favorite&&(n.createdDate??"").StartsWith(yearKey,StringComparison.Ordinal)).Take(8).Select(n=>string.IsNullOrWhiteSpace(n.title)?n.pageId:n.title).ToArray();
            var most=doc.craftWorkspace.items.Where(i=>i.owner==CraftOwner.JournalPage||i.owner==CraftOwner.ReleaseBookPage).GroupBy(i=>i.assetId).OrderByDescending(g=>g.Count()).Take(5).Select(g=>(PersonalStudioAssets.Find(g.Key)?.displayName??workspace.scrapbook.Catalog.Find(g.Key)?.displayName??NightMaterialCatalog.NameFor(g.Key))+" ×"+g.Count());
            var replied=doc.commissionDesk?.letters.Where(l=>l.status==CommissionLetterStatus.Replied).ToArray()??Array.Empty<CommissionLetterState>();
            var clients=replied.Where(l=>l.sentUtcTicks>0&&new DateTime(l.sentUtcTicks,DateTimeKind.Utc).Year==year).GroupBy(l=>l.sender).Select(g=>g.Key+" ×"+g.Count());
            string left=year+"  我的白日梦年鉴\n\n"+studio.title+"\n\n本年珍藏："+favorite.Length+" 页\n"+string.Join("\n",favorite)+"\n\n本年留下的私人日记："+studio.diary.Count(d=>d.dateKey.StartsWith(yearKey,StringComparison.Ordinal))+" 篇";
            string right="这一年的朋友\n\n"+string.Join("\n",clients)+"\n\n日期未知的旧回信："+replied.Count(l=>l.sentUtcTicks==0)+" 封\n\n此刻作品中最常使用的素材\n"+string.Join("\n",most)+"\n\n不需要连续登录。\n每一页都为你留下。";
            int lines=Math.Max(left.Split('\n').Sum(line=>Mathf.Max(1,Mathf.CeilToInt(line.Length/21f))),right.Split('\n').Sum(line=>Mathf.Max(1,Mathf.CeilToInt(line.Length/21f))));
            int height=Mathf.Clamp((lines+3)*42,768,4096);
            var leftPage=StudioGraphics.Text(left,1,DaydreamPalette.Ink,768,height);var rightPage=StudioGraphics.Text(right,1,DaydreamPalette.Ink,768,height);
            var texture=new Texture2D(1568,height,TextureFormat.RGBA32,false);
            try
            {
                var pixels=Enumerable.Repeat(new Color(1,.98f,.94f,1),1568*height).ToArray();
                var lp=leftPage.GetPixels();var rp=rightPage.GetPixels();
                for(int y=0;y<height;y++)for(int x=0;x<1568;x++)
                {int i=y*1568+x;if(x>=768&&x<800){pixels[i]=new Color(.84f,.88f,.86f,1);continue;}Color c=x<768?lp[y*768+x]:rp[y*768+x-800];pixels[i]=Color.Lerp(pixels[i],new Color(c.r,c.g,c.b,1),c.a);}
                texture.SetPixels(pixels);texture.Apply();string path=Path.Combine(Folder(workspace),"year-in-review-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".png");File.WriteAllBytes(path,texture.EncodeToPNG());
                if(!workspace.Service.RecordAtelierShare("year-"+year,DateTime.UtcNow.Ticks,true))Debug.LogWarning("[StudioExport] 年鉴已保存，导出记录暂未保存："+workspace.Service.LastMessage);
                return path;
            }
            finally{UnityEngine.Object.Destroy(texture);UnityEngine.Object.Destroy(leftPage);UnityEngine.Object.Destroy(rightPage);}
        }
    }
}
