using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace VintageJournal
{
    /// <summary>Opt-in actual scene transactions and GPU checks. Native input is a separate acceptance step.</summary>
    public sealed class DeskUsabilityProbe : MonoBehaviour
    {
        [Serializable] public sealed class Row { public string name, detail; public bool passed; }
        [Serializable] public sealed class Report { public bool passed, actualPlayMode, nativeInputValidated; public string utc, unity, gpu, savePath; public Row[] checks; public string[] errors; }
        [Serializable] public sealed class Expected { public string releaseId, caughtId; public Vector3 releasePosition; public int releasePage, notebookSpread; }
        [Serializable] public sealed class SkyRow { public string name, firstHit; public bool active, eligible; public int layer; public Vector3 colliderCenter, colliderSize, viewport; }
        [Serializable] public sealed class SkyAudit { public string utc; public Vector3 eye, forward; public SkyRow[] targets; }
        public string output;
        public bool restart;
        public Action<bool> completed;
        WorkspaceGameplayController w; DeskLayoutBindings layout; JournalController j; NotebookWorkbench work;
        readonly List<Row> checks = new List<Row>(); readonly List<string> errors = new List<string>();
        readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
        bool finished; float started;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "--desk-usability-probe");
            if (index < 0 || index + 1 >= args.Length || !args.Contains("--desk-revision-validation")) return;
            var p = new GameObject("Desk usability evidence").AddComponent<DeskUsabilityProbe>();
            p.output = Path.GetFullPath(args[index + 1]); p.restart = args.Contains("--desk-usability-restart"); Application.runInBackground = true;
        }
        void Start() { Directory.CreateDirectory(output); started = Time.realtimeSinceStartup; Application.logMessageReceived += Log; stack.Push(Run()); }
        void Log(string text, string trace, LogType type) { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(text + "\n" + trace); }
        void Update()
        {
            if (finished) return;
            if (Time.realtimeSinceStartup - started > 180) { Check("Harness timeout", false); Finish(); return; }
            try { while (stack.Count > 0) { var e = stack.Peek(); if (!e.MoveNext()) { stack.Pop(); continue; } if (e.Current is IEnumerator child) { stack.Push(child); continue; } return; } Finish(); }
            catch (Exception error) { errors.Add(error.ToString()); Check("Harness exception", false, error.Message); Finish(); }
        }
        void Check(string name, bool pass, string detail = "") { checks.Add(new Row { name = name, passed = pass, detail = detail }); Debug.Log("[DeskUsabilityProbe] " + (pass ? "PASS " : "FAIL ") + name + " " + detail); }
        void Require(string name, bool pass, string detail = "") { Check(name, pass, detail); if (!pass) throw new InvalidOperationException(name + " " + detail); }
        IEnumerator Wait(float seconds) { float until = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < until) yield return null; }
        IEnumerator Idle() { float until = Time.realtimeSinceStartup + 12; while ((w.physical.IsSettling || j.IsBusy || j.cameraRig.IsTransitioning) && Time.realtimeSinceStartup < until) yield return null; yield return null; }
        IEnumerator Reading(string kind)
        {
            float until=Time.realtimeSinceStartup+5;
            while(!w.bookInspection.ReadyForReading&&Time.realtimeSinceStartup<until)yield return null;
            Require(kind+" inspection reaches configured near-view pose",w.bookInspection.ReadyForReading);
            bool visible=w.bookInspection.TryGetReadingViewport(out var rect);
            Require(kind+" real page corners face the camera at readable size",visible&&rect.width>.15f&&rect.height>.18f&&rect.height/rect.width>.5f,rect.ToString());
        }
        IEnumerator Shot(string name)
        {
            yield return Wait(.15f); string file = Path.Combine(output, name + ".png");
            if (!Application.isEditor) { ScreenCapture.CaptureScreenshot(file); yield return Wait(.3f); }
            else
            {
                var target = new RenderTexture(1920, 1080, 24); var texture = new Texture2D(1920, 1080, TextureFormat.RGB24, false); var previous = RenderTexture.active;
                try { RenderPipeline.SubmitRenderRequest(w.lens, new RenderPipeline.StandardRequest { destination = target }); RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0); texture.Apply(); File.WriteAllBytes(file, texture.EncodeToPNG()); }
                finally { RenderTexture.active = previous; target.Release(); Destroy(target); Destroy(texture); }
            }
            Require("GPU screenshot " + name, File.Exists(file));
        }
        bool HitItem(PhysicalInteractable item)
        {
            if (!item || !item.interactionCollider || !item.interactionCollider.enabled) return false;
            var bounds = item.interactionCollider.bounds;
            foreach (var offset in new[] { Vector3.zero, Vector3.right * .7f, Vector3.left * .7f, Vector3.forward * .7f, Vector3.back * .7f, Vector3.up * .7f })
            {
                var point = w.lens.WorldToViewportPoint(bounds.center + Vector3.Scale(bounds.extents, offset));
                if (point.z <= 0 || point.x < .01f || point.x > .99f || point.y < .03f || point.y > .97f) continue;
                if (Physics.Raycast(w.lens.ViewportPointToRay(point), out var hit, 12) && hit.collider.GetComponentInParent<PhysicalInteractable>() == item) return true;
            }
            return false;
        }
        IEnumerator View(DeskViewKind kind)
        { Require("Request controlled view " + kind, j.cameraRig.SelectDeskView(kind)); yield return Idle(); Require("Camera reaches " + kind, j.cameraRig.ActiveDeskView == kind && !j.cameraRig.IsTransitioning); }
        void AuditSky(FloatingMaterialTarget[] targets,string file)
        {
            var rows=new List<SkyRow>();
            foreach(var target in targets)
            {
                var bounds=target.hitCollider.bounds;var p=w.lens.WorldToViewportPoint(bounds.center);
                var ray=w.lens.ViewportPointToRay(p);string first=Physics.Raycast(ray,out var hit,12)?hit.collider.name+" layer="+hit.collider.gameObject.layer:"none";
                rows.Add(new SkyRow{name=target.passer.name,active=target.passer.IsVisible,eligible=target.CanClaim(1),layer=target.gameObject.layer,
                    colliderCenter=bounds.center,colliderSize=bounds.size,viewport=p,firstHit=first});
            }
            File.WriteAllText(Path.Combine(output,file),JsonUtility.ToJson(new SkyAudit{utc=DateTime.UtcNow.ToString("O"),eye=w.lens.transform.position,forward=w.lens.transform.forward,targets=rows.ToArray()},true));
        }
        IEnumerator Run()
        {
            float until = Time.realtimeSinceStartup + 35;
            while (!(w = FindFirstObjectByType<WorkspaceGameplayController>()) || !w.Ready || !(layout = FindFirstObjectByType<DeskLayoutBindings>()) || !(j = w.journal) ||
                !DreamGameplayController.Instance || !DreamGameplayController.Instance.Ready || !(work = FindFirstObjectByType<NotebookWorkbench>()) || !work.Ready)
            { if (Time.realtimeSinceStartup > until) throw new TimeoutException("Usability scene did not initialize"); yield return null; }
            foreach (var input in FindObjectsByType<WorkspaceInputRouter>(FindObjectsSortMode.None)) input.enabled = false;
            foreach (var input in FindObjectsByType<PhysicalMouseInputAdapter>(FindObjectsSortMode.None)) input.enabled = false;
            foreach (var input in FindObjectsByType<ScrapbookInput>(FindObjectsSortMode.None)) input.enabled = false;
            RuntimeInputGate.Reset(); w.desk.Begin(); if (AtelierTutorial.Instance && AtelierTutorial.Instance.IsOpen) AtelierTutorial.Instance.Close(); yield return Wait(.5f);
            Require("Probe uses isolated save", j.SavePath.Contains("DeskRevisionValidation"), j.SavePath);
            var mail=CommissionDeskController.Instance;
            Require("View switch and existing mail entry have disjoint UI rectangles",mail&&!DeskViewControls.ToggleRect.Overlaps(mail.PanelRect));
            bool valid = CraftWorkspaceData.Validate(j.Document, out var error);
            Require("Atomic original schema valid", j.Document.schemaVersion == 1 && w.Service.Data.deskLayoutVersion == 1 && valid, error);
            if (restart)
            {
                var expected = JsonUtility.FromJson<Expected>(File.ReadAllText(Path.Combine(output, "expected.json")));
                var item = FindObjectsByType<PhysicalInteractable>(FindObjectsSortMode.None).First(i => i.itemInstanceId == expected.releaseId);
                Require("Restart restores release book desktop ownership", w.Service.GetItem(item.itemInstanceId).owner == CraftOwner.Desktop && Vector3.Distance(item.transform.position, expected.releasePosition) < .003f);
                Require("Restart preserves independent release page", w.Service.FindBook(w.BookIdFor(item)).currentPage == expected.releasePage);
                Require("Restart keeps caught finite instance exactly once", w.Service.Items.Count(i => i.instanceId == expected.caughtId) == 1);
                Require("Restart preserves seven independent books", j.Document.notebookLibrary.notebooks.Count == 7);
                Require("Restart does not duplicate drawer inventory", w.Service.Items.Select(i => i.instanceId).Distinct().Count() == w.Service.Items.Count);
                yield return Shot("restart-desktop"); yield break;
            }
            var items = FindObjectsByType<PhysicalInteractable>(FindObjectsSortMode.None);
            var shafts = FindObjectsByType<WorkspaceShaft>(FindObjectsSortMode.None);
            Require("Both under-desk drawers exist independently", layout.leftDrawer.drawerId == "desk-drawer-left" && layout.rightDrawer.drawerId == "desk-drawer-right" && layout.leftDrawer != layout.rightDrawer);
            Require("All original fifteen display shafts filled", shafts.Length == 15 && shafts.All(s => s.slot && s.slot.Occupant && s.slot.Occupant.itemType == PhysicalItemType.Tape));
            Require("Physical refill sheets all live in left drawer", items.Where(i => i.itemType == PhysicalItemType.LoosePage).Count() == 8 && items.Where(i => i.itemType == PhysicalItemType.LoosePage).All(i => i.homeSlot.GetComponentInParent<WorkspaceDrawer>() == layout.leftDrawer));
            Require("Sticker stock boxed in right drawer", items.Where(i => i.itemType == PhysicalItemType.Sticker).All(i => i.homeSlot.container && i.homeSlot.GetComponentInParent<WorkspaceDrawer>() == layout.rightDrawer));
            Require("Six independently selectable pens in pen case", items.Count(i => i.assetId.StartsWith("atelier_pen_", StringComparison.Ordinal) && i.homeSlot.GetComponentInParent<WorkspaceDrawer>() == layout.rightDrawer) == 6);
            Require("Dense desktop note organizer has twenty-four sheets", items.Count(i => i.itemType == PhysicalItemType.Note && i.homeSlot.container == layout.desktopNotes) == 24);
            Require("Seven separately bound spine-out notebooks", items.Count(i => i.itemType == PhysicalItemType.TravelersNotebook) == 7 && j.Document.notebookLibrary.notebooks.Count == 7);
            Require("Desktop box and dot-glue are real independent entities", layout.desktopBox && layout.glue && layout.glue.itemType == PhysicalItemType.Tool && w.Service.GetItem(layout.glue.itemInstanceId)?.kind == CraftItemKind.Tool);
            yield return Shot("desk-close");
            var notes = items.Where(i => i.itemType == PhysicalItemType.Note && i.homeSlot.GetComponentInParent<WorkspaceDrawer>() == layout.rightDrawer).ToArray();
            Require("Closed drawer gates every nested note box", !layout.rightDrawer.container.IsAccessibleHierarchy && notes.Length >= 18 && notes.All(i => !i.IsAccessible && !i.interactionCollider.enabled));
            layout.rightDrawer.SetOpenAmount(1); yield return Wait(.1f);
            Require("Open drawer exposes nested note boxes", notes.All(i => i.IsAccessible && i.interactionCollider.enabled));
            layout.leftDrawer.SetOpenAmount(1); yield return Wait(.1f);
            // Existing release-book homes remain in the original rack drawers; expose those
            // genuine mechanisms as well before testing whether a material can be reached.
            foreach(var drawer in items.Where(i=>i.itemType==PhysicalItemType.ReleaseBook&&i.CurrentSlot)
                .Select(i=>i.CurrentSlot.GetComponentInParent<WorkspaceDrawer>()).Where(d=>d).Distinct())drawer.SetOpenAmount(1);
            yield return Wait(.2f);
            foreach (DeskViewKind kind in Enum.GetValues(typeof(DeskViewKind)))
            { yield return View(kind); yield return Shot("view-" + kind); }
            var reachable = new HashSet<string>(StringComparer.Ordinal);
            foreach (DeskViewKind kind in new[] { DeskViewKind.Desk, DeskViewKind.Overview, DeskViewKind.TapeRack, DeskViewKind.RightStorage, DeskViewKind.LeftDrawer, DeskViewKind.RightDrawer })
            {
                yield return View(kind); Physics.SyncTransforms();
                foreach (var item in items) if (HitItem(item)) reachable.Add(item.itemInstanceId);
            }
            foreach (var type in new[] { PhysicalItemType.Tape, PhysicalItemType.LoosePage, PhysicalItemType.Note, PhysicalItemType.Sticker, PhysicalItemType.TravelersNotebook, PhysicalItemType.ReleaseBook, PhysicalItemType.Tool })
                Require("Actual collider ray reachable for " + type, items.Any(i => i.itemType == type && reachable.Contains(i.itemInstanceId)));
            Require("All seven notebook spines directly clickable", items.Where(i => i.itemType == PhysicalItemType.TravelersNotebook).All(i => reachable.Contains(i.itemInstanceId)));
            var paper = items.First(i => i.itemType == PhysicalItemType.LoosePage && reachable.Contains(i.itemInstanceId));
            yield return View(DeskViewKind.LeftDrawer);
            var originalPose = paper.CurrentWorldPose;
            Require("Selecting paper preserves pose and ownership", w.SelectPhysical(paper) && !w.physical.HeldItem && Vector3.Distance(originalPose.position, paper.transform.position) < .00001f && w.Service.GetItem(paper.itemInstanceId).owner == CraftOwner.Stock);
            yield return Wait(.15f); Require("Selected material stays at original height", Vector3.Distance(originalPose.position, paper.transform.position) < .00001f);
            w.ClearSelection(); yield return View(DeskViewKind.Desk);
            var release = items.First(i => i.itemType == PhysicalItemType.ReleaseBook);
            var surface = w.physical.surface; Pose place = default; bool found = false;
            foreach (float x in new[] { -.30f, -.38f, .38f, .43f })
            { foreach (float z in new[] { -.06f, -.12f, -.19f }) { var candidate = surface.RestingPose(release, surface.ToWorld(new Vector3(x, 0, z)), Quaternion.identity); if (surface.IsValidPlacement(release, candidate)) { place = candidate; found = true; break; } } if (found) break; }
            Require("A legal book-sized space exists beside central journal", found);
            var releaseStart = release.CurrentWorldPose;
            Require("Release book grab starts at original pose", w.GrabPhysical(release) && Vector3.Distance(release.transform.position, releaseStart.position) < .00001f && Quaternion.Angle(release.transform.rotation, releaseStart.rotation) < .001f);
            yield return Wait(.15f);
            Require("Grabbed book does not rise without a move intent", Vector3.Distance(release.transform.position, releaseStart.position) < .00001f);
            Require("Move release book to real tabletop pose", w.physical.Submit(new PhysicalInteractionIntent(PhysicalIntentKind.Move, release, place)), w.physical.LastResult);
            yield return Wait(.5f);
            Require("Drop release book onto desk", w.physical.Submit(new PhysicalInteractionIntent(PhysicalIntentKind.Release, release)), w.physical.LastResult); yield return Idle();
            Require("Release book settles beside journal and saves Desktop owner", !w.physical.HeldItem && Vector3.Distance(release.transform.position, place.position) < .004f && w.Service.GetItem(release.itemInstanceId).owner == CraftOwner.Desktop);
            string releaseId = w.BookIdFor(release); Require("Direct open release book on tabletop", w.OpenBook(releaseId, true)); yield return Wait(.1f);
            Require("Open book exposes real two-page spread", w.releaseBooks.First(b => b.physical == release).IsOpen); yield return Shot("release-open-beside-journal");
            Require("Inspect release book", w.InspectBook(release)); yield return Reading("Release book");
            Require("Inspect release exposes actual inner pages", w.bookInspection.IsActive && w.bookInspection.PageCount == 6);
            Require("Turn inspected release book without copying", w.TurnInspectedBook(1) && w.Service.FindBook(releaseId).currentPage == 2); yield return Shot("release-inspect-next-page");
            w.Cancel(); yield return Idle();
            var notebook = items.First(i => i.itemType == PhysicalItemType.TravelersNotebook); string central = j.Document.ActiveBookId;
            Require("Inspect TN without replacing central journal", w.InspectBook(notebook)); yield return Reading("TN");
            Require("TN inspection shows twenty-four real independent pages", w.bookInspection.IsActive && w.bookInspection.PageCount == 24);
            Require("Turn inspected TN saves only that notebook cursor", w.TurnInspectedBook(1) && j.Document.ActiveBookId == central && j.Document.notebookLibrary.notebooks.First(b => b.bookId == notebook.assetId).currentSpread == 1);
            yield return Shot("tn-inspect-next-page"); w.Cancel(); yield return Idle();
            var tape = shafts.First(s => s.slot.Occupant).slot.Occupant;
            Require("Tape selection leaves original roll on shaft", w.SelectPhysical(tape) && !w.physical.HeldItem && tape.CurrentSlot);
            Require("Forward pull starts measured tape choice", w.StartTapeLength(tape.transform.position)); w.SetTapeLength(.125f);
            Require("Length choice cuts exact metric strip, original roll stays", w.ConfirmTapeLength() && Mathf.Abs(w.Service.HeldItem.lengthMeters - .125f) < .00001f && tape.CurrentSlot);
            w.Cancel(); w.ClearSelection();
            var time = FindFirstObjectByType<DreamTimeController>(); time.SetNight(false, false, true); yield return Wait(.3f);
            j.EnterEditing(); yield return Idle();
            var dream = DreamGameplayController.Instance;
            Require("Day collection remains enabled during idle journal editing", dream.CurrentSetting == 1 && j.IsEditing && dream.CanCollect);
            j.ExitEditing(); yield return Idle();
            var collectible = dream.collection.Collectibles.FirstOrDefault(c => {
                if(!c||!c.gameObject.activeInHierarchy)return false;
                var p=w.lens.WorldToViewportPoint(c.transform.position);
                return p.z>0&&p.x>.03f&&p.x<.97f&&p.y>.03f&&p.y<.97f&&
                    Physics.Raycast(w.lens.ViewportPointToRay(p),out var hit,12)&&hit.collider.GetComponentInParent<NightCollectible>()==c;
            });
            Require("Day materials have real collider targets", collectible);
            int stockBefore = w.Service.Items.Count; int pendingBefore = dream.Data.pending.Count(t => t.setting == 1);
            Require("Day collectible ray grants finite material", dream.collection.TryClick(w.lens.ScreenPointToRay(w.lens.WorldToScreenPoint(collectible.transform.position))));
            string caught = w.Service.Items.LastOrDefault(i => i.instanceId.StartsWith("dream-", StringComparison.Ordinal))?.instanceId;
            Require("Day click decrements pending and adds exactly one instance", dream.Data.pending.Count(t => t.setting == 1) == pendingBefore - 1 && w.Service.Items.Count == stockBefore + 1);
            Require("Reward feedback visible without opening material bag", !dream.BagOpen && dream.Status.Contains("获得额外素材") && dream.FeedbackUntil > Time.unscaledTime);
            yield return Shot("day-reward-feedback");
            var sky = FindObjectsByType<FloatingMaterialTarget>(FindObjectsInactive.Include,FindObjectsSortMode.None);
            Require("Large sky pictures have explicit collection capability", sky.Length >= 6 && sky.All(s => s.hitCollider));
            AuditSky(sky,"sky-before.json");
            FloatingMaterialTarget skyClick=null; Ray skyRay=default; float skyUntil=Time.realtimeSinceStartup+60;
            while(!skyClick&&Time.realtimeSinceStartup<skyUntil)
            {
                foreach(var target in sky.Where(s=>s.CanClaim(1)&&s.hitCollider.enabled))
                {
                    var p=w.lens.WorldToViewportPoint(target.hitCollider.bounds.center);
                    if(p.z<=0||p.x<.03f||p.x>.97f||p.y<.05f||p.y>.94f)continue;
                    var ray=w.lens.ViewportPointToRay(p);
                    if(Physics.Raycast(ray,out var hit,12)&&hit.collider.GetComponentInParent<FloatingMaterialTarget>()==target){skyClick=target;skyRay=ray;break;}
                }
                if(!skyClick)yield return null;
            }
            AuditSky(sky,"sky-after.json");
            Require("A naturally passing large decoration is visible and collider reachable",skyClick);
            int skyStock=w.Service.Items.Count,skyPending=dream.Data.pending.Count(t=>t.setting==1);
            Require("Large daytime decoration actually grants one pending material",dream.collection.TryClick(skyRay)&&
                dream.Data.pending.Count(t=>t.setting==1)==skyPending-1&&w.Service.Items.Count==skyStock+1);
            Require("Same passing decoration consumes repeat click without duplicate reward",dream.collection.TryClick(skyRay)&&w.Service.Items.Count==skyStock+1);
            yield return Shot("day-large-floating-reward");
            Require("Final document keeps unique valid atomic ownership", CraftWorkspaceData.Validate(j.Document, out error) && w.Service.Items.Select(i => i.instanceId).Distinct().Count() == w.Service.Items.Count, error);
            w.coordinator.SyncPhysical(); Require("Flush complete snapshot", w.Service.Flush());
            File.WriteAllText(Path.Combine(output, "expected.json"), JsonUtility.ToJson(new Expected { releaseId = release.itemInstanceId, releasePosition = release.transform.position, releasePage = w.Service.FindBook(releaseId).currentPage, caughtId = caught, notebookSpread = 1 }, true));
        }
        void Finish()
        {
            if (finished) return; finished = true; Application.logMessageReceived -= Log;
            bool pass = checks.Count > 0 && checks.All(c => c.passed) && errors.Count == 0;
            File.WriteAllText(Path.Combine(output, restart ? "restart.json" : "result.json"), JsonUtility.ToJson(new Report { passed = pass, actualPlayMode = Application.isPlaying, nativeInputValidated = false, utc = DateTime.UtcNow.ToString("O"), unity = Application.unityVersion, gpu = SystemInfo.graphicsDeviceName, savePath = j ? j.SavePath : "", checks = checks.ToArray(), errors = errors.ToArray() }, true));
            if (completed != null) completed(pass); else Application.Quit(pass ? 0 : 1);
        }
    }
}
