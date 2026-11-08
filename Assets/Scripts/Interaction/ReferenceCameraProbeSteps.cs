using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace VintageJournal
{
    /// <summary>Real runtime camera intents and renderer/Physics checks. This module never injects native input.</summary>
    public static class ReferenceCameraProbeSteps
    {
        static IEnumerator Idle(WorkspaceGameplayController w)
        {
            float until=Time.realtimeSinceStartup+8;
            while((w.journal.IsBusy||w.journal.cameraRig.IsTransitioning||w.physical.IsSettling)&&Time.realtimeSinceStartup<until)yield return null;
            yield return null;
        }
        static Vector2 Pixel(Rect virtualRect)=>new Vector2(virtualRect.center.x/1920*Screen.width,(1080-virtualRect.center.y)/1080*Screen.height);
        static bool Near(DeskCamera camera,float yaw)=>Mathf.Abs(Mathf.DeltaAngle(camera.SeatedYawDegrees,yaw))<.02f;
        static bool FixedOrigin(DeskCamera camera)=>Vector3.Distance(camera.transform.position,camera.DefaultPosition)<.0001f;
        static bool TryRealNearRay(WorkspaceGameplayController w,DreamGameplayController dream,out NightCollectible selected,out Ray ray)
        {
            selected=null;ray=default;Physics.SyncTransforms();
            foreach(var view in dream.collection.Collectibles.Where(v=>v&&v.gameObject.activeInHierarchy&&v.HitCollider&&v.HitCollider.enabled&&dream.Data.pending.Any(t=>t.tokenId==v.TokenId&&t.setting==dream.CurrentSetting))) {
                var bounds=view.HitCollider.bounds;
                foreach(var offset in new[]{Vector3.zero,Vector3.up*.6f,Vector3.left*.6f,Vector3.right*.6f}) {
                    var point=w.lens.WorldToViewportPoint(bounds.center+Vector3.Scale(bounds.extents,offset));
                    if(point.z<=0||point.x<.02f||point.x>.98f||point.y<.03f||point.y>.96f)continue;
                    var candidate=w.lens.ViewportPointToRay(point);
                    if(Physics.Raycast(candidate,out var hit,12,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)&&hit.collider.GetComponentInParent<NightCollectible>()==view){selected=view;ray=candidate;return true;}
                }
            }
            return false;
        }
        static bool RearRendererVisible(Camera lens,ScenicOnlyMarker marker)
        {
            foreach(var renderer in marker.GetComponentsInChildren<Renderer>()) {
                if(!renderer.enabled||!renderer.gameObject.activeInHierarchy||!GeometryUtility.TestPlanesAABB(GeometryUtility.CalculateFrustumPlanes(lens),renderer.bounds))continue;
                var p=lens.WorldToViewportPoint(renderer.bounds.center);
                if(p.z<=0||p.x<.03f||p.x>.97f||p.y<.04f||p.y>.94f)continue;
                var ray=lens.ViewportPointToRay(p);
                if(!Physics.Raycast(ray,p.z-.02f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))return true;
            }
            return false;
        }
        static float PinkFraction(Camera lens)
        {
            var target=new RenderTexture(96,54,24);var texture=new Texture2D(96,54,TextureFormat.RGB24,false);var previous=RenderTexture.active;
            try {
                RenderPipeline.SubmitRenderRequest(lens,new RenderPipeline.StandardRequest{destination=target});
                RenderTexture.active=target;texture.ReadPixels(new Rect(0,0,96,54),0,0);texture.Apply();
                var colors=texture.GetPixels();return colors.Count(c=>c.r>.5f&&c.b>.45f&&c.r-c.g>.018f&&c.b-c.g>.003f)/(float)colors.Length;
            } finally {RenderTexture.active=previous;target.Release();Object.Destroy(target);Object.Destroy(texture);}
        }
        public static IEnumerator Run(WorkspaceGameplayController w,Action<string,bool,string> check,Func<string,IEnumerator> shot)
        {
            var camera=w.journal.cameraRig;var router=w.GetComponent<WorkspaceInputRouter>();var dream=DreamGameplayController.Instance;
            check("Seated camera module starts with complete bindings",camera&&camera.profile&&router&&dream&&dream.Ready,"Actual runtime scene components.");
            check("Camera module begins without an active material transfer",!w.InteractionBusy,"No token or ownership rollback is forced by this module.");
            if(AtelierHub.Instance&&AtelierHub.Instance.IsOpen)AtelierHub.Instance.Close();
            if(dream.BagOpen)dream.CloseBag();if(dream.discovery)dream.discovery.Close();
            if(CommissionDeskController.Instance&&CommissionDeskController.Instance.PanelOpen)CommissionDeskController.Instance.ClosePanel();
            if(DeskViewControls.Instance&&DeskViewControls.Instance.Expanded)DeskViewControls.Instance.CloseMenu();
            if(DrawerStockIndex.Instance&&DrawerStockIndex.Instance.Expanded)DrawerStockIndex.Instance.Close();
            if(w.journal.IsEditing)w.journal.ExitEditing();yield return Idle(w);w.ClearSelection();
            check("Camera can request the fixed seated forward view",camera.SelectDeskView(DeskViewKind.Desk),"Independent intent request, not native input.");
            yield return Idle(w);camera.SetMouseLookEnabled(true);camera.ArmMouseLook();
            check("Initial forward pose has zero yaw and the fixed origin",Near(camera,0)&&FixedOrigin(camera)&&camera.ForwardFacing,"Position "+camera.transform.position.ToString("F4"));
            check("Camera profile enables continuous seated 360 look",camera.CanLook&&camera.profile.seatedYawDegreesPerSecond>0,"Edge rate "+camera.profile.seatedYawDegreesPerSecond+" degrees/second.");
            check("Public view controls and drawer entry do not overlap",!DeskViewControls.ToggleRect.Overlaps(DrawerStockIndex.EntryRect)&&!DeskViewControls.MouseLookRect.Overlaps(DrawerStockIndex.EntryRect)&&!DeskViewControls.ForwardRect.Overlaps(DrawerStockIndex.EntryRect)&&!DeskViewControls.LookHintRect.Overlaps(DrawerStockIndex.EntryRect),"Cross-component authored rectangles.");
            if(dream.discovery)check("Look instructions do not cover the material introduction card",!DeskViewControls.LookHintRect.Overlaps(dream.discovery.CardRect),"Current introduction card rectangle.");
            var adapter=new SeatedLookMouseAdapter();
            check("Left edge produces a negative yaw intent",adapter.TrySample(new Vector2(1,Screen.height*.5f),new Vector2(Screen.width,Screen.height),.016f,camera.profile,out var left)&&left.degrees.x<0,"Adapter only; hardware input is tested separately.");
            check("Pointer center produces no camera rotation intent",!adapter.TrySample(new Vector2(Screen.width*.5f,Screen.height*.5f),new Vector2(Screen.width,Screen.height),.016f,camera.profile,out _),"Ordinary material pointing is stable.");
            dream.timeOfDay.SetDusk(false,false,true);dream.timeOfDay.SetNight(false,false,true);
            check("Natural daytime collection pool can initialize",w.Service.RefreshSettingCollection(1,DateTime.UtcNow.Ticks),"Uses the real current clock; no fabricated token or future refill.");
            NightCollectible near=null;Ray nearRay=default;float until=Time.realtimeSinceStartup+5;
            while(!TryRealNearRay(w,dream,out near,out nearRay)&&Time.realtimeSinceStartup<until)yield return null;
            check("An existing real daytime token has an unobstructed collider ray",near,"The camera test does not claim this pending token.");
            string tokenId=near.TokenId;Vector3 tokenPoint=near.transform.position;
            check("Forward token position satisfies the configured collection cone",camera.CanCollectDirection(tokenPoint),"World-position and current-facing constraints both apply.");
            check("Look intent turns left sixty-five degrees",camera.SubmitLook(new DeskLookIntent(new Vector2(-65,0))),"Accepted relative intent.");
            yield return null;check("Left view keeps the eye at its original seated point",Near(camera,-65)&&FixedOrigin(camera),"yaw="+camera.SeatedYawDegrees);yield return shot("seated-look-left-65");
            check("Look intent crosses from left to right",camera.SubmitLook(new DeskLookIntent(new Vector2(130,0))),"Relative +130 degrees.");
            yield return null;check("Right view keeps the same seated point",Near(camera,65)&&FixedOrigin(camera),"yaw="+camera.SeatedYawDegrees);yield return shot("seated-look-right-65");
            check("Look intent reaches behind without orbiting",camera.SubmitLook(new DeskLookIntent(new Vector2(115,0))),"Relative +115 degrees.");
            yield return null;Physics.SyncTransforms();
            check("Back view is one hundred eighty degrees with no position change",Near(camera,180)&&FixedOrigin(camera)&&!camera.ForwardFacing,"yaw="+camera.SeatedYawDegrees);
            var scenic=Object.FindObjectsByType<ScenicOnlyMarker>(FindObjectsSortMode.None);
            check("Rear scenery is geometric and has no collection or physical collider",scenic.Length>=3&&scenic.All(s=>s.GetComponentsInChildren<Collider>(true).Length==0&&s.GetComponentsInChildren<PhysicalInteractable>(true).Length==0&&s.GetComponentsInChildren<FloatingMaterialTarget>(true).Length==0),"Scenic marker count="+scenic.Length);
            check("A rear three-dimensional renderer projects visibly in the real camera",scenic.Any(s=>RearRendererVisible(w.lens,s)),"Renderer frustum plus an unobstructed actual Physics ray.");
            float pink=PinkFraction(w.lens);check("Rear GPU render contains the pink sky palette",pink>.025f,"Pink pixel fraction="+pink.ToString("F3"));yield return shot("seated-look-back-180");
            int stock=w.Service.Items.Count,pending=dream.Data.pending.Count;
            check("Back-facing direct catch rejects an existing front token",!dream.TryCatch(tokenId,tokenPoint),"Same natural pending token remains available.");
            check("Back-facing ray catch rejects the real front collider ray",!dream.collection.TryClick(nearRay),"No bypass by supplying an old forward ray.");
            var rear=scenic.SelectMany(s=>s.GetComponentsInChildren<Renderer>()).First(r=>r.enabled&&w.lens.WorldToViewportPoint(r.bounds.center).z>0);
            check("Actual rear scenery click remains decorative",!dream.collection.TryClick(w.lens.ScreenPointToRay(w.lens.WorldToScreenPoint(rear.bounds.center))),"Rear objects carry no reward capability.");
            check("All back-facing attempts leave stock and pending identities unchanged",w.Service.Items.Count==stock&&dream.Data.pending.Count==pending&&dream.Data.pending.Any(t=>t.tokenId==tokenId),"No token was consumed or added.");
            check("Continuous look can complete a full revolution",camera.SubmitLook(new DeskLookIntent(new Vector2(180,0))),"Relative +180 reaches total 360.");
            yield return null;check("A full revolution normalizes yaw and keeps the original viewpoint",Near(camera,0)&&FixedOrigin(camera)&&camera.ForwardFacing,"yaw="+camera.SeatedYawDegrees);
            camera.SubmitLook(new DeskLookIntent(new Vector2(24,5)));yield return null;
            check("Reset-forward intent is accepted",camera.SubmitLook(new DeskLookIntent(DeskLookIntentKind.ResetForward)),"No position is teleported behind the desk.");
            check("Reset initially requires mouse rearming",camera.MouseLookAwaitingNeutral,"Held edge does not immediately rotate away again.");
            yield return Idle(w);camera.SetMouseLookEnabled(true);
            var neutral=new Vector2(Screen.width*.5f,Screen.height*.5f);
            check("Mouse look waits for a neutral pointer after reset",!router.CanDispatchSeatedLook(neutral)&&camera.MouseLookAwaitingNeutral,"Interaction gate rejects an unarmed mouse.");
            check("Neutral pointer matches the real adapter rearm area",adapter.IsNeutral(neutral,new Vector2(Screen.width,Screen.height),camera.profile),"Device-independent coordinate check.");camera.ArmMouseLook();
            check("Forward reset restores yaw pitch and position",Near(camera,0)&&Mathf.Abs(camera.SeatedPitchDegrees)<.02f&&FixedOrigin(camera),"Reset pose equals the configured seated origin.");
            check("UI hover freezes look through the shared ownership gate",!router.CanDispatchSeatedLook(Pixel(DeskViewControls.ToggleRect)),"Uses the public authored view-control rectangle.");
            check("Mouse button and scroll activity freeze look",!router.CanDispatchSeatedLook(neutral,true,false)&&!router.CanDispatchSeatedLook(neutral,false,true),"Logical producer gate; no native input claim.");
            var tape=Object.FindObjectsByType<PhysicalInteractable>(FindObjectsSortMode.None).FirstOrDefault(i=>i.itemType==PhysicalItemType.Tape&&i.CurrentState!=PhysicalItemState.Disabled&&(!i.CurrentContainer||i.CurrentContainer.IsAccessibleHierarchy)&&w.Service.GetItem(i.itemInstanceId)?.owner==CraftOwner.Stock);
            check("A real finite roll is available for the tape-mode gate test",tape,"The roll is selected, never cut or consumed.");
            check("Real tape length mode can start safely",w.SelectPhysical(tape)&&w.StartTapeLength(tape.transform.position),"No strip is confirmed.");
            check("Tape length interaction freezes the shared look adapter",!router.CanDispatchSeatedLook(neutral),"Actual workspace.TapeLengthSelecting state.");
            w.Cancel();w.ClearSelection();if(dream.discovery)dream.discovery.Close();yield return null;
            foreach(DeskViewKind kind in Enum.GetValues(typeof(DeskViewKind))) {
                check("Controlled view accepts request "+kind,camera.SelectDeskView(kind),"Named view remains available alongside 360 look.");yield return Idle(w);
                check("Controlled view resets look angles "+kind,camera.ActiveDeskView==kind&&!camera.IsTransitioning&&Near(camera,0)&&Mathf.Abs(camera.SeatedPitchDegrees)<.02f,"Camera controller owns the transition.");
            }
            check("Camera module returns to default desk",camera.SelectDeskView(DeskViewKind.Desk),"Leaves downstream tests in forward desk mode.");yield return Idle(w);camera.ArmMouseLook();
            check("Final camera is forward at the original seated point",FixedOrigin(camera)&&Near(camera,0)&&camera.ForwardFacing,"No pending natural token was claimed by this module.");
        }
    }
}
