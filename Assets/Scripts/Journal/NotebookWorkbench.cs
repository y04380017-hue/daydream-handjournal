using System.Collections;
using System.Linq;
using UnityEngine;

namespace VintageJournal
{
    /// <summary>Replaces only central book presentation. DeskCamera and existing page/decoration controllers remain authoritative.</summary>
    public sealed class NotebookWorkbench : MonoBehaviour
    {
        public WorkspaceGameplayController workspace;
        public JournalController journal;
        public PhysicalInteractable[] notebooks;
        public Renderer[] binderOnly;
        public GameObject boundCover;
        public Renderer[] boundCoverRenderers;
        public Vector3 binderScale;
        public string Status { get; private set; }
        public bool Ready { get; private set; }
        string presentedId;

        IEnumerator Start()
        {
            if (!workspace) workspace = FindFirstObjectByType<WorkspaceGameplayController>();
            if (!journal) journal = GetComponent<JournalController>();
            while (!workspace || !workspace.Ready) yield return null;
            if (notebooks == null || notebooks.Length == 0)
                notebooks = FindObjectsByType<PhysicalInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Where(i => i.itemType == PhysicalItemType.TravelersNotebook).OrderBy(i => i.assetId).ToArray();
            Ready = workspace.Service.InitializeNotebooks(notebooks.Select(i => new NotebookData {
                bookId = i.assetId, physicalInstanceId = i.itemInstanceId, displayName = i.displayName,
                pageSizeMeters = i.dimensions.pageSize }), journal.Catalog.defaultPaperId);
            RefreshPresentation(); journal.RefreshPages();
        }

        public bool Use(PhysicalInteractable item) => item && item.itemType == PhysicalItemType.TravelersNotebook && Use(item.assetId);
        public bool UseBinder() => Use(NotebookLibraryData.BinderId);
        public bool Use(string id)
        {
            if (!Ready || journal.IsBusy || journal.HasPaperPreview || workspace.TapeLengthSelecting ||
                workspace.Service.HeldItem != null || workspace.physical.IsSettling ||
                workspace.GetComponent<WorkspaceInputRouter>().IsDraggingMechanism)
            { Status = "先完成当前取放或翻页，再换本。"; return false; }
            var next = notebooks.FirstOrDefault(i => i.assetId == id);
            if (id != NotebookLibraryData.BinderId && (!next || !next.IsAccessible)) return false;
            if (workspace.physical.HeldItem)
            {
                if (workspace.physical.HeldItem != next) return false;
                if (!workspace.physical.RestoreGrabToSafePose(next)) return false;
            }
            workspace.scrapbook.Cancel(); workspace.scrapbook.Deselect();
            if (!workspace.scrapbook.FlushSaves()) return false;
            workspace.coordinator.SyncPhysical();
            var old = notebooks.FirstOrDefault(i => i.assetId == journal.Document.ActiveBookId);
            var home = old ? old.homeSlot : null;
            if (!workspace.Service.SwitchNotebook(id, home ? home.slotId : null, home ? home.WorldPose : default))
            { Status = workspace.Service.LastMessage; return false; }
            workspace.coordinator.ApplySavedState(); workspace.ClearSelection();
            journal.cameraRig.ResetView(); RefreshPresentation(); journal.RefreshPages();
            Status = id == NotebookLibraryData.BinderId ? "已换回活页本，原页面内容保留。" : "旅行手账已放到中央：固定装订，可翻页、贴纸和胶带。";
            workspace.desk.ConsumePointer(); RuntimeInputGate.Consume();
            Debug.Log("[Notebook] Active:" + id);
            return true;
        }

        public bool IsCentralDrop(Ray ray)
        {
            if (!journal || !journal.pageView) return false;
            foreach (JournalSide side in new[] { JournalSide.Left, JournalSide.Right })
            {
                var anchor = journal.pageView.GetSurface(side); var size = journal.pageView.SurfaceSize(side);
                var plane = new Plane(anchor.forward, anchor.position);
                if (!plane.Raycast(ray, out float d)) continue;
                var p = anchor.InverseTransformPoint(ray.GetPoint(d));
                if (p.x >= -.025f && p.y >= -.025f && p.x <= size.x + .025f && p.y <= size.y + .025f) return true;
            }
            return false;
        }

        public void RefreshPresentation()
        {
            if (!journal || journal.Document == null) return;
            var id = journal.Document.ActiveBookId;
            if (presentedId == id) return;
            if (binderScale == Vector3.zero) binderScale = transform.localScale;
            var book = journal.Document.ActiveNotebook; bool tn = book != null;
            foreach (var r in binderOnly) if (r) r.enabled = !tn;
            if (boundCover) boundCover.SetActive(tn);
            transform.localScale = tn ? new Vector3(binderScale.x * book.pageSizeMeters.x / .148f,
                binderScale.y, binderScale.z * book.pageSizeMeters.y / .210f) : binderScale;
            Color[] colors = { new Color(.50f,.83f,.72f), new Color(1,.66f,.55f), new Color(.72f,.65f,.94f), new Color(.53f,.79f,.93f) };
            int index = notebooks == null ? 0 : System.Array.FindIndex(notebooks, i => i && i.assetId == id);
            var block = new MaterialPropertyBlock(); block.SetColor("_BaseColor", colors[Mathf.Clamp(index,0,3)]);
            foreach (var r in boundCoverRenderers) if (r) r.SetPropertyBlock(block);
            presentedId = id;
        }
    }
}
