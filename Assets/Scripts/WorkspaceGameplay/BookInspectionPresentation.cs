using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace VintageJournal
{
    /// <summary>Read-only 3D presentation of a held book's real pages. Page turns save only its own cursor.</summary>
    public sealed class BookInspectionPresentation : MonoBehaviour
    {
        public WorkspaceGameplayController workspace;
        public PhysicalInteractable Item { get; private set; }
        public bool IsActive => Item && workspace && workspace.physical && workspace.physical.HeldItem == Item && workspace.physical.IsInspecting;
        public int CurrentSpread { get; private set; }
        public int PageCount { get; private set; }
        [Range(.35f,.60f)] public float inspectionDistance = .42f;
        public Vector2 inspectionViewportCentre = new Vector2(.5f,.43f);
        public Pose InspectionPose => readingAnchor ? new Pose(readingAnchor.position,readingAnchor.rotation) : default;
        public bool ReadyForReading => IsActive && readingAnchor &&
            Vector3.Distance(Item.transform.position,readingAnchor.position)<.008f &&
            Quaternion.Angle(Item.transform.rotation,readingAnchor.rotation)<3;
        GameObject preview;
        Transform readingAnchor;
        readonly List<Transform> paperSurfaces = new List<Transform>();
        readonly List<Material> materials = new List<Material>();
        readonly List<Mesh> meshes = new List<Mesh>();
        bool originalOpen, originalVisualActive;
        Font font;
        GUIStyle text, button;
        readonly Rect controls = new Rect(430, 106, 1060, 132);

        /// <summary>A separate item target in the current seated view; never writes the camera or permanent home.</summary>
        public Transform InspectionAnchorFor(PhysicalInteractable item)
        {
            if(!readingAnchor)
            {readingAnchor=new GameObject("Book reading target").transform;readingAnchor.SetParent(transform,false);}
            var lens=workspace.lens;
            var ray=lens.ViewportPointToRay(new Vector3(inspectionViewportCentre.x,inspectionViewportCentre.y,0));
            Vector3 centre=lens.transform.position+ray.direction*Mathf.Clamp(inspectionDistance,.35f,.60f);
            Quaternion rotation=Quaternion.LookRotation(lens.transform.up,-lens.transform.forward);
            Vector3 localCentre=Vector3.zero;
            var release=item.GetComponent<ReleaseBookView>();
            if(release&&release.pageAnchors.Length>0)
            {
                int count=0;
                foreach(var anchor in release.pageAnchors)
                {
                    if(!anchor)continue;var size=ReleaseBookView.LocalPageSize(anchor,release.pageSize);
                    localCentre+=item.transform.InverseTransformPoint(anchor.TransformPoint(new Vector3(size.x*.5f,size.y*.5f,0)));count++;
                }
                if(count>0)localCentre/=count;
            }
            readingAnchor.SetPositionAndRotation(centre-rotation*localCentre,rotation);
            return readingAnchor;
        }

        public void Begin(PhysicalInteractable item, bool wasOpen)
        {
            if (Item) End();
            Item = item; originalOpen = wasOpen;
            originalVisualActive = item.visualRoot && item.visualRoot.gameObject.activeSelf;
            var binding = item.GetComponent<TravelersNotebookBinding>(); if (binding) binding.SetInspectionOpen(true);
            Rebuild();
            Debug.Log("[BookInspection] Open:" + item.itemInstanceId + ":spread=" + CurrentSpread);
        }
        public bool Turn(int direction)
        {
            if (!IsActive || direction == 0) return false;
            bool changed;
            string releaseId = workspace.BookIdFor(Item);
            if (releaseId != null) changed = workspace.TurnBook(releaseId, direction);
            else
            {
                var book = workspace.Service.Document.notebookLibrary?.notebooks.FirstOrDefault(b => b.bookId == Item.assetId);
                if (book == null) return false;
                int next = Mathf.Clamp(book.currentSpread + (direction > 0 ? 1 : -1), 0, book.pages.Length / 2 - 1);
                changed = next != book.currentSpread && workspace.Service.SetInspectedNotebookSpread(book.bookId, next);
            }
            if (changed) { Rebuild(); Debug.Log("[BookInspection] Page:" + Item.itemInstanceId + ":spread=" + CurrentSpread); }
            return changed;
        }
        public void End()
        {
            var old = Item;
            Item = null; ClearPreview();
            if (!old) return;
            var binding = old.GetComponent<TravelersNotebookBinding>(); if (binding) binding.SetInspectionOpen(false);
            if (old.visualRoot) old.visualRoot.gameObject.SetActive(originalVisualActive);
            string releaseId = workspace && workspace.Ready ? workspace.BookIdFor(old) : null;
            if (releaseId != null) workspace.Service.SetBookOpen(releaseId, originalOpen);
            Debug.Log("[BookInspection] Close:" + old.itemInstanceId);
        }
        void LateUpdate()
        {
            if (Item && !IsActive) { End(); return; }
            if (preview && IsActive)
                preview.transform.SetPositionAndRotation(Item.transform.position-workspace.lens.transform.forward*.006f, workspace.lens.transform.rotation);
        }
        /// <summary>Actual live page corners, suitable for viewport/readability acceptance in every fixed view.</summary>
        public bool TryGetReadingViewport(out Rect bounds)
        {
            bounds=default;if(!IsActive)return false;
            var points=new List<Vector3>();
            var release=Item.GetComponent<ReleaseBookView>();
            if(release)
                foreach(var anchor in release.pageAnchors)
                {
                    if(!anchor)continue;var size=ReleaseBookView.LocalPageSize(anchor,release.pageSize);
                    foreach(var corner in new[]{Vector2.zero,Vector2.up,Vector2.one,Vector2.right})
                        points.Add(anchor.TransformPoint(new Vector3(size.x*corner.x,size.y*corner.y,0)));
                }
            else foreach(var paper in paperSurfaces)
                foreach(var corner in new[]{new Vector2(-.5f,-.5f),new Vector2(-.5f,.5f),new Vector2(.5f,.5f),new Vector2(.5f,-.5f)})
                    points.Add(paper.TransformPoint(new Vector3(corner.x,corner.y,0)));
            if(points.Count==0)return false;
            Vector2 min=new Vector2(float.MaxValue,float.MaxValue),max=new Vector2(float.MinValue,float.MinValue);
            bool visible=true;
            foreach(var point in points)
            {
                var p=workspace.lens.WorldToViewportPoint(point);min=Vector2.Min(min,p);max=Vector2.Max(max,p);
                visible&=p.z>workspace.lens.nearClipPlane&&p.x>.02f&&p.x<.98f&&p.y>.02f&&p.y<.78f;
            }
            bounds=Rect.MinMaxRect(min.x,min.y,max.x,max.y);
            return visible;
        }
        public bool Contains(Vector2 screen)
        { return IsActive && controls.Contains(new Vector2(screen.x * 1920 / Screen.width, (Screen.height - screen.y) * 1080 / Screen.height)); }
        void OnGUI()
        {
            if (!IsActive) return;
            if (!font)
            {
                font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Arial" }, 22);
                text = new GUIStyle(GUI.skin.label) { font = font, fontSize = 21, alignment = TextAnchor.MiddleCenter };
                text.normal.textColor = DaydreamPalette.Ink;
                button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 21 };
            }
            var matrix = GUI.matrix; var oldDepth = GUI.depth;
            GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / 1920f, Screen.height / 1080f, 1)); GUI.depth = -171;
            try
            {
                GUI.Box(controls, "");
                GUI.Label(new Rect(690, 112, 540, 54), Item.displayName + " · " + (CurrentSpread * 2 + 1) + "–" + (CurrentSpread * 2 + 2) + " / " + PageCount, text);
                GUI.enabled = CurrentSpread > 0;
                if (GUI.Button(new Rect(442, 116, 225, 50), "← 上一页", button)) Turn(-1);
                GUI.enabled = (CurrentSpread + 1) * 2 < PageCount;
                if (GUI.Button(new Rect(1238, 116, 225, 50), "下一页 →", button)) Turn(1);
                GUI.enabled = true;
                if (GUI.Button(new Rect(800, 188, 320, 46), "结束查看 / ESC", button)) workspace.Cancel();
            }
            finally { GUI.enabled = true; GUI.matrix = matrix; GUI.depth = oldDepth; }
        }
        void Rebuild()
        {
            ClearPreview(); if (!Item || !workspace || !workspace.Ready) return;
            string releaseId = workspace.BookIdFor(Item);
            if (releaseId != null)
            {
                var book = workspace.Service.FindBook(releaseId); CurrentSpread = book.currentPage / 2; PageCount = book.pages.Count;
                return; // The real release-book anchors already render its real finite materials.
            }
            var notebook = workspace.Service.Document.notebookLibrary?.notebooks.FirstOrDefault(b => b.bookId == Item.assetId);
            if (notebook == null) return;
            CurrentSpread = notebook.currentSpread; PageCount = notebook.pages.Length;
            if (Item.visualRoot) Item.visualRoot.gameObject.SetActive(false);
            preview = new GameObject("Inspected real notebook spread");
            preview.transform.SetPositionAndRotation(Item.transform.position-workspace.lens.transform.forward*.006f, workspace.lens.transform.rotation);
            Vector2 size = notebook.pageSizeMeters;
            for (int side = 0; side < 2; side++)
            {
                var page = notebook.pages[CurrentSpread * 2 + side];
                float centre = (side == 0 ? -1 : 1) * (size.x * .5f + .002f);
                var definition = workspace.journal.Catalog.Find(page.paperId);
                paperSurfaces.Add(Quad("Paper " + page.pageId, new Vector3(centre, 0, 0), size, 0, definition?.texture, false, 1, 0));
                foreach (var decoration in page.decorations.OrderBy(d => d.sortOrder))
                {
                    var art = workspace.scrapbook.Catalog.Find(decoration.assetId); if (art == null || !art.texture) continue;
                    Vector2 actual = Vector2.Scale(DecorationRenderer.EffectiveSize(art, decoration), size);
                    bool tape = art.supportsLengthAdjustment;
                    float aspect = (float)art.texture.width / Mathf.Max(1, art.texture.height);
                    Quad("Inspect art " + decoration.instanceId,
                        new Vector3(centre + (decoration.position.x - .5f) * size.x, (decoration.position.y - .5f) * size.y,
                            -.001f - Mathf.Max(0, decoration.sortOrder) * .00001f),
                        actual, decoration.rotationDegrees, art.texture, tape, tape ? actual.x / Mathf.Max(.00001f, actual.y) / aspect : 1, decoration.sortOrder + 1);
                }
            }
        }
        Transform Quad(string name, Vector3 position, Vector2 size, float rotation, Texture texture, bool tape, float repeat, int order)
        {
            var root = new GameObject(name); root.transform.SetParent(preview.transform, false);
            root.transform.localPosition = position; root.transform.localRotation = Quaternion.Euler(0, 0, rotation);
            root.transform.localScale = new Vector3(size.x, size.y, 1);
            var mesh = new Mesh { name = "Inspection page surface" };
            mesh.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0), new Vector3(.5f, -.5f, 0) };
            mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right }; mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 }; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            root.AddComponent<MeshFilter>().sharedMesh = mesh; meshes.Add(mesh);
            var renderer = root.AddComponent<MeshRenderer>(); renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            var shader = workspace.scrapbook.Catalog.paperShader;
            if (!shader) shader = Shader.Find("VintageJournal/ScrapbookPaper");
            if (!shader) shader = Shader.Find("Universal Render Pipeline/Unlit");
            var material = new Material(shader); materials.Add(material);
            material.SetTexture("_BaseMap", texture); material.SetColor("_BaseColor", Color.white); material.SetFloat("_Opacity", 1);
            material.SetFloat("_IsTape", tape ? 1 : 0); material.SetFloat("_UVRepeat", repeat); material.SetFloat("_Shadow", 0);
            material.renderQueue = 3100; renderer.sharedMaterial = material; renderer.sortingOrder = Mathf.Clamp(order, 0, 650);
            return root.transform;
        }
        void ClearPreview()
        {
            if (preview) Destroy(preview); preview = null;
            paperSurfaces.Clear();
            foreach (var material in materials) if (material) Destroy(material); materials.Clear();
            foreach (var mesh in meshes) if (mesh) Destroy(mesh); meshes.Clear();
        }
        void OnDestroy() { if (Item && Item.visualRoot) Item.visualRoot.gameObject.SetActive(originalVisualActive); ClearPreview(); if (font) Destroy(font); }
    }
}
