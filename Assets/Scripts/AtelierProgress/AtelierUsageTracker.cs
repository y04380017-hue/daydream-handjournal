using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VintageJournal
{
    public sealed partial class CraftWorkspaceService
    {
        public bool RecordAtelierSession(string sessionId,long utcTicks)
        {
            if(string.IsNullOrWhiteSpace(sessionId)||utcTicks<0||utcTicks>DateTime.MaxValue.Ticks)return Fail("会话记录无效。");
            if(!InitializeAtelierProgress(utcTicks))return false;
            if(Document.atelierProgress.sessions?.Exists(s=>s.id==sessionId)==true)return true;
            var candidate=Document.DeepClone();candidate.atelierProgress.sessions??=new List<AtelierSessionRecord>();
            candidate.atelierProgress.sessions.Add(new AtelierSessionRecord{id=sessionId,utcTicks=utcTicks,dateKey=AtelierProgressCatalog.DateId(new DateTime(utcTicks,DateTimeKind.Utc).ToLocalTime())});
            return Commit(candidate,"记录本次工房访问",false);
        }
        public bool RecordAtelierShare(string pageId,long utcTicks,bool yearReview)
        {
            if(string.IsNullOrWhiteSpace(pageId)||utcTicks<0||utcTicks>DateTime.MaxValue.Ticks)return Fail("导出记录无效。");
            if(!InitializeAtelierProgress(utcTicks))return false;
            var candidate=Document.DeepClone();candidate.atelierProgress.shares??=new List<AtelierShareRecord>();
            candidate.atelierProgress.shares.Add(new AtelierShareRecord{id=Guid.NewGuid().ToString("N"),pageId=pageId,utcTicks=utcTicks,isYearReview=yearReview,dateKey=AtelierProgressCatalog.DateId(new DateTime(utcTicks,DateTimeKind.Utc).ToLocalTime())});
            return Commit(candidate,"记录本次图片导出",false);
        }
    }
    /// <summary>Opt-in local usage records only. No network or inferred player-retention claims.</summary>
    public sealed class AtelierUsageTracker:MonoBehaviour
    {
        public WorkspaceGameplayController workspace;
        IEnumerator Start()
        {
            var args=Environment.GetCommandLineArgs();
            if(Array.Exists(args,a=>a.Contains("validation")||a.Contains("probe"))&&!Array.Exists(args,a=>a=="--atelier-full-validation"))yield break;
            while(!workspace||!workspace.Ready)yield return null;
            workspace.Service.RecordAtelierSession(Guid.NewGuid().ToString("N"),DateTime.UtcNow.Ticks);
        }
    }
}
