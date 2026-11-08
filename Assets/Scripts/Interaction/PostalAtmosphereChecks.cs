using System.Collections;
using System.Linq;
using UnityEngine;

namespace VintageJournal
{
    public sealed partial class DeskRevisionProbe
    {
        IEnumerator CheckPostalAtelier(DreamTimeController cycle,DreamSkyPasser[] passers)
        {
            var atmosphere=cycle.atmosphere;
            var mail=atmosphere.nightMail;
            var day=passers.Where(m=>m.dayOnly).ToArray();
            var origin=w.lens.transform.position;
            Check("Three independently authored postal silhouettes",mail.Length==3&&mail.Select(p=>p.visual).Distinct().Count()==3&&mail.All(p=>p.nightOnly&&p.resetOnNightfall));
            Check("No decorative collider or gameplay entity in the new lighting and mail",!atmosphere.GetComponentInChildren<Collider>(true)&&!atmosphere.GetComponentInChildren<PhysicalInteractable>(true));
            foreach(var p in passers){p.enabled=false;p.Sample(p.duration*.5f-p.phaseSeconds);}
            Check("Only night postal passers can appear after dark",day.Length==7&&day.All(p=>!p.IsVisible)&&mail.All(p=>p.IsVisible));
            // 2026-11-07 dreamcore night: the pool over the book stays cream (warm), the side wash turns lavender; without dreamcore the old warm rule applies.
            var pool=cycle.dreamcore?atmosphere.overheadLantern:atmosphere.leftSoftbox;
            Check(cycle.dreamcore?"Soft night lights reach both sides; cream pool over the book":"Warm desk lights reach both sides and the book",atmosphere.leftSoftbox.enabled&&atmosphere.overheadLantern.enabled&&atmosphere.edgeLight.enabled&&pool.color.r>pool.color.b&&cycle.workLamp.intensity>.10f);
            Check("Daytime keepsakes disappear outside the night desk",atmosphere.daytimeKeepsakes.Length>0&&atmosphere.daytimeKeepsakes.All(r=>!r.enabled));
            Check("Glass shaders supported by active graphics device",atmosphere.phaseSurfaces.All(r=>r&&r.sharedMaterial.shader.isSupported&&r.sharedMaterial.shader.name!="Hidden/InternalErrorShader"));
            foreach(var p in mail)p.Sample(p.duration+1-p.phaseSeconds);
            string[] names={"lantern-paper-boat","mail-airship","glowing-stamp"};
            for(int i=0;i<3;i++)
            {
                var p=mail[i];float fraction=i==1?.58f:.43f;
                p.Sample(p.duration*fraction-p.phaseSeconds);
                var v=w.lens.WorldToViewportPoint(p.visual.position);
                Check("Visible postal route at seated view: "+names[i],p.IsVisible&&v.x>.02f&&v.x<.98f&&v.y>.45f&&v.y<.95f,v.ToString("F2"));
                yield return Shot(names[i]);p.Sample(p.duration+1-p.phaseSeconds);
            }
            for(int sec=0;sec<108;sec++)
            {
                foreach(var p in mail)p.Sample(sec);
                if(mail.Count(p=>p.IsVisible)>1){Check("Night routes do not crowd the same passage",false,"second "+sec);yield break;}
            }
            Check("One night craft at most in a full 108 second schedule",true);
            cycle.SetDusk(true,false,true);yield return null;
            foreach(var p in passers)p.Sample(p.duration*.5f-p.phaseSeconds);
            Check("Dusk restores daytime props and clears warm night fixtures",cycle.IsDusk&&mail.All(p=>!p.IsVisible)&&day.All(p=>p.IsVisible)&&!atmosphere.nightFixtures.activeSelf);
            cycle.SetNight(false,false,true);yield return null;
            Check("Returning to day restores palette, stationary desk and daylight",cycle.sun.color==cycle.daySun&&Vector3.Distance(w.lens.transform.position,origin)<.001f&&!atmosphere.leftSoftbox.enabled&&atmosphere.daytimeKeepsakes.All(r=>r.enabled));
            foreach(var p in passers)p.enabled=true;
        }
    }
}
