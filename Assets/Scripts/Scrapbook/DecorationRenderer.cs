using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VintageJournal
{
    /// <summary>Page-attached decoration meshes. Data and input remain owned by the scrapbook controller.</summary>
    [DisallowMultipleComponent]
    public sealed class DecorationRenderer : MonoBehaviour
    {
        public JournalController journal;
        public DecorationCatalog catalog;
        public int VisibleCount { get; private set; }
        public int MissingAssetCount { get; private set; }
        public bool HasPreview => previewRequested && preview != null && preview.group.activeSelf;
        public bool PreviewValid { get; private set; }

        sealed class Visual
        {
            public GameObject group;
            public Transform art, shadow;
            public MeshRenderer artRenderer, shadowRenderer;
            public Material artMaterial, shadowMaterial;
            public bool visible;
        }

        readonly Dictionary<string, Visual> visuals = new Dictionary<string, Visual>();
        readonly HashSet<string> missing = new HashSet<string>();
        readonly HashSet<string> liveIds = new HashSet<string>();
        readonly List<string> staleIds = new List<string>();
        Mesh quad;
        Visual preview;
        bool previewRequested, previousFlip;
        string selectedId;
        JournalDocument previousDocument;
        int previousSpread = -1;
        static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int Opacity = Shader.PropertyToID("_Opacity");
        static readonly int IsTape = Shader.PropertyToID("_IsTape");
        static readonly int Repeat = Shader.PropertyToID("_UVRepeat");
        static readonly int Shadow = Shader.PropertyToID("_Shadow");

        void Start() { Refresh(); }

        bool Ready()
        {
            if (!journal) journal = GetComponent<JournalController>();
            if (!catalog) catalog = DecorationCatalog.Load();
            return journal && journal.Document != null && journal.pageView && catalog && journal.pageView.Initialize();
        }

        /// <summary>Final dimensions in normalized physical-page space, with a uniform aspect-preserving scale.</summary>
        public static Vector2 EffectiveSize(DecorationDefinition definition, JournalDecorationData data)
        {
            if (definition == null || data == null) return Vector2.zero;
            Vector2 size = data.size.x > 0 && data.size.y > 0 ? data.size : definition.defaultSize;
            float scale = FinitePositive(data.scale.x) ? data.scale.x : 1;
            scale = data.physicalMaterial ? Mathf.Clamp(scale,.1f,3f) : Mathf.Clamp(scale, Mathf.Max(.01f, definition.minScale), Mathf.Max(definition.minScale, definition.maxScale));
            size *= scale;
            if (definition.supportsLengthAdjustment)
                size.x *= Mathf.Clamp(FinitePositive(data.length) ? data.length : 1, .3f, 2.5f);
            return size;
        }

        static bool FinitePositive(float value) => value > 0 && !float.IsInfinity(value) && !float.IsNaN(value);

        public void Refresh()
        {
            if (!Ready()) return;
            previousDocument = journal.Document;
            previousSpread = journal.Document.ActiveSpread;
            liveIds.Clear(); missing.Clear();
            foreach (JournalSide side in new[] { JournalSide.Left, JournalSide.Right })
            {
                var page = journal.Document.GetPage(side);
                if (page == null || page.decorations == null) continue;
                var ordered = new List<JournalDecorationData>(page.decorations);
                ordered.RemoveAll(record => record == null);
                ordered.Sort((a, b) => a.sortOrder != b.sortOrder ? a.sortOrder.CompareTo(b.sortOrder) :
                    string.CompareOrdinal(a.instanceId, b.instanceId));
                for (int rank = 0; rank < ordered.Count; rank++)
                {
                    var record = ordered[rank];
                    if (record == null || string.IsNullOrEmpty(record.instanceId)) continue;
                    var definition = catalog.Find(record.assetId);
                    if (definition == null || !definition.IsAvailable) { missing.Add(record.assetId ?? ""); continue; }
                    liveIds.Add(record.instanceId);
                    if (!visuals.TryGetValue(record.instanceId, out var visual))
                    {
                        visual = CreateVisual("Decoration " + record.instanceId);
                        visuals.Add(record.instanceId, visual);
                    }
                    UpdateVisual(visual, definition, record, side, rank, false);
                    visual.visible = true;
                }
            }
            staleIds.Clear();
            foreach (var pair in visuals)
                if (!liveIds.Contains(pair.Key)) staleIds.Add(pair.Key);
            foreach (string id in staleIds) { DisposeVisual(visuals[id]); visuals.Remove(id); }
            MissingAssetCount = missing.Count;
            ApplyVisibility();
        }

        void LateUpdate()
        {
            if (!Ready()) return;
            bool flipping = journal.flip && journal.flip.IsFlipping;
            if (journal.Document != previousDocument || journal.Document.ActiveSpread != previousSpread) Refresh();
            if (flipping != previousFlip) ApplyVisibility();
        }

        void ApplyVisibility()
        {
            bool flipping = journal && journal.flip && journal.flip.IsFlipping;
            previousFlip = flipping;
            VisibleCount = 0;
            foreach (var visual in visuals.Values)
            {
                bool visible = visual.visible && !flipping && enabled;
                visual.group.SetActive(visible);
                if (visible) VisibleCount++;
            }
            if (preview != null) preview.group.SetActive(previewRequested && PreviewValid && !flipping && enabled);
        }

        public void SetPreview(DecorationDefinition definition, JournalSide side, Vector2 position, float length, bool valid)
        {
            if (definition == null) { ClearPreview(); return; }
            SetPreviewData(definition, side, new JournalDecorationData {
                assetId = definition.assetId, size = definition.defaultSize, position = position,
                scale = Vector2.one, length = length
            }, valid);
        }

        public void SetPreviewData(DecorationDefinition definition, JournalSide side, JournalDecorationData data, bool valid)
        {
            if (!Ready() || definition == null || !definition.IsAvailable || data == null) { ClearPreview(); return; }
            if (preview == null) preview = CreateVisual("Decoration preview (runtime)");
            previewRequested = true; PreviewValid = valid;
            UpdateVisual(preview, definition, data, side, 650, true);
            ApplyVisibility();
        }

        public void ClearPreview()
        {
            previewRequested = false; PreviewValid = false;
            if (preview != null) preview.group.SetActive(false);
        }

        public void SetSelected(string instanceId)
        {
            selectedId = instanceId;
            foreach (var pair in visuals)
                pair.Value.artMaterial.SetColor(BaseColor, pair.Key == selectedId ? new Color(1, .985f, .94f, 1) : Color.white);
        }

        public Transform GetVisual(string instanceId)
        { return instanceId != null && visuals.TryGetValue(instanceId, out var visual) ? visual.group.transform : null; }

        public Renderer GetRenderer(string instanceId)
        { return instanceId != null && visuals.TryGetValue(instanceId, out var visual) ? visual.artRenderer : null; }

        Visual CreateVisual(string name)
        {
            if (!quad)
            {
                quad = new Mesh { name = "Runtime scrapbook unit quad" };
                quad.vertices = new[] { new Vector3(-.5f,-.5f,0), new Vector3(-.5f,.5f,0), new Vector3(.5f,.5f,0), new Vector3(.5f,-.5f,0) };
                quad.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
                quad.triangles = new[] { 0,1,2,0,2,3 }; quad.RecalculateNormals(); quad.RecalculateBounds();
            }
            Shader shader = catalog.paperShader ? catalog.paperShader : Shader.Find("VintageJournal/ScrapbookPaper");
            if (!shader) shader = Shader.Find("Universal Render Pipeline/Unlit");
            var visual = new Visual { group = new GameObject(name) };
            visual.group.transform.SetParent(transform, false);
            visual.art = CreatePlane("Artwork", visual.group.transform, shader, out visual.artRenderer, out visual.artMaterial);
            visual.shadow = CreatePlane("Soft contact", visual.group.transform, shader, out visual.shadowRenderer, out visual.shadowMaterial);
            visual.shadowMaterial.SetFloat(Shadow, 1);
            visual.shadowMaterial.SetColor(BaseColor, new Color(.20f, .15f, .09f, .15f));
            return visual;
        }

        Transform CreatePlane(string name, Transform parent, Shader shader, out MeshRenderer renderer, out Material material)
        {
            var plane = new GameObject(name); plane.transform.SetParent(parent, false);
            plane.AddComponent<MeshFilter>().sharedMesh = quad;
            renderer = plane.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            material = new Material(shader) { name = "Runtime " + name };
            renderer.sharedMaterial = material;
            return plane.transform;
        }

        void UpdateVisual(Visual visual, DecorationDefinition definition, JournalDecorationData data, JournalSide side, int rank, bool ghost)
        {
            Transform anchor = journal.pageView.GetSurface(side);
            Vector2 pageSize = journal.pageView.SurfaceSize(side);
            Vector2 size = Vector2.Scale(EffectiveSize(definition, data), pageSize);
            var t = visual.group.transform;
            int layer = Mathf.Clamp(rank, 0, 650);
            if(data.physicalMaterial)
            {
                Vector2 metres=new Vector2(anchor.TransformVector(Vector3.right*pageSize.x).magnitude,anchor.TransformVector(Vector3.up*pageSize.y).magnitude);
                size=Vector2.Scale(EffectiveSize(definition,data),metres);
                // Metric artwork is a world-space child of the page pose, avoiding skew from legacy nonuniform visual fitting.
                t.SetParent(null,false);
                t.position=anchor.TransformPoint(new Vector3(data.position.x*pageSize.x,data.position.y*pageSize.y,0))-anchor.forward*(.00055f+layer*.000002f);
                t.rotation=anchor.rotation*Quaternion.Euler(0,0,data.rotationDegrees);
            }
            else
            {
                t.SetParent(anchor, false);
                t.localPosition = new Vector3(data.position.x * pageSize.x, data.position.y * pageSize.y, -.0014f - layer * .000004f);
                t.localRotation = Quaternion.Euler(0, 0, data.rotationDegrees);
            }
            t.localScale = Vector3.one;
            visual.art.localScale = new Vector3(size.x, size.y, 1);
            visual.shadow.localScale = new Vector3(size.x * 1.025f, size.y * 1.025f, 1);
            visual.shadow.localPosition = new Vector3(.0005f, -.0007f, .0002f);
            visual.artMaterial.SetTexture(BaseMap, definition.texture);
            visual.shadowMaterial.SetTexture(BaseMap, definition.texture);
            float repeats = definition.supportsLengthAdjustment
                ? size.x / Mathf.Max(.0001f, size.y) / ((float)definition.texture.width / definition.texture.height) : 1;
            visual.artMaterial.SetFloat(Repeat, repeats);
            visual.shadowMaterial.SetFloat(Repeat, repeats);
            visual.artMaterial.SetFloat(IsTape, definition.supportsLengthAdjustment ? 1 : 0);
            visual.shadowMaterial.SetFloat(IsTape, definition.supportsLengthAdjustment ? 1 : 0);
            visual.artMaterial.SetFloat(Opacity, ghost ? .57f : 1);
            visual.shadowMaterial.SetFloat(Opacity, ghost ? .35f : 1);
            visual.artMaterial.SetColor(BaseColor, !ghost && data.instanceId == selectedId ? new Color(1, .985f, .94f, 1) : Color.white);
            visual.shadowMaterial.renderQueue = 3000 + layer * 2;
            visual.artMaterial.renderQueue = 3001 + layer * 2;
            visual.shadowRenderer.sortingOrder = layer * 2;
            visual.artRenderer.sortingOrder = layer * 2 + 1;
        }

        void OnDisable()
        {
            foreach (var visual in visuals.Values) if (visual.group) visual.group.SetActive(false);
            if (preview != null && preview.group) preview.group.SetActive(false);
        }
        void OnEnable() { if (journal && journal.Document != null) Refresh(); }
        void OnDestroy()
        {
            foreach (var visual in visuals.Values) DisposeVisual(visual);
            visuals.Clear(); DisposeVisual(preview);
            DestroyOwned(quad);
        }
        static void DisposeVisual(Visual visual)
        {
            if (visual == null) return;
            DestroyOwned(visual.artMaterial); DestroyOwned(visual.shadowMaterial); DestroyOwned(visual.group);
        }
        static void DestroyOwned(Object value)
        { if (!value) return; if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
    }
}
