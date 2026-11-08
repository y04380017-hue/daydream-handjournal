using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace VintageJournal
{
    public sealed class ReferenceRoomProbe:MonoBehaviour
    {
        [Serializable] public sealed class Row{public string name,detail;public bool passed;}
        [Serializable] public sealed class Report{public bool passed,nativeInputValidated;public string utc,savePath;public Row[] checks;public string[] errors;}
        public string output;public bool restart;public Action<bool> completed;
        readonly List<Row> rows=new List<Row>();readonly List<string> errors=new List<string>();readonly Stack<IEnumerator> stack=new Stack<IEnumerator>();
        WorkspaceGameplayController w;DeskReferenceBindings r;DeskLayoutBindings layout;bool finished;float started;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]static void Install(){var a=Environment.GetCommandLineArgs();int p=Array.IndexOf(a,"--reference-room-probe");if(p<0||p+1>=a.Length||!a.Contains("--desk-revision-validation"))return;var v=new GameObject("Reference room evidence").AddComponent<ReferenceRoomProbe>();v.output=Path.GetFullPath(a[p+1]);v.restart=a.Contains("--reference-room-restart");Application.runInBackground=true;}
        void Start(){Directory.CreateDirectory(output);started=Time.realtimeSinceStartup;Application.logMessageReceived+=Log;stack.Push(Run());}
        void Log(string s,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(s+"\n"+trace);}
        void Update(){if(finished)return;try{if(Time.realtimeSinceStartup-started>210)throw new TimeoutException("Reference probe timeout");while(stack.Count>0){var e=stack.Peek();if(!e.MoveNext()){stack.Pop();continue;}if(e.Current is IEnumerator child){stack.Push(child);continue;}return;}Finish();}catch(Exception e){errors.Add(e.ToString());rows.Add(new Row{name="Harness exception",detail=e.Message,passed=false});Finish();}}
        void Check(string n,bool ok,string detail=""){rows.Add(new Row{name=n,passed=ok,detail=detail});Debug.Log("[ReferenceProbe] "+(ok?"PASS ":"FAIL ")+n+" "+detail);if(!ok)throw new InvalidOperationException(n+" "+detail);}
        IEnumerator Wait(float t){float end=Time.realtimeSinceStartup+t;while(Time.realtimeSinceStartup<end)yield return null;}
        IEnumerator Idle(){float end=Time.realtimeSinceStartup+12;while((w.physical.IsSettling||w.physical.HeldItem||w.journal.IsBusy||w.journal.cameraRig.IsTransitioning||r.stockIndex.Busy)&&Time.realtimeSinceStartup<end)yield return null;yield return null;}
        IEnumerator View(DeskViewKind kind){if(w.journal.IsEditing){w.journal.ExitEditing();yield return Idle();}MaterialDiscoveryPresentation.Instance?.Close();Check("Controlled view "+kind,w.journal.cameraRig.SelectDeskView(kind));yield return Idle();}
        Texture2D Render(){var rt=new RenderTexture(1280,720,24);var tex=new Texture2D(1280,720,TextureFormat.RGB24,false);var prior=RenderTexture.active;try{RenderPipeline.SubmitRenderRequest(w.lens,new RenderPipeline.StandardRequest{destination=rt});RenderTexture.active=rt;tex.ReadPixels(new Rect(0,0,1280,720),0,0);tex.Apply();return tex;}finally{RenderTexture.active=prior;rt.Release();Destroy(rt);}}
        IEnumerator Shot(string name){yield return Wait(.12f);string path=Path.Combine(output,name+".png");if(!Application.isEditor){ScreenCapture.CaptureScreenshot(path);yield return Wait(.3f);}else{var t=Render();File.WriteAllBytes(path,t.EncodeToPNG());Destroy(t);}Check("Actual rendered screenshot "+name,File.Exists(path));}
        static Bounds BoundsOf(IEnumerable<Renderer> renderers){bool any=false;Bounds b=default;foreach(var v in renderers.Where(v=>v&&v.enabled&&v.gameObject.activeInHierarchy)){if(!any){b=v.bounds;any=true;}else b.Encapsulate(v.bounds);}return b;}
        static bool Opaque(Material m)=>m&&m.renderQueue<3000&&(!m.HasProperty("_Surface")||m.GetFloat("_Surface")<.01f)&&(!m.HasProperty("_ZWrite")||m.GetFloat("_ZWrite")>.99f);
        void DepthCheck(string label,Vector3 inside,float size)
        {
            var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);obj.name="Temporary GPU occlusion marker";
            Destroy(obj.GetComponent<Collider>());obj.transform.position=inside;obj.transform.localScale=Vector3.one*size;
            var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));material.SetColor("_BaseColor",Color.magenta);
            obj.GetComponent<Renderer>().sharedMaterial=material;obj.GetComponent<Renderer>().shadowCastingMode=ShadowCastingMode.Off;
            obj.SetActive(false);var baseline=Render();obj.SetActive(true);var hidden=Render();
            obj.transform.position=inside+(w.lens.transform.position-inside).normalized*.34f;var control=Render();
            // Calibrate against pixels actually rendered by the visible control. Editor Game View and
            // render-request aspect ratios can differ; screen projection is not the sampling authority.
            var a=baseline.GetPixels32();var b=hidden.GetPixels32();var c=control.GetPixels32();float h=0,f=0;int count=0;
            for(int i=0;i<c.Length;i++)if(c[i].r>230&&c[i].b>230&&c[i].g<25)
            {
                float difference=(Mathf.Abs(c[i].r-a[i].r)+Mathf.Abs(c[i].g-a[i].g)+Mathf.Abs(c[i].b-a[i].b))/765f;
                if(difference<.1f)continue;
                count++;f+=difference;h+=(Mathf.Abs(b[i].r-a[i].r)+Mathf.Abs(b[i].g-a[i].g)+Mathf.Abs(b[i].b-a[i].b))/765f;
            }
            h/=Mathf.Max(1,count);f/=Mathf.Max(1,count);
            File.WriteAllBytes(Path.Combine(output,label+"-depth-control.png"),control.EncodeToPNG());
            Check(label+" actually occludes a bright object behind its surface",count>=4&&h<.025f&&f>.08f,"control pixels="+count+"; hidden RGB diff="+h+"; front control diff="+f);
            obj.SetActive(false);Destroy(obj);Destroy(material);Destroy(baseline);Destroy(hidden);Destroy(control);
        }
        IEnumerator Run()
        {
            float until=Time.realtimeSinceStartup+35;while(!(w=FindFirstObjectByType<WorkspaceGameplayController>())||!w.Ready||!(r=FindFirstObjectByType<DeskReferenceBindings>())||!DreamGameplayController.Instance||!DreamGameplayController.Instance.Ready||!AtelierHub.Instance||!AtelierHub.Instance.Ready){if(Time.realtimeSinceStartup>until)throw new TimeoutException("Reference bindings not ready");yield return null;}
            layout=FindFirstObjectByType<DeskLayoutBindings>();foreach(var a in FindObjectsByType<WorkspaceInputRouter>(FindObjectsSortMode.None))a.enabled=false;foreach(var a in FindObjectsByType<PhysicalMouseInputAdapter>(FindObjectsSortMode.None))a.enabled=false;foreach(var a in FindObjectsByType<ScrapbookInput>(FindObjectsSortMode.None))a.enabled=false;
            RuntimeInputGate.Reset();w.desk.Begin();AtelierTutorial.Instance?.Close();yield return Wait(.5f);
            Check("Independent validation save",w.journal.SavePath.Contains("DeskRevisionValidation"));Check("Version two valid single ownership snapshot",w.Service.Data.deskLayoutVersion==2&&CraftWorkspaceData.Validate(w.Service.Document,out _));
            yield return Shot("reference-initial");
            if(restart){Check("Restart has one unique copy of each physical definition",layout.placements.All(p=>w.Service.Items.Count(i=>i.instanceId==p.item.itemInstanceId)==1));Check("Restart preserves all release pages",w.Service.Books.Count==2&&w.Service.Books.All(b=>b.pages.Count==6));Check("Restart preserves seven independent TN books",w.Service.Document.notebookLibrary.notebooks.Count==7);Check("Restart retains compact layout version",w.Service.Data.deskLayoutVersion==2);yield return Shot("restart");yield break;}
            Check("Earlier 135 objects retained and fifteen new exclusive rolls added",layout.placements.Length==150&&layout.placements.Select(p=>p.item.itemInstanceId).Distinct().Count()==150&&r.addedRolls.Length==15);
            Check("Thirty real rolls fill the reference rack",r.showcaseSlots.Length==30&&r.showcaseSlots.All(s=>s.Occupant&&s.Occupant.itemType==PhysicalItemType.Tape));
            Check("Pattern samples resolve to real rolls without becoming stock",r.sampleStripViews.Length==30&&r.sampleStripViews.All(s=>s.Source&&!s.GetComponent<PhysicalInteractable>()));
            Check("All release books start closed in lowest open shelf",layout.placements.Where(p=>p.item.itemType==PhysicalItemType.ReleaseBook).All(p=>p.item.CurrentSlot&&p.item.CurrentSlot.container==r.releaseShelf&&!w.Service.FindBook(p.releaseBookId).isOpen));
            Check("Eight real cloth pockets hold all 24 desktop notes",layout.placements.Count(p=>p.item.itemType==PhysicalItemType.Note&&p.homeSlot.transform.IsChildOf(r.bagRoot))==24&&r.bagRoot.GetComponentsInChildren<PhysicalContainer>().Length==9&&r.bagRoot.GetComponentsInChildren<MeshRenderer>().Length>16);
            foreach(var pair in new[]{new{label="Left",renderers=r.leftDrawerBody},new{label="Right",renderers=r.rightDrawerBody}}){var b=BoundsOf(pair.renderers);Check(pair.label+" drawer true outer bounds are sixty percent",Vector3.Distance(b.size,r.drawerExterior)<.018f,b.size.ToString("F4"));}
            Check("A5 refills retain original real scale",layout.placements.Where(p=>p.item.itemType==PhysicalItemType.LoosePage).All(p=>Mathf.Abs(p.item.transform.lossyScale.x-1)<.001f&&p.item.dimensions.size.x>=.10f));
            var box=BoundsOf(layout.desktopBox.GetComponentsInChildren<Renderer>());var centre=w.journal.transform.position;Check("Desktop box leaves a clear writing area",box.min.x>centre.x+.23f||box.max.x<centre.x-.23f||box.min.z>centre.z+.20f,box.ToString());
            Check("Dot glue has separate transparent three-dimensional shell",r.glueShellRenderers.Length==3&&r.glueShellRenderers.All(v=>v.GetComponent<MeshFilter>()&&v.GetComponent<MeshFilter>().sharedMesh.vertexCount>=20&&v.GetComponent<MeshFilter>().sharedMesh.bounds.size.y>.0001f&&v.sharedMaterials.Any(m=>m.HasProperty("_Surface")&&m.GetFloat("_Surface")>.5f))&&r.glueShellRenderers.Any(v=>v.GetComponent<MeshFilter>().sharedMesh.bounds.size.y>.015f));
            var solids=FindFirstObjectByType<DeskOpaqueSurfaceBindings>();Check("Mailbox and desk use depth-writing opaque body materials",solids&&solids.deskBodies.Length>0&&solids.mailboxBodies.Length>0&&solids.deskBodies.Concat(solids.mailboxBodies).All(v=>v.sharedMaterials.All(Opaque)));
            yield return View(DeskViewKind.Overview);
            foreach(var placement in layout.placements.Where(p=>p.item.itemType==PhysicalItemType.ReleaseBook))
            {
                var book=placement.item;var start=book.CurrentWorldPose;
                var destination=w.physical.surface.RestingPose(book,w.physical.surface.ToWorld(new Vector3(-.30f,0,-.12f)),Quaternion.identity);
                Check("Clear real desktop space exists for "+book.assetId,w.physical.surface.IsValidPlacement(book,destination));
                Check("Grab lowest-shelf "+book.assetId+" without initial lift",w.GrabPhysical(book)&&Vector3.Distance(start.position,book.transform.position)<.001f);
                Check("Move lowest-shelf "+book.assetId+" to tabletop",w.physical.Submit(new PhysicalInteractionIntent(PhysicalIntentKind.Move,book,destination)),w.physical.LastResult);
                yield return Wait(1.2f);
                Check("Release lowest-shelf "+book.assetId+" on tabletop",w.physical.Submit(new PhysicalInteractionIntent(PhysicalIntentKind.Release,book)),w.physical.LastResult);yield return Idle();
                Check("Lowest-shelf "+book.assetId+" settles as the same desktop instance",!w.physical.HeldItem&&Vector3.Distance(destination.position,book.transform.position)<.004f&&w.Service.GetItem(book.itemInstanceId).owner==CraftOwner.Desktop);
                Check("Return "+book.assetId+" to its actual shelf",w.coordinator.ReturnToHome(book));yield return Idle();
                Check("Returned "+book.assetId+" retains exclusive lowest shelf ownership",book.CurrentSlot==placement.homeSlot&&w.Service.GetItem(book.itemInstanceId).containerId==placement.homeSlot.slotId);
            }
            var guardedBook=layout.placements.First(p=>p.item.itemType==PhysicalItemType.ReleaseBook).item;
            var guardedStart=guardedBook.CurrentWorldPose;
            var exitObstacle=GameObject.CreatePrimitive(PrimitiveType.Cube);exitObstacle.name="Temporary external shelf-exit obstacle";
            exitObstacle.transform.position=new Vector3(guardedStart.position.x,guardedStart.position.y,.02f);exitObstacle.transform.localScale=new Vector3(.11f,.04f,.024f);Physics.SyncTransforms();
            var guardedDestination=w.physical.surface.RestingPose(guardedBook,w.physical.surface.ToWorld(new Vector3(-.30f,0,-.12f)),Quaternion.identity);
            Check("Grab release book for real exit-obstacle check",w.GrabPhysical(guardedBook));
            Check("Move request uses the configured shelf exit",w.physical.Submit(new PhysicalInteractionIntent(PhysicalIntentKind.Move,guardedBook,guardedDestination)));
            Check("Early release queues the real shelf route",w.physical.Submit(new PhysicalInteractionIntent(PhysicalIntentKind.Release,guardedBook)));yield return Wait(.5f);
            Check("Configured shelf route never tunnels through an external object",guardedBook.transform.position.z>exitObstacle.GetComponent<Collider>().bounds.max.z+guardedBook.HalfExtents.z-.002f&&w.Service.GetItem(guardedBook.itemInstanceId).owner!=CraftOwner.Desktop,guardedBook.transform.position.ToString("F4"));
            exitObstacle.SetActive(false);Destroy(exitObstacle);yield return Idle();
            Check("Removing the external obstacle lets the same queued book settle",!w.physical.HeldItem&&w.Service.GetItem(guardedBook.itemInstanceId).owner==CraftOwner.Desktop&&Vector3.Distance(guardedBook.transform.position,guardedDestination.position)<.004f);
            Check("Guarded route book can return to its own shelf",w.coordinator.ReturnToHome(guardedBook));yield return Idle();
            Check("Guarded route returns the same unique release book",guardedBook.CurrentSlot==guardedBook.homeSlot&&w.Service.Items.Count(i=>i.instanceId==guardedBook.itemInstanceId)==1);
            var cancelObstacle=GameObject.CreatePrimitive(PrimitiveType.Cube);cancelObstacle.name="Temporary partial-extraction cancel obstacle";
            cancelObstacle.transform.position=new Vector3(guardedStart.position.x,guardedStart.position.y,.02f);cancelObstacle.transform.localScale=new Vector3(.11f,.04f,.024f);Physics.SyncTransforms();
            Check("Start a partial shelf extraction for cancellation",w.GrabPhysical(guardedBook)&&w.physical.Submit(new PhysicalInteractionIntent(PhysicalIntentKind.Move,guardedBook,guardedDestination)));yield return Wait(.25f);
            Check("Cancel a partial shelf extraction",w.Cancel());yield return Wait(1.2f);
            Check("Partial extraction cancels backwards safely despite an obstacle in front",!w.physical.HeldItem&&!w.physical.IsSettling&&guardedBook.CurrentSlot==guardedBook.homeSlot&&w.Service.GetItem(guardedBook.itemInstanceId).owner==CraftOwner.Stock);
            cancelObstacle.SetActive(false);Destroy(cancelObstacle);yield return null;
            w.ClearSelection();
            FindFirstObjectByType<DreamTimeController>().SetNight(false,false,true);yield return Wait(.2f);yield return View(DeskViewKind.Desk);yield return Shot("reference-desk");
            DepthCheck("desk",w.physical.surface.ToWorld(new Vector3(-.27f,-.014f,-.25f)),.020f);
            yield return View(DeskViewKind.Overview);var mail=CommissionDeskController.Instance;DepthCheck("mailbox",mail.mailboxRoot.TransformPoint(new Vector3(-.091f,.945f,.035f)),.008f);
            yield return View(DeskViewKind.LeftDrawer);layout.leftDrawer.SetOpenAmount(1);layout.rightDrawer.SetOpenAmount(1);yield return Wait(.2f);yield return Shot("compact-left-drawer");
            int inventory=w.Service.Items.Count;
            foreach(var type in new[]{PhysicalItemType.LoosePage,PhysicalItemType.Tape,PhysicalItemType.Sticker,PhysicalItemType.Note,PhysicalItemType.Tool})
            {
                bool left=type==PhysicalItemType.LoosePage||type==PhysicalItemType.Tape;
                var drawer=left?layout.leftDrawer:layout.rightDrawer;yield return View(left?DeskViewKind.LeftDrawer:DeskViewKind.RightDrawer);
                var item=r.stockIndex.ItemsFor(drawer).FirstOrDefault(i=>i.itemType==type);Check("Dense stock directory lists "+type,item);
                Check("Request same-instance extraction "+type,r.stockIndex.RequestExtract(item));yield return Idle();
                Check("Extracted "+type+" is one actual stock instance",item.CurrentSlot&&r.stockIndex.extractionSlots.Contains(item.CurrentSlot)&&w.Service.GetItem(item.itemInstanceId).containerId==item.CurrentSlot.slotId&&w.Service.Items.Count==inventory,r.stockIndex.Status);
                if(type!=PhysicalItemType.Tool){yield return Wait(.12f);var card=MaterialDiscoveryPresentation.Instance;Check("Extracted "+type+" displays its actual pattern and halo",card&&card.IsOpen&&card.HaloVisible&&card.PreviewTexture&&card.AssetId==item.assetId);
                    if(type!=PhysicalItemType.Tape){Check("Flat "+type+" has a complete ring above its paper and the solid desk",card.UsesPaperPlane&&card.HaloWorldPoints.Length==72&&card.HaloWorldPoints.All(p=>p.y>item.interactionCollider.bounds.max.y)&&card.MinimumPaperPlaneClearance>.0005f);yield return Shot("flat-"+type.ToString().ToLowerInvariant()+"-halo");}}
                MaterialDiscoveryPresentation.Instance?.Close();Check("Return extracted "+type+" to original home",w.coordinator.ReturnToHome(item));yield return Idle();
                Check("Returned "+type+" retains its unique original slot",item.CurrentSlot==item.homeSlot&&w.Service.GetItem(item.itemInstanceId).containerId==item.homeSlot.slotId&&w.Service.Items.Count==inventory);
            }
            yield return View(DeskViewKind.LeftDrawer);
            var blockedPage=r.stockIndex.ItemsFor(layout.leftDrawer).First(i=>i.itemType==PhysicalItemType.LoosePage);
            var originalSlot=blockedPage.CurrentSlot;
            var pageTarget=r.stockIndex.extractionSlots.First(s=>s.acceptedType==PhysicalItemType.LoosePage);
            var obstruction=GameObject.CreatePrimitive(PrimitiveType.Cube);obstruction.name="Temporary external extraction obstacle";
            obstruction.transform.position=pageTarget.transform.position+Vector3.up*.015f;obstruction.transform.localScale=new Vector3(.164f,.06f,.224f);Physics.SyncTransforms();
            Check("Dense stock extraction can request a physically blocked target",r.stockIndex.RequestExtract(blockedPage));yield return Idle();
            Check("External obstacle cancels extraction without moving or duplicating stock",blockedPage.CurrentSlot==originalSlot&&w.Service.GetItem(blockedPage.itemInstanceId).containerId==originalSlot.slotId&&w.Service.Items.Count==inventory&&r.stockIndex.Status.Contains("占用"),r.stockIndex.Status);
            obstruction.SetActive(false);Destroy(obstruction);yield return null;
            yield return View(DeskViewKind.RightDrawer);
            yield return Shot("compact-right-drawer");yield return View(DeskViewKind.Desk);
            var tape=r.showcaseSlots.First().Occupant;var pose=tape.CurrentWorldPose;Check("Select real roll without moving it",w.SelectPhysical(tape));yield return Wait(.12f);var discovery=MaterialDiscoveryPresentation.Instance;
            Check("Material selection gives visible world halo and true pattern example",discovery&&discovery.IsOpen&&discovery.HaloVisible&&discovery.PreviewTexture&&discovery.AssetId==tape.assetId&&Vector3.Distance(pose.position,tape.transform.position)<.001f);
            yield return Shot("tape-pattern-discovery");discovery.Close();w.ClearSelection();
            yield return ReferenceCameraProbeSteps.Run(w,(n,p,d)=>Check(n,p,d),Shot);
            var dream=DreamGameplayController.Instance;var token=dream.Data.pending.First(t=>t.setting==dream.CurrentSetting);var collectible=dream.collection.Collectibles.FirstOrDefault(c=>c&&c.TokenId==token.tokenId);Check("Front reward target has real collider",collectible);
            var p0=w.lens.WorldToScreenPoint(collectible.transform.position);int count=w.Service.Items.Count;Check("Front world click grants exactly one finite material",dream.collection.TryClick(w.lens.ScreenPointToRay(p0))&&w.Service.Items.Count==count+1);yield return null;
            Check("Reward halo example and introduction correspond to earned instance",discovery.IsOpen&&discovery.HaloVisible&&discovery.PreviewTexture&&discovery.AssetId==token.assetId&&w.Service.GetItem(discovery.EarnedInstanceId)?.assetId==token.assetId&&MaterialDiscoveryPresentation.Description(token.assetId).Length>8);
            yield return Shot("special-material-introduction");discovery.Close();w.ClearSelection();
            Check("Final all-schema ownership validates and flushes",CraftWorkspaceData.Validate(w.Service.Document,out _)&&w.Service.Flush());
        }
        void Finish(){if(finished)return;finished=true;Application.logMessageReceived-=Log;bool pass=rows.Count>0&&rows.All(r=>r.passed)&&errors.Count==0;File.WriteAllText(Path.Combine(output,restart?"restart.json":"result.json"),JsonUtility.ToJson(new Report{passed=pass,nativeInputValidated=false,utc=DateTime.UtcNow.ToString("O"),savePath=w?w.journal.SavePath:"",checks=rows.ToArray(),errors=errors.ToArray()},true));if(completed!=null)completed(pass);else Application.Quit(pass?0:1);}
    }
}
