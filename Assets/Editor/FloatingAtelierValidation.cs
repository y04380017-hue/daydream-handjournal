using System.Collections;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VintageJournal.Editor
{
    public static partial class WorkspaceStage4BValidation
    {
        static IEnumerator ValidateFloatingAtelier()
        {
            var cycle=UnityEngine.Object.FindFirstObjectByType<DreamTimeController>();
            var movers=UnityEngine.Object.FindObjectsByType<DreamSkyPasser>(FindObjectsSortMode.None);
            var cameraPosition=lens.transform.position;var cameraRotation=lens.transform.rotation;
            var homes=items.ToDictionary(i=>i,i=>i.transform.position);
            cycle.SetNight(false,false,true);yield return Wait(.25);
            float daySun=cycle.sun.intensity,dayLamp=cycle.workLamp.intensity;
            foreach(var mover in movers){mover.enabled=false;mover.Sample(mover.duration*.5f-mover.phaseSeconds);}
            Capture("floating-day.png");
            Check("Dream01","All decorative paths stay behind the interactive desk",movers.Length==(cycle.atmosphere?10:GameObject.Find("Desk revision 20261107")?9:3) && movers.All(m=>
                m.from.z>=3 && m.to.z>=3 && !m.GetComponentsInChildren<Collider>(true).Any(c=>c.enabled) &&
                !m.GetComponentInChildren<PhysicalInteractable>(true)),"Balloon, paper boat and meteor have no collision or ownership.");
            foreach(var mover in movers)
            {
                mover.Sample(0-mover.phaseSeconds);Vector3 first=mover.transform.position;
                mover.Sample(mover.duration*.5f-mover.phaseSeconds);Vector3 second=mover.transform.position;
                bool moves=Vector3.Distance(first,second)>1 || mover.nightOnly;
                mover.Sample(mover.duration+1-mover.phaseSeconds);bool absent=!mover.IsVisible;
                mover.Sample(mover.period+mover.duration*.5f-mover.phaseSeconds);
                Check("Dream02","Timed pass and quiet interval: "+mover.name,moves&&absent&&(mover.nightOnly||mover.IsVisible),"Tested positions on both sides of one full cycle.");
            }
            cycle.SetNight(true,false);yield return Wait(.7);
            Check("Dream03","Day/night blend transitions smoothly",cycle.NightAmount>0&&cycle.NightAmount<1&&cycle.IsTransitioning,"Transition uses unscaled time.");
            yield return Wait(cycle.NightTransitionDuration+.2);
            foreach(var mover in movers)mover.Sample(mover.duration*.5f-mover.phaseSeconds);
            Capture("floating-night.png");
            Check("Dream04","Night reduces sunlight and strengthens the reading lamp",cycle.IsNight&&!cycle.IsTransitioning&&cycle.sun.intensity<daySun*.5f&&cycle.workLamp.intensity>dayLamp*3&&cycle.moon.activeSelf,
                "Sun="+cycle.sun.intensity+" lamp="+cycle.workLamp.intensity);
            Check("Dream05","All night-only passers appear",movers.Any(m=>m.nightOnly)&&movers.Where(m=>m.nightOnly).All(m=>m.IsVisible),"Live GameObject active in night mode.");
            CheckShaftSightlines("night-closed");
            Check("Dream06","Day/night and sky motion leave camera and stored items stable",Vector3.Distance(lens.transform.position,cameraPosition)<.0001f&&
                Quaternion.Angle(lens.transform.rotation,cameraRotation)<.01f&&homes.All(p=>Vector3.Distance(p.Key.transform.position,p.Value)<.0001f),"Fixed seat and all physical anchors sampled through the transition.");
            var catalog=journal.Catalog;
            bool complete=Enumerable.Range(1,24).All(i=>{
                string id="page_"+i.ToString("00");var definition=catalog.Find(id);
                return definition!=null&&definition.texture&&definition.texture.width>=1024&&
                    Resources.Load<Texture2D>("DaydreamPapers/"+id)==definition.texture;
            });
            Check("Dream07","24 stable paper IDs resolve to new large artwork",complete,"Resource catalog, paper catalog and source images agree.");
            bool thumbnails=ItemCatalog.Load().items.Where(i=>i.category=="page").All(i=>catalog.Find(i.id)?.texture==i.Thumbnail);
            Check("Dream08","Page thumbnails match the applied paper texture",thumbnails,"No stale floral thumbnails for page_01..24.");
            journal.EnterEditing();yield return JournalIdle();
            journal.BeginPaperSelection(JournalSide.Left);journal.SelectPaper("page_02");journal.ConfirmPaper();
            journal.BeginPaperSelection(JournalSide.Right);journal.SelectPaper("page_03");journal.ConfirmPaper();
            journal.ZoomBy(.52f);yield return Wait(.8);Capture("floating-night-pages.png");
            Check("Dream09","New paper can be selected independently during night mode",journal.Document.GetPage(JournalSide.Left).paperId=="page_02"&&journal.Document.GetPage(JournalSide.Right).paperId=="page_03",
                "Existing page picker transaction and IDs retained.");
            cycle.SetNight(false,false);yield return Wait(cycle.NightTransitionDuration+.2);Capture("floating-day-pages.png");
            journal.BeginPaperSelection(JournalSide.Left);journal.SelectPaper(catalog.defaultPaperId);journal.ConfirmPaper();
            journal.BeginPaperSelection(JournalSide.Right);journal.SelectPaper(catalog.defaultPaperId);journal.ConfirmPaper();
            journal.ExitEditing();yield return JournalIdle();
            foreach(var mover in movers)mover.enabled=true;
        }
    }
}
