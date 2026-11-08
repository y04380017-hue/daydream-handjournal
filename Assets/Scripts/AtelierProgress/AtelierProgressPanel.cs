using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace VintageJournal
{
    public static class AtelierProgressPanel
    {
        static int tab;
        static Vector2 scroll;
        static GUIStyle label,title,button;
        static string status;
        static Font font;
        public static void Draw(Rect rect,WorkspaceGameplayController workspace)
        {
            if(!workspace||!workspace.Ready)return;
            if(!font){font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","Arial"},21);label=new GUIStyle(GUI.skin.label){font=font,fontSize=22,wordWrap=true};title=new GUIStyle(label){fontSize=28};button=new GUIStyle(GUI.skin.button){font=font,fontSize=21,wordWrap=true};}
            label.normal.textColor=title.normal.textColor=DaydreamPalette.Text(DaydreamPalette.Ink);
            GUI.BeginGroup(rect);
            string[] tabs={"今日来信","收藏图鉴","工房成长","足迹与年鉴"};
            for(int i=0;i<tabs.Length;i++)if(GUI.Button(new Rect(i*220,0,208,46),tabs[i],button)){tab=i;scroll=Vector2.zero;}
            var service=workspace.Service;var doc=service.Document;var data=doc.atelierProgress;
            if(data==null){if(GUI.Button(new Rect(0,68,400,50),"打开成长手册",button))service.InitializeAtelierProgress(DateTime.UtcNow.Ticks);GUI.EndGroup();return;}
            scroll=GUI.BeginScrollView(new Rect(0,62,rect.width,rect.height-112),scroll,new Rect(0,0,rect.width-28,tab==1?Mathf.CeilToInt(NightMaterialCatalog.AllAssetIds.Length/5f)*238:tab==2?630:tab==3?850:640));
            if(tab==0)
            {
                GUI.Label(new Rect(8,0,rect.width-50,48),DateTime.Now.ToString("yyyy年M月d日")+" · 今天也可以慢慢来",title);
                GUI.Label(new Rect(8,64,rect.width-50,72),AtelierProgressCatalog.TodayPrompt(DateTime.Now)+"\n可以在个人手账里用文字、贴纸或照片回答，也可以只看看云。",label);
                if(GUI.Button(new Rect(8,160,300,54),"领取今日的一封信",button))status=service.ClaimDailyCommission(DateTime.Now)?"今日的信已放入邮筒。":service.LastMessage;
                string seasonal=AtelierProgressCatalog.SeasonalAsset(DateTime.Now);
                GUI.Label(new Rect(8,245,rect.width-50,50),"此刻的季节礼物："+NightMaterialCatalog.NameFor(seasonal),title);
                if(GUI.Button(new Rect(8,308,350,54),"迎接季节客人与小礼",button))status=service.ClaimSeasonalEvent(DateTime.Now)?"小礼已入素材袋，旅人的信留在邮筒里。":service.LastMessage;
                if(GUI.Button(new Rect(380,308,350,54),"看看远处的门与楼梯",button))status=service.RevealDreamEvent(DateTime.Now,DreamGameplayController.Instance?DreamGameplayController.Instance.CurrentSetting:0)?"回到桌前，轻点窗外的门，收藏被遗忘的梦。":service.LastMessage;
                GUI.Label(new Rect(8,405,rect.width-50,130),data.welcomeNote+"\n\n没有连续登录奖励，也没有漏掉哪一天的惩罚。离开几天回来，仍会有纸张与问候在等你。",label);
            }
            if(tab==1)
            {
                for(int i=0;i<NightMaterialCatalog.AllAssetIds.Length;i++)
                {string id=NightMaterialCatalog.AllAssetIds[i];int count=service.Items.Count(item=>item.assetId==id&&item.owner!=CraftOwner.Deleted);bool found=service.Items.Any(item=>item.assetId==id);float x=i%5*218,y=i/5*238;
                    if(found)GUI.DrawTexture(new Rect(x+28,y+4,150,132),NightMaterialCatalog.Find(id).thumbnail,ScaleMode.ScaleToFit,true);
                    GUI.Label(new Rect(x+6,y+144,206,48),found?NightMaterialCatalog.NameFor(id):"尚未发现",label);
                    GUI.Label(new Rect(x+6,y+196,206,30),found?"拥有 "+count+" · 已收藏":"在不同时段与季节相遇",label);
                }
            }
            if(tab==2)
            {
                GUI.Label(new Rect(8,0,rect.width-50,56),"工房声望 Lv."+AtelierProgressCatalog.Reputation(doc)+" · 已帮助 "+AtelierProgressCatalog.Completed(doc)+" 次来信 · 梦币 "+(doc.dreamGameplay?.coins??0),title);
                GUI.Label(new Rect(8,66,rect.width-50,62),"回复1/2/3封信会依次开放印章、贴纸工坊和照片；也可以用梦币提前添置。",label);
                for(int i=0;i<AtelierProgressCatalog.UpgradeIds.Length;i++)
                {string id=AtelierProgressCatalog.UpgradeIds[i];bool owned=data.purchases.Any(p=>p.id==id);float y=152+i*72;
                    GUI.Label(new Rect(8,y,620,52),AtelierProgressCatalog.UpgradeName(id)+" · "+AtelierProgressCatalog.Price(id)+" 梦币",label);
                    GUI.enabled=!owned;if(GUI.Button(new Rect(650,y,260,50),owned?"已经添置":"添置到工房",button))status=service.PurchaseUpgrade(id)?"已添置，回桌前看看吧。":service.LastMessage;GUI.enabled=true;
                }
            }
            if(tab==3)
            {
                var sent=doc.commissionDesk?.letters.Where(l=>!l.IsOpen).ToArray()??Array.Empty<CommissionLetterState>();
                int caught=service.Items.Count(i=>i.instanceId.StartsWith("dream-catch-"));
                GUI.Label(new Rect(8,0,rect.width-50,56),"工房足迹与年度回顾 · "+DateTime.Now.Year,title);
                string frequent=string.Join("、",doc.AllPages.SelectMany(p=>p.decorations).GroupBy(d=>d.assetId).OrderByDescending(g=>g.Count()).Take(5).Select(g=>NightMaterialCatalog.IsKnown(g.Key)?NightMaterialCatalog.NameFor(g.Key)+" ×"+g.Count():g.Key+" ×"+g.Count()));
                int thisYear=sent.Count(l=>l.sentUtcTicks>0&&new DateTime(l.sentUtcTicks,DateTimeKind.Utc).ToLocalTime().Year==DateTime.Now.Year);
                int favorites=doc.personalStudio?.pageNotes?.Count(n=>n.favorite)??0;
                int sessions=data.sessions?.Count??0;
                int todaySessions=data.sessions?.Count(s=>new DateTime(s.utcTicks,DateTimeKind.Utc).ToLocalTime().Date==DateTime.Now.Date)??0;
                int shares=data.shares?.Count(s=>!s.isYearReview)??0;
                int yearReviews=data.shares?.Count(s=>s.isYearReview)??0;
                bool personal=(doc.personalStudio?.diary?.Count??0)>0||favorites>0||(doc.personalStudio?.artworks?.Count??0)>0;
                GUI.Label(new Rect(8,80,rect.width-50,540),"到访过的日子："+data.visitDates.Count+"\n实际游戏会话："+sessions+"（今天 "+todaySessions+"）\n导出的页面分享图片："+shares+"；年度双页："+yearReviews+"\n累计寄出的页面照片："+sent.Length+"（本年有日期记录 "+thisYear+"）\n已完成回信："+AtelierProgressCatalog.Completed(doc)+"\n帮过的朋友："+sent.Select(l=>l.sender).Distinct().Count()+"\n收集过的漂浮物："+caught+"\n收藏的页面："+favorites+"\n已有私人日记 / 收藏 / 自制素材："+(personal?"有":"还没有")+"\n当前最常用素材："+(string.IsNullOrEmpty(frequent)?"还在等第一张作品":frequent)+"\n\n这是本机单份存档的实际记录。D1 / D7 留存和个人创作玩家占比，需要真人样本与足够观察天数，当前不估算。",label);
                if(GUI.Button(new Rect(8,650,420,54),"导出本机足迹指标CSV",button))
                {try{status="已导出："+ExportMetrics(workspace);}catch(Exception error){status="导出未完成："+error.Message;}}
                GUI.Label(new Rect(8,724,rect.width-50,90),"CSV包含访问日期、会话与导出事件，不包含私人日记正文或图片。它不会自动上传。年度双页和作品图片可以在个人创作的分享页导出。",label);
            }
            GUI.EndScrollView();GUI.Label(new Rect(0,rect.height-44,rect.width,44),status??"",label);GUI.EndGroup();
        }
        public static string ExportMetrics(WorkspaceGameplayController workspace)
        {
            if(!workspace||!workspace.Ready)throw new InvalidOperationException("工房尚未准备。");
            var doc=workspace.Service.Document;var data=doc.atelierProgress;if(data==null)throw new InvalidOperationException("还没有足迹记录。");
            var rows=new List<string>{"record_type,id,timestamp_or_date,page_id,value"};
            rows.Add("metric,visit_days,,,"+data.visitDates.Count);
            rows.Add("metric,sessions,,,"+(data.sessions?.Count??0));
            rows.Add("metric,page_exports,,,"+(data.shares?.Count(s=>!s.isYearReview)??0));
            rows.Add("metric,year_review_exports,,,"+(data.shares?.Count(s=>s.isYearReview)??0));
            rows.Add("metric,completed_commissions,,,"+AtelierProgressCatalog.Completed(doc));
            rows.Add("metric,favorite_pages,,,"+(doc.personalStudio?.pageNotes?.Count(n=>n.favorite)??0));
            foreach(string date in data.visitDates.OrderBy(d=>d,StringComparer.Ordinal))rows.Add("visit_date,,"+Csv(date)+",,1");
            if(data.sessions!=null)foreach(var session in data.sessions.OrderBy(s=>s.utcTicks))rows.Add("session,"+Csv(session.id)+","+Csv(new DateTime(session.utcTicks,DateTimeKind.Utc).ToString("O",CultureInfo.InvariantCulture))+",,1");
            if(data.shares!=null)foreach(var share in data.shares.OrderBy(s=>s.utcTicks))rows.Add((share.isYearReview?"year_review":"page_export")+","+Csv(share.id)+","+Csv(new DateTime(share.utcTicks,DateTimeKind.Utc).ToString("O",CultureInfo.InvariantCulture))+","+Csv(share.pageId)+",1");
            string path=Path.Combine(StudioExports.Folder(workspace),"local-footprints-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".csv");
            File.WriteAllText(path,string.Join("\r\n",rows)+"\r\n",new UTF8Encoding(true));return path;
        }
        static string Csv(string value)
        {
            value=value??string.Empty;if(value.Length>0&&"=+-@".IndexOf(value[0])>=0)value="'"+value;
            return "\""+value.Replace("\"","\"\"")+"\"";
        }
    }
}
